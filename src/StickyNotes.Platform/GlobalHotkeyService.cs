using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using StickyNotes.Core.Abstractions;

namespace StickyNotes.Platform;

/// <summary>
/// Atajos de teclado globales mediante <c>RegisterHotKey</c> sobre una ventana sólo-mensaje
/// (<see cref="HwndSource"/> con padre <c>HWND_MESSAGE</c>).
/// <para>
/// IMPORTANTE: debe crearse, usarse y liberarse en el hilo de UI (con <see cref="Dispatcher"/> en ejecución).
/// Los callbacks se invocan en ese mismo hilo, desde el bucle de mensajes.
/// </para>
/// Formato de atajo: modificadores + tecla separados por '+', p. ej. "Ctrl+Alt+N", "Ctrl+Shift+F12",
/// "Win+Alt+S". Modificadores: Ctrl/Control, Alt, Shift/Mayús, Win/Windows. La tecla es un nombre de
/// <see cref="Key"/> ("N", "F12", "Space", "OemPlus"…) o un dígito "0"–"9". Sin modificadores sólo se aceptan F1–F24.
/// Se usa <c>MOD_NOREPEAT</c> para no repetir al mantener pulsado.
/// </summary>
public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private readonly HwndSource _source;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<int, Action> _callbacks = [];
    private int _nextId = 1;
    private bool _disposed;

    /// <summary>Crea la ventana sólo-mensaje. Llamar en el hilo de UI.</summary>
    public GlobalHotkeyService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        var parameters = new HwndSourceParameters("Kip.GlobalHotkeys")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <inheritdoc />
    public bool Register(string gesture, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (_disposed)
        {
            return false;
        }

        if (!TryParseGesture(gesture, out uint modifiers, out uint virtualKey))
        {
            Trace.TraceWarning($"GlobalHotkeyService: atajo inválido '{gesture}'.");
            return false;
        }

        if (!_dispatcher.CheckAccess())
        {
            return _dispatcher.Invoke(() => Register(gesture, callback));
        }

        int id = _nextId++;
        if (NativeMethods.RegisterHotKey(_source.Handle, id, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey) == 0)
        {
            Trace.TraceWarning($"GlobalHotkeyService: RegisterHotKey('{gesture}') falló (¿ocupado?). Win32 error {Marshal.GetLastPInvokeError()}.");
            return false;
        }

        _callbacks[id] = callback;
        return true;
    }

    /// <inheritdoc />
    public void UnregisterAll()
    {
        if (_disposed)
        {
            return;
        }

        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(UnregisterAll);
            return;
        }

        foreach (int id in _callbacks.Keys)
        {
            if (NativeMethods.UnregisterHotKey(_source.Handle, id) == 0)
            {
                Trace.TraceWarning($"GlobalHotkeyService: UnregisterHotKey({id}) falló. Win32 error {Marshal.GetLastPInvokeError()}.");
            }
        }

        _callbacks.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_dispatcher.CheckAccess())
            {
                DisposeCore();
            }
            else if (!_dispatcher.HasShutdownFinished)
            {
                _dispatcher.Invoke(DisposeCore);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"GlobalHotkeyService.Dispose: {ex}");
        }

        GC.SuppressFinalize(this);
    }

    private void DisposeCore()
    {
        if (_disposed)
        {
            return;
        }

        UnregisterAll();
        _disposed = true;
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    /// <summary>
    /// Convierte un atajo textual en modificadores Win32 (MOD_*) y código de tecla virtual.
    /// Función pura, sin efectos secundarios.
    /// </summary>
    internal static bool TryParseGesture(string? gesture, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(gesture))
        {
            return false;
        }

        string[] parts = gesture.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(p => p.Length == 0))
        {
            return false;
        }

        for (int i = 0; i < parts.Length - 1; i++)
        {
            uint mod = parts[i].ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" or "CTL" => NativeMethods.MOD_CONTROL,
                "ALT" => NativeMethods.MOD_ALT,
                "SHIFT" or "MAYÚS" or "MAYUS" => NativeMethods.MOD_SHIFT,
                "WIN" or "WINDOWS" or "META" => NativeMethods.MOD_WIN,
                _ => 0,
            };
            if (mod == 0 || (modifiers & mod) != 0)
            {
                return false; // modificador desconocido o repetido
            }

            modifiers |= mod;
        }

        if (!TryParseKey(parts[^1], out Key key))
        {
            return false;
        }

        bool isFunctionKey = key is >= Key.F1 and <= Key.F24;
        if (modifiers == 0 && !isFunctionKey)
        {
            return false;
        }

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk <= 0)
        {
            return false;
        }

        virtualKey = (uint)vk;
        return true;
    }

    private static bool TryParseKey(string token, out Key key)
    {
        key = Key.None;
        if (token.Length == 1 && char.IsAsciiDigit(token[0]))
        {
            key = Key.D0 + (token[0] - '0');
            return true;
        }

        // Evita que Enum.TryParse acepte valores numéricos ("65").
        if (token.All(char.IsAsciiDigit) || token.StartsWith('-'))
        {
            return false;
        }

        string normalized = token.ToUpperInvariant() switch
        {
            "ESC" => nameof(Key.Escape),
            "DEL" or "SUPR" => nameof(Key.Delete),
            "INS" => nameof(Key.Insert),
            "PGUP" => nameof(Key.PageUp),
            "PGDN" => nameof(Key.PageDown),
            "ENTER" => nameof(Key.Return),
            "PLUS" => nameof(Key.OemPlus),
            "MINUS" => nameof(Key.OemMinus),
            _ => token,
        };

        if (!Enum.TryParse(normalized, ignoreCase: true, out key) || !Enum.IsDefined(key))
        {
            return false;
        }

        // Las teclas modificadoras no pueden ser la tecla principal.
        return key is not (Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _callbacks.TryGetValue((int)wParam, out Action? callback))
        {
            handled = true;
            try
            {
                callback();
            }
            catch (Exception ex)
            {
                Trace.TraceError($"GlobalHotkeyService: el callback del atajo lanzó: {ex}");
            }
        }

        return 0;
    }
}

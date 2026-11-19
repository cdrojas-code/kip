using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using StickyNotes.Core.Abstractions;

namespace StickyNotes.Platform;

/// <summary>
/// Detección heurística de pantalla compartida (mitigación principal en Windows 10 LTSC 2019, donde
/// <c>WDA_EXCLUDEFROMCAPTURE</c> no existe). Cada ~1,5 s enumera las ventanas visibles de nivel superior
/// y las compara con <see cref="ShareSignatures.Default"/>.
/// <para>
/// Hilos: <see cref="Start"/> debe llamarse desde el hilo de UI (con <see cref="Dispatcher"/>); el sondeo
/// usa un <see cref="DispatcherTimer"/> de ese hilo y enumera las ventanas en el pool, pero
/// <see cref="SharingChanged"/> se dispara siempre en el hilo de UI.
/// </para>
/// <para>
/// Antirrebote: <see cref="IsSharing"/> pasa a true en cuanto un sondeo es positivo y sólo vuelve a false
/// tras <see cref="NegativePollsToClear"/> sondeos negativos consecutivos.
/// </para>
/// </summary>
public sealed class ScreenShareDetector : IScreenShareDetector
{
    /// <summary>Intervalo de sondeo.</summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1500);

    /// <summary>Sondeos negativos consecutivos necesarios para considerar que se dejó de compartir.</summary>
    internal const int NegativePollsToClear = 2;

    /// <summary>Cada cuántos sondeos se vacía la caché pid→nombre (los PIDs se reutilizan).</summary>
    private const int CacheResetEveryPolls = 40;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new(StringComparer.Ordinal);

    private readonly IReadOnlyList<ShareSignature> _signatures;
    private readonly HashSet<string> _signatureProcesses;
    private readonly Dictionary<uint, string> _processNameCache = [];
    private readonly uint _ownPid = (uint)Environment.ProcessId;

    private DispatcherTimer? _timer;
    private int _negativeStreak;
    private int _pollCount;
    private bool _pollInFlight;
    private bool _disposed;

    /// <summary>Crea el detector con las firmas por defecto (<see cref="ShareSignatures.Default"/>).</summary>
    public ScreenShareDetector()
        : this(ShareSignatures.Default)
    {
    }

    /// <summary>Constructor para pruebas o firmas personalizadas.</summary>
    internal ScreenShareDetector(IReadOnlyList<ShareSignature> signatures)
    {
        _signatures = signatures ?? throw new ArgumentNullException(nameof(signatures));
        _signatureProcesses = new HashSet<string>(
            signatures.Select(s => NormalizeProcessName(s.ProcessName)),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool IsSharing { get; private set; }

    /// <inheritdoc />
    public event EventHandler<bool>? SharingChanged;

    /// <inheritdoc />
    /// <remarks>Debe llamarse en el hilo de UI. Idempotente. Ejecuta un primer sondeo inmediato.</remarks>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_timer is not null)
        {
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }

            return;
        }

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = PollInterval };
        _timer.Tick += OnTick;
        _timer.Start();
        OnTick(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    /// <remarks>Detiene el sondeo. No modifica <see cref="IsSharing"/>.</remarks>
    public void Stop()
    {
        _timer?.Stop();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }

        SharingChanged = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Función pura: true si alguna ventana coincide con alguna firma.
    /// Proceso: igualdad sin mayúsculas (".exe" opcional). Patrones: regex IgnoreCase; los nulos se ignoran;
    /// una firma sin ningún patrón nunca coincide. Un patrón inválido o que agota el tiempo se trata como no coincidente.
    /// </summary>
    internal static bool Matches(IEnumerable<WindowInfo> windows, IReadOnlyList<ShareSignature> sigs)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(sigs);

        foreach (WindowInfo window in windows)
        {
            string process = NormalizeProcessName(window.ProcessName);
            for (int i = 0; i < sigs.Count; i++)
            {
                ShareSignature sig = sigs[i];
                if (sig.ClassNamePattern is null && sig.TitlePattern is null)
                {
                    continue;
                }

                if (!string.Equals(process, NormalizeProcessName(sig.ProcessName), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (sig.ClassNamePattern is not null && !SafeIsMatch(sig.ClassNamePattern, window.ClassName))
                {
                    continue;
                }

                if (sig.TitlePattern is not null && !SafeIsMatch(sig.TitlePattern, window.Title))
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>Quita ".exe" final y espacios, para comparar nombres de proceso.</summary>
    internal static string NormalizeProcessName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        string trimmed = name.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed;
    }

    private static bool SafeIsMatch(string pattern, string? input)
    {
        try
        {
            Regex regex = RegexCache.GetOrAdd(
                pattern,
                static p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout));
            return regex.IsMatch(input ?? string.Empty);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        catch (ArgumentException ex)
        {
            Trace.TraceWarning($"ScreenShareDetector: patrón inválido '{pattern}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// La enumeración (EnumWindows, OpenProcess…) puede tardar con procesos lentos: se hace en el pool y
    /// sólo el resultado vuelve al hilo de UI (el await reanuda en el Dispatcher del timer). Un sondeo en
    /// curso impide que empiece otro, así que la caché pid→nombre nunca se usa desde dos hilos a la vez.
    /// </summary>
    private async void OnTick(object? sender, EventArgs e)
    {
        if (_pollInFlight)
        {
            return;
        }

        _pollInFlight = true;
        try
        {
            bool resetCache = ++_pollCount % CacheResetEveryPolls == 0;
            bool detected = await Task.Run(() =>
            {
                if (resetCache)
                {
                    _processNameCache.Clear();
                }

                return Matches(EnumerateCandidateWindows(), _signatures);
            });

            if (!_disposed && _timer is { IsEnabled: true })
            {
                UpdateState(detected);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"ScreenShareDetector: error en el sondeo: {ex}");
        }
        finally
        {
            _pollInFlight = false;
        }
    }

    private void UpdateState(bool detected)
    {
        if (detected)
        {
            _negativeStreak = 0;
            SetSharing(true);
            return;
        }

        if (!IsSharing)
        {
            return;
        }

        if (++_negativeStreak >= NegativePollsToClear)
        {
            _negativeStreak = 0;
            SetSharing(false);
        }
    }

    private void SetSharing(bool value)
    {
        if (IsSharing == value)
        {
            return;
        }

        IsSharing = value;
        try
        {
            SharingChanged?.Invoke(this, value);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"ScreenShareDetector: un suscriptor de SharingChanged lanzó: {ex}");
        }
    }

    /// <summary>Ventanas visibles de nivel superior cuyo proceso aparece en alguna firma.</summary>
    private unsafe List<WindowInfo> EnumerateCandidateWindows()
    {
        var handles = new List<nint>(256);
        GCHandle gch = GCHandle.Alloc(handles);
        try
        {
            if (NativeMethods.EnumWindows(&EnumWindowsCallback, GCHandle.ToIntPtr(gch)) == 0)
            {
                Trace.TraceWarning($"ScreenShareDetector: EnumWindows falló. Win32 error {Marshal.GetLastPInvokeError()}.");
            }
        }
        finally
        {
            gch.Free();
        }

        var result = new List<WindowInfo>();
        char* buffer = stackalloc char[512];
        foreach (nint hwnd in handles)
        {
            if (NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0 || pid == _ownPid)
            {
                continue;
            }

            string processName = GetProcessName(pid);
            if (processName.Length == 0 || !_signatureProcesses.Contains(processName))
            {
                continue;
            }

            int classLen = NativeMethods.GetClassNameW(hwnd, buffer, 512);
            string className = classLen > 0 ? new string(buffer, 0, classLen) : string.Empty;

            int titleLen = NativeMethods.GetWindowTextW(hwnd, buffer, 512);
            string title = titleLen > 0 ? new string(buffer, 0, titleLen) : string.Empty;

            result.Add(new WindowInfo(processName, className, title));
        }

        return result;
    }

    [UnmanagedCallersOnly]
    private static int EnumWindowsCallback(nint hwnd, nint lParam)
    {
        try
        {
            if (NativeMethods.IsWindowVisible(hwnd) != 0 && GCHandle.FromIntPtr(lParam).Target is List<nint> list)
            {
                list.Add(hwnd);
            }
        }
        catch (Exception ex)
        {
            // Nunca propagar excepciones a través de código nativo.
            Trace.TraceError($"ScreenShareDetector: error en EnumWindows callback: {ex.Message}");
        }

        return 1; // continuar enumerando
    }

    /// <summary>Nombre del ejecutable sin ".exe" (cacheado). Cadena vacía si no se puede obtener.</summary>
    private unsafe string GetProcessName(uint pid)
    {
        if (_processNameCache.TryGetValue(pid, out string? cached))
        {
            return cached;
        }

        string name = string.Empty;
        nint hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, 0, pid);
        if (hProcess != 0)
        {
            try
            {
                char* path = stackalloc char[1024];
                uint size = 1024;
                if (NativeMethods.QueryFullProcessImageNameW(hProcess, 0, path, ref size) != 0 && size > 0)
                {
                    name = NormalizeProcessName(Path.GetFileName(new string(path, 0, (int)size)));
                }
            }
            finally
            {
                if (NativeMethods.CloseHandle(hProcess) == 0)
                {
                    Trace.TraceWarning($"ScreenShareDetector: CloseHandle falló. Win32 error {Marshal.GetLastPInvokeError()}.");
                }
            }
        }

        if (name.Length == 0)
        {
            // Acceso denegado (procesos elevados/protegidos): System.Diagnostics usa otra vía (NtQuerySystemInformation).
            try
            {
                using Process process = Process.GetProcessById((int)pid);
                name = process.ProcessName;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                name = string.Empty;
            }
        }

        _processNameCache[pid] = name;
        return name;
    }
}

using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace StickyNotes.App.Interop;

/// <summary>P/Invoke mínimo que necesita la capa de UI (estilos de ventana y orden Z).
/// La lógica Win32 "de negocio" vive en StickyNotes.Platform.</summary>
internal static partial class NativeMethods
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_APPWINDOW = 0x00040000L;

    private static readonly nint HWND_TOPMOST = -1;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUNDSMALL = 3;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    private const uint SWP_NOZORDER = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hWnd, out RECT rect);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int GetClassName(nint hWnd, char* className, int maxCount);

    // QUERY_USER_NOTIFICATION_STATE
    private const int QUNS_BUSY = 2;                    // app a pantalla completa
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3; // juego / vídeo D3D a pantalla completa
    private const int QUNS_PRESENTATION_MODE = 4;       // presentación (PowerPoint, "modo presentación")

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);

    /// <summary>Mueve la ventana a una posición en píxeles físicos, sin cambiar tamaño, orden Z ni foco.</summary>
    public static bool MoveTo(nint hwnd, int x, int y) =>
        hwnd != 0 && SetWindowPos(hwnd, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

    /// <summary>Esquina superior izquierda de la ventana en píxeles físicos.</summary>
    public static bool TryGetPosition(nint hwnd, out int x, out int y)
    {
        x = y = 0;
        if (hwnd == 0 || !GetWindowRect(hwnd, out var r)) return false;
        x = r.Left;
        y = r.Top;
        return true;
    }

    /// <summary>
    /// True si NO conviene reafirmar "topmost" ahora: hay una presentación o una app/vídeo a pantalla
    /// completa, o el primer plano es un menú emergente (#32768). Las notas siguen siendo topmost;
    /// sólo se deja de "pelear" el orden Z con esas ventanas.
    /// </summary>
    public static unsafe bool ShouldYieldTopmost()
    {
        try
        {
            if (SHQueryUserNotificationState(out var state) == 0 &&
                state is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE)
            {
                return true;
            }
        }
        catch (EntryPointNotFoundException) { }
        catch (DllNotFoundException) { }

        var fg = GetForegroundWindow();
        if (fg == 0) return false;
        char* buffer = stackalloc char[64];
        var len = GetClassName(fg, buffer, 64);
        return len > 0 && new string(buffer, 0, len) == "#32768";
    }

    /// <summary>Oculta la ventana de Alt+Tab y de la barra de tareas.</summary>
    public static void MakeToolWindow(nint hwnd)
    {
        if (hwnd == 0) return;
        var style = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        style = (style | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)style);
    }

    /// <summary>Vuelve a poner la ventana en la cima de la banda "topmost" sin activarla ni moverla.</summary>
    public static void BringToTopmost(nint hwnd)
    {
        if (hwnd == 0) return;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    /// <summary>True si la ventana en primer plano pertenece a este proceso.</summary>
    public static bool IsForegroundOwnedByThisProcess()
    {
        var fg = GetForegroundWindow();
        if (fg == 0) return false;
        GetWindowThreadProcessId(fg, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>Esquinas redondeadas pequeñas en Windows 11 (en Windows 10 no hace nada).</summary>
    public static void TryRoundCorners(nint hwnd)
    {
        try
        {
            var pref = DWMWCP_ROUNDSMALL;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}

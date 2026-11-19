using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace StickyNotes.Platform;

/// <summary>
/// Declaraciones Win32 (P/Invoke generado en compilación con <see cref="LibraryImportAttribute"/>).
/// Todas las funciones que devuelven BOOL se exponen como <see cref="int"/> (0 = fallo) para
/// evitar ambigüedades de marshalling; los llamadores deben comprobar el resultado.
/// </summary>
internal static unsafe partial class NativeMethods
{
    // ───────────── Afinidad de visualización ─────────────

    internal const uint WDA_NONE = 0x00;
    internal const uint WDA_MONITOR = 0x01;
    internal const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);

    // ───────────── Enumeración de ventanas ─────────────

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int EnumWindows(delegate* unmanaged<nint, nint, int> lpEnumFunc, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int GetClassNameW(nint hWnd, char* lpClassName, int nMaxCount);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int GetWindowTextW(nint hWnd, char* lpString, int nMaxCount);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    internal static partial int IsWindowVisible(nint hWnd);

    // ───────────── Procesos ─────────────

    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint OpenProcess(uint dwDesiredAccess, int bInheritHandle, uint dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int QueryFullProcessImageNameW(nint hProcess, uint dwFlags, char* lpExeName, ref uint lpdwSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int CloseHandle(nint hObject);

    // ───────────── Atajos globales ─────────────

    internal const int WM_HOTKEY = 0x0312;
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_WIN = 0x0008;
    internal const uint MOD_NOREPEAT = 0x4000;

    /// <summary>Padre especial para ventanas sólo-mensaje.</summary>
    internal static readonly nint HWND_MESSAGE = -3;

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int UnregisterHotKey(nint hWnd, int id);

    // ───────────── Monitores ─────────────

    internal const uint MONITORINFOF_PRIMARY = 0x1;
    internal const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [LibraryImport("user32.dll")]
    internal static partial int EnumDisplayMonitors(nint hdc, nint lprcClip, delegate* unmanaged<nint, nint, RECT*, nint, int> lpfnEnum, nint dwData);

    [LibraryImport("user32.dll")]
    internal static partial int GetMonitorInfoW(nint hMonitor, ref MONITORINFO lpmi);

    /// <summary>Shcore.dll (Windows 8.1+). Devuelve un HRESULT (0 = S_OK).</summary>
    [LibraryImport("shcore.dll")]
    internal static partial int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}

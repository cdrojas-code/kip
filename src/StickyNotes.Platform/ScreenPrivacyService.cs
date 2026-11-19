using System.Diagnostics;
using System.Runtime.InteropServices;
using StickyNotes.Core.Abstractions;

namespace StickyNotes.Platform;

/// <summary>
/// Protege las ventanas de nota frente a capturas y pantalla compartida mediante
/// <c>SetWindowDisplayAffinity</c>.
/// <list type="bullet">
/// <item>Build ≥ 19041 (Win10 2004+, LTSC 2021, Win11): <c>WDA_EXCLUDEFROMCAPTURE</c>; la ventana no aparece en la captura.</item>
/// <item>Build &lt; 19041 (p. ej. LTSC 2019, 17763): <c>WDA_MONITOR</c>; la ventana aparece como recuadro negro y la App debe
/// complementar con <see cref="ScreenShareDetector"/> para ocultarla.</item>
/// </list>
/// Si <c>WDA_EXCLUDEFROMCAPTURE</c> falla en tiempo de ejecución se recurre a <c>WDA_MONITOR</c> y
/// <see cref="Mode"/> pasa a <see cref="ScreenPrivacyMode.BlackoutWithAutoHide"/> desde ese momento.
/// Nunca lanza excepciones; los errores se registran con <see cref="Trace"/>.
/// </summary>
public sealed class ScreenPrivacyService : IScreenPrivacyService
{
    /// <summary>Primer build de Windows 10 (2004) que soporta <c>WDA_EXCLUDEFROMCAPTURE</c>.</summary>
    internal const int ExcludeFromCaptureMinBuild = 19041;

    private volatile bool _excludeFailed;
    private readonly bool _supportsExclude;

    /// <summary>Crea el servicio detectando el build real del sistema operativo.</summary>
    public ScreenPrivacyService()
        : this(Environment.OSVersion.Version.Build)
    {
    }

    /// <summary>Constructor para pruebas: fuerza el número de build.</summary>
    internal ScreenPrivacyService(int osBuild)
    {
        OsBuild = osBuild;
        _supportsExclude = osBuild >= ExcludeFromCaptureMinBuild;
    }

    /// <summary>Build del sistema operativo considerado.</summary>
    internal int OsBuild { get; }

    /// <inheritdoc />
    /// <remarks>Puede degradarse a <see cref="ScreenPrivacyMode.BlackoutWithAutoHide"/> si el sistema rechaza
    /// <c>WDA_EXCLUDEFROMCAPTURE</c>; conviene volver a consultarlo después de <see cref="Protect"/>.</remarks>
    public ScreenPrivacyMode Mode =>
        _supportsExclude && !_excludeFailed ? ScreenPrivacyMode.ExcludeFromCapture : ScreenPrivacyMode.BlackoutWithAutoHide;

    /// <inheritdoc />
    public void Protect(nint hwnd)
    {
        if (hwnd == 0)
        {
            Trace.TraceWarning("ScreenPrivacyService.Protect: HWND nulo, se ignora.");
            return;
        }

        try
        {
            if (Mode == ScreenPrivacyMode.ExcludeFromCapture)
            {
                if (TrySet(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE))
                {
                    return;
                }

                _excludeFailed = true;
                Trace.TraceWarning("ScreenPrivacyService: WDA_EXCLUDEFROMCAPTURE falló; se usa WDA_MONITOR.");
            }

            TrySet(hwnd, NativeMethods.WDA_MONITOR);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"ScreenPrivacyService.Protect: {ex}");
        }
    }

    /// <inheritdoc />
    public void Unprotect(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        try
        {
            TrySet(hwnd, NativeMethods.WDA_NONE);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"ScreenPrivacyService.Unprotect: {ex}");
        }
    }

    private static bool TrySet(nint hwnd, uint affinity)
    {
        if (NativeMethods.SetWindowDisplayAffinity(hwnd, affinity) != 0)
        {
            return true;
        }

        int error = Marshal.GetLastPInvokeError();
        Trace.TraceWarning($"SetWindowDisplayAffinity(0x{hwnd:X}, 0x{affinity:X}) falló. Win32 error {error}.");
        return false;
    }
}

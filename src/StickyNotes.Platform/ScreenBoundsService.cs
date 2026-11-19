using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using StickyNotes.Core.Abstractions;

namespace StickyNotes.Platform;

/// <summary>
/// Áreas de trabajo de los monitores (sin barra de tareas) en PÍXELES FÍSICOS de la pantalla virtual y
/// recolocación de notas que quedaron fuera de pantalla (p. ej. al desconectar un monitor).
/// <para>
/// Por qué píxeles y no DIPs: con monitores de escalas distintas (100 % + 150 %) la "pantalla virtual en
/// DIPs" no es continua y WPF convierte Left/Top con el DPI del sistema antes de crear el HWND, por lo que
/// una posición guardada en DIPs puede reabrirse en otro monitor. Los píxeles físicos son únicos y estables;
/// la App coloca la ventana con <c>SetWindowPos</c> y lee su posición con <c>GetWindowRect</c>.
/// </para>
/// <para>
/// La franja mínima visible y el margen se escalan con el DPI de cada monitor (<c>GetDpiForMonitor</c>,
/// Shcore.dll, Win 8.1+); si Shcore falla se asume 96 DPI.
/// </para>
/// Seguro para cualquier hilo; nunca lanza.
/// </summary>
public sealed class ScreenBoundsService : IScreenBoundsService
{
    /// <summary>Tamaño mínimo (px a 96 DPI, se escala por monitor) de la franja de título que debe quedar visible.</summary>
    internal const double MinVisible = 40;

    /// <summary>Margen (px a 96 DPI, se escala por monitor) al recolocar en el monitor principal.</summary>
    internal const double Margin = 24;

    /// <inheritdoc />
    /// <remarks>Píxeles físicos. El primer elemento es el monitor principal.</remarks>
    public IReadOnlyList<ScreenRect> GetWorkAreas() => GetMonitors().Select(m => m.Area).ToList();

    /// <summary>Escala (DPI/96) del monitor principal; 1.0 si no se puede obtener.</summary>
    public static double GetPrimaryScale() => GetMonitors()[0].Scale;

    /// <inheritdoc />
    public (double Left, double Top) EnsureVisible(double left, double top, double width, double height)
    {
        try
        {
            List<(ScreenRect Area, double Scale)> monitors = GetMonitors();
            return EnsureVisible(left, top, width, height, monitors.Select(m => m.Area).ToList(), monitors.Select(m => m.Scale).ToList());
        }
        catch (Exception ex)
        {
            Trace.TraceError($"ScreenBoundsService.EnsureVisible: {ex}");
            return (left, top);
        }
    }

    /// <summary>
    /// Lógica pura de <see cref="EnsureVisible(double, double, double, double)"/> (todo en píxeles).
    /// <paramref name="workAreas"/>[0] se considera el monitor principal; <paramref name="scales"/> (opcional,
    /// mismo orden) escala la franja mínima y el margen. Si al menos 40×40 px escalados de la franja de título
    /// intersecan alguna área, devuelve la posición sin cambios; si no, la coloca arriba a la derecha del área
    /// principal con un margen de 24 px escalados, sin salirse del área.
    /// </summary>
    internal static (double Left, double Top) EnsureVisible(
        double left, double top, double width, double height, IReadOnlyList<ScreenRect> workAreas,
        IReadOnlyList<double>? scales = null)
    {
        if (workAreas.Count == 0 || !double.IsFinite(width) || !double.IsFinite(height))
        {
            return (left, top);
        }

        width = Math.Max(0, width);
        height = Math.Max(0, height);

        if (double.IsFinite(left) && double.IsFinite(top))
        {
            for (int i = 0; i < workAreas.Count; i++)
            {
                ScreenRect wa = workAreas[i];
                double min = MinVisible * ScaleAt(scales, i);
                double needW = Math.Min(min, width);
                double needH = Math.Min(min, height);
                double ix = Math.Min(left + width, wa.Left + wa.Width) - Math.Max(left, wa.Left);
                double iy = Math.Min(top + needH, wa.Top + wa.Height) - Math.Max(top, wa.Top);
                if (ix >= needW && iy >= needH && (ix > 0 || needW == 0) && (iy > 0 || needH == 0))
                {
                    return (left, top);
                }
            }
        }

        ScreenRect primary = workAreas[0];
        double margin = Margin * ScaleAt(scales, 0);
        double newLeft = primary.Left + primary.Width - width - margin;
        double newTop = primary.Top + margin;

        // Asegurar que la esquina superior izquierda queda dentro del área (la nota puede ser mayor que el área).
        newLeft = Math.Max(primary.Left, Math.Min(newLeft, primary.Left + primary.Width - Math.Min(width, primary.Width)));
        newTop = Math.Max(primary.Top, Math.Min(newTop, primary.Top + primary.Height - Math.Min(height, primary.Height)));
        return (Math.Round(newLeft), Math.Round(newTop));
    }

    private static double ScaleAt(IReadOnlyList<double>? scales, int index) =>
        scales is not null && index < scales.Count && double.IsFinite(scales[index]) && scales[index] > 0 ? scales[index] : 1.0;

    /// <summary>Monitores (principal primero). Nunca vacío.</summary>
    private static List<(ScreenRect Area, double Scale)> GetMonitors()
    {
        try
        {
            List<(ScreenRect Area, double Scale, bool IsPrimary)> monitors = EnumerateMonitors();
            if (monitors.Count > 0)
            {
                return monitors.OrderByDescending(m => m.IsPrimary).Select(m => (m.Area, m.Scale)).ToList();
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"ScreenBoundsService.GetMonitors: {ex}");
        }

        return [(FallbackPrimaryWorkArea(), 1.0)];
    }

    private static ScreenRect FallbackPrimaryWorkArea()
    {
        try
        {
            // Aproximación: con proceso PerMonitorV2, WorkArea está en DIPs del DPI del sistema.
            Rect wa = SystemParameters.WorkArea;
            return new ScreenRect(wa.Left, wa.Top, wa.Width, wa.Height);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"ScreenBoundsService: SystemParameters.WorkArea falló: {ex}");
            return new ScreenRect(0, 0, 1024, 768);
        }
    }

    private static unsafe List<(ScreenRect Area, double Scale, bool IsPrimary)> EnumerateMonitors()
    {
        var handles = new List<nint>(4);
        GCHandle gch = GCHandle.Alloc(handles);
        try
        {
            if (NativeMethods.EnumDisplayMonitors(0, 0, &MonitorEnumCallback, GCHandle.ToIntPtr(gch)) == 0)
            {
                Trace.TraceWarning("ScreenBoundsService: EnumDisplayMonitors falló.");
            }
        }
        finally
        {
            gch.Free();
        }

        var result = new List<(ScreenRect, double, bool)>(handles.Count);
        foreach (nint hMonitor in handles)
        {
            var info = new NativeMethods.MONITORINFO { cbSize = (uint)sizeof(NativeMethods.MONITORINFO) };
            if (NativeMethods.GetMonitorInfoW(hMonitor, ref info) == 0)
            {
                Trace.TraceWarning($"ScreenBoundsService: GetMonitorInfo(0x{hMonitor:X}) falló.");
                continue;
            }

            NativeMethods.RECT r = info.rcWork;
            var area = new ScreenRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            if (area.Width > 0 && area.Height > 0)
            {
                result.Add((area, GetScale(hMonitor), (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0));
            }
        }

        return result;
    }

    /// <summary>Escala del monitor (DPI efectivo / 96). 1.0 si no se puede obtener.</summary>
    private static double GetScale(nint hMonitor)
    {
        try
        {
            int hr = NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _);
            if (hr == 0 && dpiX > 0)
            {
                return dpiX / 96.0;
            }

            Trace.TraceWarning($"ScreenBoundsService: GetDpiForMonitor devolvió 0x{hr:X8}.");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Trace.TraceWarning($"ScreenBoundsService: Shcore no disponible: {ex.Message}");
        }

        return 1.0;
    }

    [UnmanagedCallersOnly]
    private static unsafe int MonitorEnumCallback(nint hMonitor, nint hdc, NativeMethods.RECT* rect, nint data)
    {
        try
        {
            if (GCHandle.FromIntPtr(data).Target is List<nint> list)
            {
                list.Add(hMonitor);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"ScreenBoundsService: error en MonitorEnumCallback: {ex.Message}");
        }

        return 1;
    }
}

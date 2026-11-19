using System.Windows.Threading;
using StickyNotes.App.Logging;
using StickyNotes.Core.Abstractions;

namespace StickyNotes.App.Services;

/// <summary>Revisa recordatorios cada 30 s (y al arrancar) y refresca los distintivos de las notas.</summary>
public sealed class ReminderScheduler : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IReminderMonitor _monitor;
    private readonly NoteWindowManager _noteWindows;
    private readonly FileLogger _log;
    private readonly DispatcherTimer _timer;

    public ReminderScheduler(IReminderMonitor monitor, NoteWindowManager noteWindows, FileLogger log)
    {
        _monitor = monitor;
        _noteWindows = noteWindows;
        _log = log;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Interval };
        _timer.Tick += (_, _) => Check();
    }

    public void Start()
    {
        Check();
        _timer.Start();
    }

    private void Check()
    {
        try
        {
            _monitor.CheckDue();
        }
        catch (Exception ex)
        {
            _log.Error("Error al revisar recordatorios", ex);
        }
        _noteWindows.RefreshReminderStates();
    }

    public void Dispose() => _timer.Stop();
}

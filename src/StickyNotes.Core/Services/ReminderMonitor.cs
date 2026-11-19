using StickyNotes.Core.Abstractions;

namespace StickyNotes.Core.Services;

/// <summary>Detecta recordatorios vencidos de notas activas y los notifica una sola vez.</summary>
public sealed class ReminderMonitor : IReminderMonitor
{
    private readonly INoteService _notes;
    private readonly INotifier _notifier;
    private readonly IClock _clock;

    public ReminderMonitor(INoteService notes, INotifier notifier, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(clock);

        _notes = notes;
        _notifier = notifier;
        _clock = clock;
    }

    /// <summary>Fallo al notificar una nota concreta (se reintentará en la próxima revisión).</summary>
    public event EventHandler<Exception>? NotifyFailed;

    public void CheckDue()
    {
        var now = _clock.Now;
        foreach (var note in _notes.ActiveNotes)
        {
            if (note.Reminder is not { IsNotified: false } reminder || reminder.DueAt > now)
            {
                continue;
            }

            try
            {
                _notifier.ShowReminder(note);
            }
#pragma warning disable CA1031 // El fallo de una notificación no debe impedir las demás.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                NotifyFailed?.Invoke(this, ex);
                continue;
            }

            reminder.IsNotified = true;
            _notes.MarkChanged(note);
        }
    }
}

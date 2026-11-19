using CommunityToolkit.Mvvm.ComponentModel;
using StickyNotes.Core.Models;

namespace StickyNotes.App.ViewModels;

/// <summary>Estado editable del popup de recordatorio (fecha + hora + minuto).</summary>
public sealed partial class ReminderEditor : ObservableObject
{
    public static IReadOnlyList<int> Hours { get; } = Enumerable.Range(0, 24).ToArray();
    public static IReadOnlyList<int> Minutes { get; } = Enumerable.Range(0, 12).Select(i => i * 5).ToArray();

    private DateTime? _date;
    private int _hour;
    private int _minute;

    public DateTime? Date { get => _date; set => SetProperty(ref _date, value); }
    public int Hour { get => _hour; set => SetProperty(ref _hour, value); }
    public int Minute { get => _minute; set => SetProperty(ref _minute, value); }

    /// <summary>Carga el recordatorio existente o propone "dentro de una hora, en punto".</summary>
    public void LoadFrom(Reminder? reminder, DateTimeOffset now)
    {
        var local = reminder?.DueAt.LocalDateTime ?? now.LocalDateTime.AddHours(1);
        Date = local.Date;
        Hour = local.Hour;
        Minute = reminder is null ? 0 : Minutes.LastOrDefault(m => m <= local.Minute);
    }

    /// <summary>Momento elegido en hora local, o null si no hay fecha.</summary>
    public DateTimeOffset? ToDueAt()
    {
        if (Date is not { } date) return null;
        var local = DateTime.SpecifyKind(date.Date.AddHours(Hour).AddMinutes(Minute), DateTimeKind.Local);
        return new DateTimeOffset(local);
    }
}

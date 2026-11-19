using StickyNotes.Core.Models;
using StickyNotes.Core.Services;

namespace StickyNotes.Core.Tests;

public sealed class ReminderMonitorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeClock _clock = new(Now);
    private readonly InMemoryNoteRepository _repo = new();
    private readonly FakeNotifier _notifier = new();
    private readonly NoteService _service;
    private readonly ReminderMonitor _monitor;

    public ReminderMonitorTests()
    {
        _service = new NoteService(_repo, new FakeSettingsStore(), new FakeBackupService(), _clock, TimeSpan.FromHours(1));
        _monitor = new ReminderMonitor(_service, _notifier, _clock);
    }

    public void Dispose() => _service.Dispose();

    private async Task<Note> AddAsync(Note note)
    {
        _repo.Stored.Add(note);
        await _service.InitializeAsync();
        return _service.Find(note.Id)!;
    }

    [Fact]
    public async Task DueReminderIsNotifiedAndMarked()
    {
        var note = await AddAsync(new Note { Reminder = new Reminder { DueAt = Now.AddMinutes(-1) }, UpdatedAt = Now.AddDays(-1) });

        _monitor.CheckDue();

        Assert.Same(note, Assert.Single(_notifier.Shown));
        Assert.True(note.Reminder!.IsNotified);
        Assert.Equal(Now, note.UpdatedAt);
    }

    [Fact]
    public async Task ReminderExactlyNowIsDue()
    {
        await AddAsync(new Note { Reminder = new Reminder { DueAt = Now } });

        _monitor.CheckDue();

        Assert.Single(_notifier.Shown);
    }

    [Fact]
    public async Task FutureReminderIsNotNotified()
    {
        var note = await AddAsync(new Note { Reminder = new Reminder { DueAt = Now.AddMinutes(1) } });

        _monitor.CheckDue();

        Assert.Empty(_notifier.Shown);
        Assert.False(note.Reminder!.IsNotified);
    }

    [Fact]
    public async Task AlreadyNotifiedIsSkipped()
    {
        await AddAsync(new Note { Reminder = new Reminder { DueAt = Now.AddHours(-1), IsNotified = true } });

        _monitor.CheckDue();

        Assert.Empty(_notifier.Shown);
    }

    [Fact]
    public async Task TrashedNoteIsIgnored()
    {
        var note = await AddAsync(new Note { Reminder = new Reminder { DueAt = Now.AddHours(-1) }, DeletedAt = Now });

        _monitor.CheckDue();

        Assert.Empty(_notifier.Shown);
        Assert.False(note.Reminder!.IsNotified);
    }

    [Fact]
    public async Task NotifyIsOnlyDoneOnce()
    {
        await AddAsync(new Note { Reminder = new Reminder { DueAt = Now.AddHours(-1) } });

        _monitor.CheckDue();
        _monitor.CheckDue();

        Assert.Single(_notifier.Shown);
    }

    [Fact]
    public async Task NotifierFailureDoesNotStopOthers()
    {
        var bad = new Note { Title = "bad", Reminder = new Reminder { DueAt = Now.AddHours(-1) } };
        var good = new Note { Title = "good", Reminder = new Reminder { DueAt = Now.AddHours(-1) } };
        _repo.Stored.AddRange([bad, good]);
        await _service.InitializeAsync();
        _notifier.ShouldThrow = n => n.Title == "bad";
        var failures = 0;
        _monitor.NotifyFailed += (_, _) => failures++;

        _monitor.CheckDue();

        Assert.Equal("good", Assert.Single(_notifier.Shown).Title);
        Assert.False(_service.Find(bad.Id)!.Reminder!.IsNotified);
        Assert.Equal(1, failures);
    }
}

using StickyNotes.Core.Abstractions;
using StickyNotes.Core.Models;
using StickyNotes.Core.Services;

namespace StickyNotes.Core.Tests;

public sealed class NoteServiceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 18, 10, 0, 0, TimeSpan.FromHours(-5));

    private readonly FakeClock _clock = new(Start);
    private readonly InMemoryNoteRepository _repo = new();
    private readonly FakeSettingsStore _settings = new();
    private readonly FakeBackupService _backups = new();
    private readonly List<(string Kind, Note Note)> _events = [];
    private NoteService? _service;

    public void Dispose() => _service?.Dispose();

    private NoteService CreateService(TimeSpan? debounce = null)
    {
        _service = new NoteService(_repo, _settings, _backups, _clock, debounce ?? TimeSpan.FromHours(1));
        _service.NoteShown += (_, e) => _events.Add(("shown", e.Note));
        _service.NoteHidden += (_, e) => _events.Add(("hidden", e.Note));
        return _service;
    }

    [Fact]
    public async Task CreateUsesDefaultsAndRaisesShown()
    {
        _settings.Settings = new AppSettings { DefaultColor = NoteColor.Blue, DefaultSize = NoteSize.Large, DefaultFontSize = 18 };
        var service = CreateService();
        await service.InitializeAsync();

        var note = service.Create(100, 200);

        Assert.Equal(NoteColor.Blue, note.Color);
        Assert.Equal(NoteSize.Large, note.Size);
        Assert.Equal(18, note.FontSize);
        Assert.Equal((100d, 200d), (note.Left, note.Top));
        Assert.Equal(Start, note.CreatedAt);
        Assert.Equal(Start, note.UpdatedAt);
        Assert.Equal([("shown", note)], _events);
        Assert.Same(note, Assert.Single(service.ActiveNotes));
    }

    [Fact]
    public async Task InitializeDoesNotRaiseShownAndCreatesBackup()
    {
        _repo.Stored.Add(new Note { Title = "a" });
        _settings.Settings = new AppSettings { BackupRetentionDays = 12 };
        var service = CreateService();

        await service.InitializeAsync();

        Assert.Empty(_events);
        Assert.Single(service.ActiveNotes);
        var call = Assert.Single(_backups.DailyCalls);
        Assert.Equal(12, call.RetentionDays);
        Assert.Single(call.Notes);
    }

    [Fact]
    public async Task TrashRestoreAndDeleteRaiseEventsAndUpdateLists()
    {
        var service = CreateService();
        await service.InitializeAsync();
        var note = service.Create(0, 0);
        _events.Clear();

        _clock.Now = Start.AddMinutes(5);
        service.MoveToTrash(note.Id);
        Assert.Empty(service.ActiveNotes);
        Assert.Same(note, Assert.Single(service.TrashedNotes));
        Assert.Equal(Start.AddMinutes(5), note.DeletedAt);

        service.Restore(note.Id);
        Assert.Null(note.DeletedAt);
        Assert.Single(service.ActiveNotes);
        Assert.Empty(service.TrashedNotes);

        service.DeletePermanently(note.Id);
        Assert.Empty(service.ActiveNotes);
        Assert.Null(service.Find(note.Id));

        Assert.Equal([("hidden", note), ("shown", note), ("hidden", note)], _events);
    }

    [Fact]
    public async Task DeletingTrashedNoteDoesNotRaiseHidden()
    {
        var service = CreateService();
        await service.InitializeAsync();
        var note = service.Create(0, 0);
        service.MoveToTrash(note.Id);
        _events.Clear();

        service.DeletePermanently(note.Id);

        Assert.Empty(_events);
        Assert.Empty(service.TrashedNotes);
    }

    [Fact]
    public async Task EmptyTrashRemovesOnlyTrashed()
    {
        var service = CreateService();
        await service.InitializeAsync();
        var keep = service.Create(0, 0);
        var drop = service.Create(0, 0);
        service.MoveToTrash(drop.Id);

        service.EmptyTrash();

        Assert.Same(keep, Assert.Single(service.ActiveNotes));
        Assert.Empty(service.TrashedNotes);
    }

    [Fact]
    public async Task InitializePurgesExpiredTrashAndSaves()
    {
        _settings.Settings = new AppSettings { TrashRetentionDays = 30 };
        var expired = new Note { DeletedAt = Start.AddDays(-31) };
        var recent = new Note { DeletedAt = Start.AddDays(-29) };
        var active = new Note();
        _repo.Stored.AddRange([expired, recent, active]);
        var service = CreateService();

        await service.InitializeAsync();

        Assert.Null(service.Find(expired.Id));
        Assert.Equal(recent.Id, Assert.Single(service.TrashedNotes).Id);
        Assert.Equal(active.Id, Assert.Single(service.ActiveNotes).Id);
        Assert.Equal(1, _repo.SaveCount);
        Assert.DoesNotContain(_repo.LastSaved, n => n.Id == expired.Id);
        // El backup diario se toma ANTES de purgar: conserva la nota vencida por si se purgó por error.
        Assert.Contains(_backups.DailyCalls[0].Notes, n => n.Id == expired.Id);
    }

    [Fact]
    public async Task MarkChangedUpdatesTimestamp()
    {
        var service = CreateService();
        await service.InitializeAsync();
        var note = service.Create(0, 0);

        _clock.Now = Start.AddHours(2);
        service.MarkChanged(note);

        Assert.Equal(Start.AddHours(2), note.UpdatedAt);
    }

    [Fact]
    public async Task SeveralChangesAreCoalescedIntoOneSave()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(150));
        await service.InitializeAsync();
        var note = service.Create(0, 0);

        for (var i = 0; i < 5; i++)
        {
            note.Title = $"t{i}";
            service.MarkChanged(note);
        }

        await WaitUntilAsync(() => _repo.SaveCount >= 1);
        await Task.Delay(400);

        Assert.Equal(1, _repo.SaveCount);
        Assert.Equal("t4", Assert.Single(_repo.LastSaved).Title);
    }

    [Fact]
    public async Task SavedSnapshotIsIndependentOfLiveNotes()
    {
        var service = CreateService();
        await service.InitializeAsync();
        var note = service.Create(0, 0);
        note.Title = "antes";

        await service.FlushAsync();
        note.Title = "despues";

        Assert.Equal("antes", Assert.Single(_repo.LastSaved).Title);
    }

    [Fact]
    public async Task FlushSavesImmediatelyAndCancelsPendingDebounce()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(200));
        await service.InitializeAsync();
        var note = service.Create(0, 0);
        service.MarkChanged(note);

        await service.FlushAsync();
        Assert.Equal(1, _repo.SaveCount);

        await Task.Delay(500);
        Assert.Equal(1, _repo.SaveCount);
    }

    [Fact]
    public async Task BackgroundSaveFailureRaisesSaveFailed()
    {
        var failing = new FailingRepository();
        using var service = new NoteService(failing, _settings, _backups, _clock, TimeSpan.FromMilliseconds(20));
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.SaveFailed += (_, ex) => failed.TrySetResult(ex);
        await service.InitializeAsync();

        service.Create(0, 0);

        var error = await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsType<IOException>(error);
    }

    [Fact]
    public async Task SaveSettingsUsesStore()
    {
        var service = CreateService();
        await service.InitializeAsync();
        service.Settings.ConfirmDelete = false;

        await service.SaveSettingsAsync();

        Assert.Equal(1, _settings.SaveCount);
        Assert.False(_settings.Settings.ConfirmDelete);
    }

    [Fact]
    public async Task ImportAddsNewAndReplacesOnlyNewer()
    {
        var older = new Note { Title = "local-old", UpdatedAt = Start.AddDays(-2) };
        var newer = new Note { Title = "local-new", UpdatedAt = Start };
        _repo.Stored.AddRange([older, newer]);
        var service = CreateService();
        await service.InitializeAsync();

        var replacement = new Note { Id = older.Id, Title = "imported", UpdatedAt = Start.AddDays(-1) };
        var stale = new Note { Id = newer.Id, Title = "stale", UpdatedAt = Start.AddDays(-5) };
        var added = new Note { Title = "added", UpdatedAt = Start };
        var addedTrashed = new Note { Title = "trashed", UpdatedAt = Start, DeletedAt = Start };

        var count = service.Import([replacement, stale, added, addedTrashed]);

        Assert.Equal(3, count);
        Assert.Equal("imported", service.Find(older.Id)!.Title);
        Assert.Equal("local-new", service.Find(newer.Id)!.Title);
        Assert.Same(added, service.Find(added.Id));
        Assert.Same(addedTrashed, Assert.Single(service.TrashedNotes));
        Assert.Equal(3, service.ActiveNotes.Count);
        Assert.Equal(
            [("hidden", older), ("shown", replacement), ("shown", added)],
            _events);
    }

    [Fact]
    public async Task ImportReplacingTrashedWithActiveRaisesOnlyShown()
    {
        var trashed = new Note { UpdatedAt = Start.AddDays(-1), DeletedAt = Start.AddDays(-1) };
        _repo.Stored.Add(trashed);
        var service = CreateService();
        await service.InitializeAsync();
        var incoming = new Note { Id = trashed.Id, UpdatedAt = Start };

        Assert.Equal(1, service.Import([incoming]));

        Assert.Equal([("shown", incoming)], _events);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Tiempo de espera agotado.");
            await Task.Delay(10);
        }
    }

    private sealed class FailingRepository : INoteRepository
    {
        public Task<IReadOnlyList<Note>> LoadAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Note>>([]);

        public Task SaveAllAsync(IReadOnlyCollection<Note> notes, CancellationToken ct = default) =>
            Task.FromException(new IOException("disco lleno"));
    }
}

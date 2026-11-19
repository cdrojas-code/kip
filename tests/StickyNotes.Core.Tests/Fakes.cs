using StickyNotes.Core.Abstractions;
using StickyNotes.Core.Models;

namespace StickyNotes.Core.Tests;

internal sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset Now { get; set; } = now;
}

internal sealed class InMemoryNoteRepository : INoteRepository
{
    private readonly Lock _sync = new();
    private int _saveCount;

    public List<Note> Stored { get; } = [];

    public int SaveCount => Volatile.Read(ref _saveCount);

    public IReadOnlyList<Note> LastSaved { get; private set; } = [];

    public Task<IReadOnlyList<Note>> LoadAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Note>>([.. Stored]);

    public Task SaveAllAsync(IReadOnlyCollection<Note> notes, CancellationToken ct = default)
    {
        lock (_sync)
        {
            LastSaved = [.. notes];
        }

        Interlocked.Increment(ref _saveCount);
        return Task.CompletedTask;
    }
}

/// <summary>Repositorio que falla al cargar, o que falla las primeras N escrituras.</summary>
internal sealed class FaultyNoteRepository : INoteRepository
{
    private int _remainingSaveFailures;
    private int _saveAttempts;
    private int _successfulSaves;

    public FaultyNoteRepository(bool failLoad = false, int saveFailures = 0)
    {
        FailLoad = failLoad;
        _remainingSaveFailures = saveFailures;
    }

    public bool FailLoad { get; }

    public int SaveAttempts => Volatile.Read(ref _saveAttempts);

    public int SuccessfulSaves => Volatile.Read(ref _successfulSaves);

    public Task<IReadOnlyList<Note>> LoadAllAsync(CancellationToken ct = default) =>
        FailLoad
            ? Task.FromException<IReadOnlyList<Note>>(new IOException("archivo bloqueado (simulado)"))
            : Task.FromResult<IReadOnlyList<Note>>([]);

    public Task SaveAllAsync(IReadOnlyCollection<Note> notes, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _saveAttempts);
        if (Interlocked.Decrement(ref _remainingSaveFailures) >= 0)
        {
            return Task.FromException(new IOException("disco no disponible (simulado)"));
        }

        Interlocked.Increment(ref _successfulSaves);
        return Task.CompletedTask;
    }
}

internal sealed class FakeSettingsStore : ISettingsStore
{
    public AppSettings Settings { get; set; } = new();

    public int SaveCount { get; private set; }

    public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(Settings);

    public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        Settings = settings;
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeBackupService : IBackupService, StickyNotes.Core.Services.ISafetyBackup
{
    public List<(IReadOnlyCollection<Note> Notes, int RetentionDays)> DailyCalls { get; } = [];

    public List<IReadOnlyCollection<Note>> SafetyCalls { get; } = [];

    public Task CreateSafetySnapshotAsync(IReadOnlyCollection<Note> notes, CancellationToken ct = default)
    {
        lock (SafetyCalls)
        {
            SafetyCalls.Add(notes);
        }

        return Task.CompletedTask;
    }

    public Task CreateDailyBackupIfNeededAsync(IReadOnlyCollection<Note> notes, int retentionDays, CancellationToken ct = default)
    {
        DailyCalls.Add((notes, retentionDays));
        return Task.CompletedTask;
    }

    public Task ExportAsync(IReadOnlyCollection<Note> notes, string filePath, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<Note>> ImportAsync(string filePath, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Note>>([]);

    public Task<IReadOnlyList<string>> ListBackupsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}

internal sealed class FakeNotifier : INotifier
{
    public List<Note> Shown { get; } = [];

    public Func<Note, bool> ShouldThrow { get; set; } = _ => false;

    public event EventHandler<Guid>? NotificationActivated;

    public void ShowReminder(Note note)
    {
        if (ShouldThrow(note))
        {
            throw new InvalidOperationException("fallo simulado");
        }

        Shown.Add(note);
    }

    public void RaiseActivated(Guid id) => NotificationActivated?.Invoke(this, id);
}

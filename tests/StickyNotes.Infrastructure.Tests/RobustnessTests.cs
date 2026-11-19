using System.Text;
using StickyNotes.Core.Models;

namespace StickyNotes.Infrastructure.Tests;

/// <summary>Reintentos de E/S, validación de ajustes, instantáneas de seguridad y límites de importación.</summary>
public sealed class RobustnessTests : IDisposable
{
    private static readonly IoRetry FastRetry = new(attempts: 3, delay: TimeSpan.FromMilliseconds(10));

    private readonly TempDirectory _temp = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.FromHours(-5)));

    public void Dispose() => _temp.Dispose();

    private static List<Note> Notes(int count) => [.. Enumerable.Range(0, count).Select(i => new Note { Title = $"n{i}" })];

    private string[] SafetyFiles() =>
        Directory.Exists(_temp.Paths.BackupDirectory)
            ? Directory.GetFiles(_temp.Paths.BackupDirectory, "notes-safety-*.json")
            : [];

    [Fact]
    public async Task LockedExistingNotesFileThrowsInsteadOfReturningEmpty()
    {
        var repo = new JsonNoteRepository(_temp.Paths, FastRetry);
        await repo.SaveAllAsync(Notes(2));

        using (new FileStream(_temp.Paths.NotesFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => repo.LoadAllAsync());
        }

        Assert.Equal(2, (await repo.LoadAllAsync()).Count);
    }

    [Fact]
    public async Task TransientLockIsRetriedAndLoadSucceeds()
    {
        var repo = new JsonNoteRepository(_temp.Paths, new IoRetry(attempts: 40, delay: TimeSpan.FromMilliseconds(25)));
        await repo.SaveAllAsync(Notes(3));

        var locker = new FileStream(_temp.Paths.NotesFile, FileMode.Open, FileAccess.Read, FileShare.None);
        _ = Task.Delay(150).ContinueWith(_ => locker.Dispose(), TaskScheduler.Default);

        Assert.Equal(3, (await repo.LoadAllAsync()).Count);
    }

    [Fact]
    public async Task LockedTargetIsRetriedOnSave()
    {
        var repo = new JsonNoteRepository(_temp.Paths, new IoRetry(attempts: 40, delay: TimeSpan.FromMilliseconds(25)));
        await repo.SaveAllAsync(Notes(1));

        var locker = new FileStream(_temp.Paths.NotesFile, FileMode.Open, FileAccess.Read, FileShare.None);
        _ = Task.Delay(150).ContinueWith(_ => locker.Dispose(), TaskScheduler.Default);

        await repo.SaveAllAsync(Notes(2));
        Assert.Equal(2, (await repo.LoadAllAsync()).Count);
    }

    [Fact]
    public async Task SettingsOutOfRangeAreClampedOnLoad()
    {
        Directory.CreateDirectory(_temp.Paths.DataDirectory);
        await File.WriteAllTextAsync(_temp.Paths.SettingsFile,
            """{ "trashRetentionDays": 2147483647, "backupRetentionDays": 0, "defaultFontSize": 1, "toggleHotkey": "" }""",
            Encoding.UTF8);

        var settings = await new JsonSettingsStore(_temp.Paths, FastRetry).LoadAsync();

        Assert.Equal(AppSettings.MaxRetentionDays, settings.TrashRetentionDays);
        Assert.Equal(AppSettings.MinRetentionDays, settings.BackupRetentionDays);
        Assert.Equal(AppSettings.MinFontSize, settings.DefaultFontSize);
        Assert.Equal(AppSettings.DefaultToggleHotkey, settings.ToggleHotkey);
    }

    [Fact]
    public async Task LockedSettingsFileThrowsAfterRetries()
    {
        var store = new JsonSettingsStore(_temp.Paths, FastRetry);
        await store.SaveAsync(new AppSettings());

        using var locker = new FileStream(_temp.Paths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None);
        await Assert.ThrowsAnyAsync<IOException>(() => store.LoadAsync());
    }

    [Fact]
    public async Task MassReductionCreatesSafetySnapshotOfPreviousFile()
    {
        var repo = new JsonNoteRepository(_temp.Paths, FastRetry);
        await repo.SaveAllAsync(Notes(4));

        await repo.SaveAllAsync(Notes(3)); // reducción menor: sin instantánea
        Assert.Empty(SafetyFiles());

        await repo.SaveAllAsync(Notes(1)); // 1*2 < 3
        var snapshot = Assert.Single(SafetyFiles());
        Assert.Equal(3, (await new BackupService(_temp.Paths, _clock).ImportAsync(snapshot)).Count);
    }

    [Fact]
    public async Task SmallCollectionsDoNotCreateSafetySnapshots()
    {
        var repo = new JsonNoteRepository(_temp.Paths, FastRetry);
        await repo.SaveAllAsync(Notes(2));
        await repo.SaveAllAsync(Notes(0));

        Assert.Empty(SafetyFiles());
    }

    [Fact]
    public async Task SafetySnapshotsRotateKeepingTheNewest()
    {
        var backups = new BackupService(_temp.Paths, _clock);
        for (var i = 0; i < SafetySnapshots.Keep + 3; i++)
        {
            await backups.CreateSafetySnapshotAsync(Notes(i + 1));
        }

        Assert.Equal(SafetySnapshots.Keep, SafetyFiles().Length);
        var newest = SafetySnapshots.ListNewestFirst(_temp.Paths.BackupDirectory)[0];
        Assert.Equal(SafetySnapshots.Keep + 3, (await backups.ImportAsync(newest)).Count);
    }

    [Fact]
    public async Task DailyPruneLeavesSafetySnapshotsAlone()
    {
        var backups = new BackupService(_temp.Paths, _clock);
        await backups.CreateSafetySnapshotAsync(Notes(1));
        await backups.CreateDailyBackupIfNeededAsync(Notes(1), retentionDays: 1);

        _clock.Now = _clock.Now.AddDays(30);
        await backups.CreateDailyBackupIfNeededAsync(Notes(1), retentionDays: 1);

        Assert.Single(SafetyFiles());
        Assert.DoesNotContain(await backups.ListBackupsAsync(), p => p.Contains("safety", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HugeRetentionDoesNotThrow()
    {
        var backups = new BackupService(_temp.Paths, _clock);
        await backups.CreateDailyBackupIfNeededAsync(Notes(1), retentionDays: int.MaxValue);
        Assert.Single(await backups.ListBackupsAsync());
    }

    [Fact]
    public async Task ImportRejectsFilesLargerThanLimit()
    {
        var path = Path.Combine(_temp.Root, "enorme.json");
        Directory.CreateDirectory(_temp.Root);
        using (var stream = new FileStream(path, FileMode.Create))
        {
            stream.SetLength(BackupService.MaxImportBytes + 1);
        }

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => new BackupService(_temp.Paths, _clock).ImportAsync(path));
        Assert.Contains("20 MB", ex.Message, StringComparison.Ordinal);
    }
}

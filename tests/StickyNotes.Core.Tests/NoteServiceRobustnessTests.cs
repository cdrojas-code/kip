using StickyNotes.Core.Models;
using StickyNotes.Core.Services;

namespace StickyNotes.Core.Tests;

/// <summary>Protección de datos: arranque fallido, ajustes inválidos, reintentos, importaciones no confiables.</summary>
public sealed class NoteServiceRobustnessTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 18, 10, 0, 0, TimeSpan.FromHours(-5));
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly FakeClock _clock = new(Start);
    private readonly FakeSettingsStore _settings = new();
    private readonly FakeBackupService _backups = new();

    [Fact]
    public async Task FailedInitializeNeverWritesNotesOrSettings()
    {
        var repo = new FaultyNoteRepository(failLoad: true);
        using var service = new NoteService(repo, _settings, _backups, _clock, TimeSpan.FromMilliseconds(10));

        await Assert.ThrowsAsync<IOException>(() => service.InitializeAsync());
        service.Create(0, 0);
        await service.FlushAsync();
        await service.SaveSettingsAsync();
        await Task.Delay(100);

        Assert.False(service.IsInitialized);
        Assert.Equal(0, repo.SaveAttempts);
        Assert.Equal(0, _settings.SaveCount);
    }

    [Fact]
    public async Task InitializeNormalizesOutOfRangeSettings()
    {
        _settings.Settings = new AppSettings
        {
            TrashRetentionDays = int.MaxValue,
            BackupRetentionDays = -5,
            DefaultFontSize = 999,
            DefaultColor = (NoteColor)99,
            DefaultSize = (NoteSize)42,
            ToggleHotkey = "  ",
        };
        var repo = new InMemoryNoteRepository();
        repo.Stored.Add(new Note { DeletedAt = Start.AddYears(-50) });
        using var service = new NoteService(repo, _settings, _backups, _clock, TimeSpan.FromHours(1));

        await service.InitializeAsync();

        var s = service.Settings;
        Assert.Equal(AppSettings.MaxRetentionDays, s.TrashRetentionDays);
        Assert.Equal(AppSettings.MinRetentionDays, s.BackupRetentionDays);
        Assert.Equal(AppSettings.MaxFontSize, s.DefaultFontSize);
        Assert.Equal(NoteColor.Yellow, s.DefaultColor);
        Assert.Equal(NoteSize.Medium, s.DefaultSize);
        Assert.Equal(AppSettings.DefaultToggleHotkey, s.ToggleHotkey);
        Assert.Equal(AppSettings.MinRetentionDays, _backups.DailyCalls[0].RetentionDays);
        Assert.Empty(service.TrashedNotes); // 50 años > 3650 días: se purga sin excepción.
    }

    [Fact]
    public async Task FailedBackgroundSaveIsRetriedUntilItSucceeds()
    {
        var repo = new FaultyNoteRepository(saveFailures: 2);
        using var service = new NoteService(repo, _settings, _backups, _clock,
            TimeSpan.FromMilliseconds(10), [TimeSpan.FromMilliseconds(20)]);
        var failures = 0;
        var recovered = new TaskCompletionSource();
        service.SaveFailed += (_, _) => Interlocked.Increment(ref failures);
        service.SaveRecovered += (_, _) => recovered.TrySetResult();
        await service.InitializeAsync();

        service.Create(0, 0);
        await recovered.Task.WaitAsync(Timeout);

        Assert.Equal(1, failures);            // sólo el primer fallo de la racha
        Assert.Equal(3, repo.SaveAttempts);   // 2 fallos + 1 éxito
        Assert.Equal(1, repo.SuccessfulSaves);
    }

    [Fact]
    public async Task FlushStillThrowsToCaller()
    {
        var repo = new FaultyNoteRepository(saveFailures: 1);
        using var service = new NoteService(repo, _settings, _backups, _clock, TimeSpan.FromHours(1));
        await service.InitializeAsync();
        service.Create(0, 0);

        await Assert.ThrowsAsync<IOException>(() => service.FlushAsync());
        await service.FlushAsync(); // el segundo intento funciona
        Assert.Equal(1, repo.SuccessfulSaves);
    }

    [Fact]
    public async Task ImportSanitizesUntrustedNotes()
    {
        using var service = new NoteService(new InMemoryNoteRepository(), _settings, _backups, _clock, TimeSpan.FromHours(1));
        await service.InitializeAsync();
        var hostile = new Note
        {
            Title = null!,
            PlainText = "texto conservado",
            RichContent = new string('x', NoteSanitizer.MaxRichContentLength + 1),
            FontSize = 999,
            Color = (NoteColor)99,
            Size = (NoteSize)99,
            Left = double.NaN,
            Top = double.PositiveInfinity,
            Checklist = [.. Enumerable.Range(0, 600).Select(i => new ChecklistItem { Text = $"t{i}" })],
            UpdatedAt = Start,
        };

        Assert.Equal(1, service.Import([hostile, null!, new Note { Id = Guid.Empty }]));

        var note = Assert.Single(service.ActiveNotes);
        Assert.Equal(string.Empty, note.Title);
        Assert.Equal(string.Empty, note.RichContent);
        Assert.Equal("texto conservado", note.PlainText);
        Assert.Equal(AppSettings.MaxFontSize, note.FontSize);
        Assert.Equal(NoteColor.Yellow, note.Color);
        Assert.Equal(NoteSize.Medium, note.Size);
        Assert.Equal(0, note.Left);
        Assert.Equal(0, note.Top);
        Assert.Equal(NoteSanitizer.MaxChecklistItems, note.Checklist.Count);
    }

    [Fact]
    public async Task ImportWritesSafetySnapshotOfPreviousStateOnNextSave()
    {
        using var service = new NoteService(new InMemoryNoteRepository(), _settings, _backups, _clock, TimeSpan.FromHours(1));
        await service.InitializeAsync();
        var existing = service.Create(0, 0);
        existing.Title = "antes";

        service.Import([new Note { Title = "importada", UpdatedAt = Start }]);
        existing.Title = "modificada después";
        await service.FlushAsync();

        var snapshot = Assert.Single(_backups.SafetyCalls);
        var saved = Assert.Single(snapshot);
        Assert.Equal(existing.Id, saved.Id);
        Assert.Equal("antes", saved.Title);

        await service.FlushAsync();
        Assert.Single(_backups.SafetyCalls); // una sola vez por importación
    }

    [Fact]
    public async Task ImportIntoEmptyServiceDoesNotCreateSnapshot()
    {
        using var service = new NoteService(new InMemoryNoteRepository(), _settings, _backups, _clock, TimeSpan.FromHours(1));
        await service.InitializeAsync();

        service.Import([new Note { UpdatedAt = Start }]);
        await service.FlushAsync();

        Assert.Empty(_backups.SafetyCalls);
    }
}

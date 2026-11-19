using StickyNotes.Core.Models;

namespace StickyNotes.Infrastructure.Tests;

public sealed class BackupServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 18, 22, 0, 0, TimeSpan.FromHours(-5)));
    private readonly BackupService _service;

    public BackupServiceTests() => _service = new BackupService(_temp.Paths, _clock);

    public void Dispose() => _temp.Dispose();

    private string BackupPath(string date) => Path.Combine(_temp.Paths.BackupDirectory, $"notes-{date}.json");

    [Fact]
    public async Task DailyBackupIsCreatedOncePerDay()
    {
        await _service.CreateDailyBackupIfNeededAsync([new Note { Title = "first" }], 30);
        await _service.CreateDailyBackupIfNeededAsync([new Note { Title = "second" }], 30);

        var files = Directory.GetFiles(_temp.Paths.BackupDirectory);
        Assert.Equal(BackupPath("20261118"), Assert.Single(files));
        Assert.Equal("first", Assert.Single(await _service.ImportAsync(files[0])).Title);

        _clock.Now = _clock.Now.AddDays(1);
        await _service.CreateDailyBackupIfNeededAsync([new Note { Title = "third" }], 30);

        Assert.Equal([BackupPath("20261119"), BackupPath("20261118")], await _service.ListBackupsAsync());
        Assert.False(File.Exists(BackupPath("20261119") + ".tmp"));
    }

    [Fact]
    public async Task PrunesBackupsOlderThanRetention()
    {
        foreach (var date in new[] { "20261001", "20261017", "20261018", "20261110" })
        {
            await File.WriteAllTextAsync(BackupPath(date), "{}");
        }

        var unrelated = Path.Combine(_temp.Paths.BackupDirectory, "otro.json");
        await File.WriteAllTextAsync(unrelated, "{}");

        await _service.CreateDailyBackupIfNeededAsync([], 31);

        Assert.False(File.Exists(BackupPath("20261001")));
        Assert.False(File.Exists(BackupPath("20261017")));
        Assert.True(File.Exists(BackupPath("20261018")));
        Assert.True(File.Exists(BackupPath("20261110")));
        Assert.True(File.Exists(BackupPath("20261118")));
        Assert.True(File.Exists(unrelated));
        Assert.Equal(
            [BackupPath("20261118"), BackupPath("20261110"), BackupPath("20261018")],
            await _service.ListBackupsAsync());
    }

    [Fact]
    public async Task ExportImportRoundTrip()
    {
        var note = SampleNotes.FullyPopulated();
        var path = Path.Combine(_temp.Root, "export", "mis-notas.json");

        await _service.ExportAsync([note], path);
        var imported = await _service.ImportAsync(path);

        SampleNotes.AssertEquivalent(note, Assert.Single(imported));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\": 1}")]
    [InlineData("{\"schemaVersion\": 99, \"notes\": []}")]
    [InlineData("{\"schemaVersion\": 1, \"notes\": [null]}")]
    public async Task InvalidImportThrowsInvalidData(string content)
    {
        var path = Path.Combine(_temp.Root, "malo.json");
        await File.WriteAllTextAsync(path, content);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => _service.ImportAsync(path));
        Assert.Contains("no es una exportación válida", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListBackupsEmptyWhenNone()
    {
        Assert.Empty(await _service.ListBackupsAsync());
    }
}

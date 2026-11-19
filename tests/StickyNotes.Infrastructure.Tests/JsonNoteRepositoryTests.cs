using StickyNotes.Core.Models;

namespace StickyNotes.Infrastructure.Tests;

public sealed class JsonNoteRepositoryTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly JsonNoteRepository _repo;

    public JsonNoteRepositoryTests() => _repo = new JsonNoteRepository(_temp.Paths);

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task MissingFileReturnsEmpty()
    {
        Assert.Empty(await _repo.LoadAllAsync());
    }

    [Fact]
    public async Task RoundTripsFullyPopulatedNote()
    {
        var note = SampleNotes.FullyPopulated();

        await _repo.SaveAllAsync([note, new Note()]);
        var loaded = await _repo.LoadAllAsync();

        Assert.Equal(2, loaded.Count);
        SampleNotes.AssertEquivalent(note, loaded[0]);
    }

    [Fact]
    public async Task FileUsesVersionedEnvelopeWithStringEnums()
    {
        await _repo.SaveAllAsync([new Note { Color = NoteColor.Green }]);

        var json = await File.ReadAllTextAsync(_temp.Paths.NotesFile);

        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"notes\": [", json, StringComparison.Ordinal);
        Assert.Contains("\"Green\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("isInTrash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AtomicSaveLeavesNoTempAndKeepsBak()
    {
        await _repo.SaveAllAsync([new Note { Title = "v1" }]);
        await _repo.SaveAllAsync([new Note { Title = "v2" }]);

        Assert.False(File.Exists(_temp.Paths.NotesFile + ".tmp"));
        Assert.True(File.Exists(_temp.Paths.NotesFile + ".bak"));
        Assert.Equal("v2", Assert.Single(await _repo.LoadAllAsync()).Title);
    }

    [Fact]
    public async Task CorruptFileFallsBackToBakAndPreservesCorruptCopy()
    {
        await _repo.SaveAllAsync([new Note { Title = "good" }]);
        await _repo.SaveAllAsync([new Note { Title = "newer" }]);
        await File.WriteAllTextAsync(_temp.Paths.NotesFile, "{ esto no es json");

        var loaded = await _repo.LoadAllAsync();

        Assert.Equal("good", Assert.Single(loaded).Title);
        var corrupt = Assert.Single(Directory.GetFiles(_temp.Root, "notes.corrupt-*.json"));
        Assert.Equal("{ esto no es json", await File.ReadAllTextAsync(corrupt));
    }

    [Fact]
    public async Task CorruptFileAndBakFallBackToNewestBackup()
    {
        var backups = _temp.Paths.BackupDirectory;
        await new JsonNoteRepository(new AppPaths(Path.Combine(_temp.Root, "scratch"))).SaveAllAsync([new Note { Title = "old" }]);
        File.Copy(Path.Combine(_temp.Root, "scratch", "notes.json"), Path.Combine(backups, "notes-20261101.json"));
        await new JsonNoteRepository(new AppPaths(Path.Combine(_temp.Root, "scratch2"))).SaveAllAsync([new Note { Title = "newest" }]);
        File.Copy(Path.Combine(_temp.Root, "scratch2", "notes.json"), Path.Combine(backups, "notes-20261115.json"));
        await File.WriteAllTextAsync(_temp.Paths.NotesFile, "[1,2");
        await File.WriteAllTextAsync(_temp.Paths.NotesFile + ".bak", "{\"notes\": null}");

        var loaded = await _repo.LoadAllAsync();

        Assert.Equal("newest", Assert.Single(loaded).Title);
        Assert.Single(Directory.GetFiles(_temp.Root, "notes.corrupt-*.json"));
    }

    [Fact]
    public async Task AllSourcesCorruptReturnsEmptyButKeepsCorruptCopy()
    {
        await File.WriteAllTextAsync(_temp.Paths.NotesFile, "garbage");

        var loaded = await _repo.LoadAllAsync();

        Assert.Empty(loaded);
        var corrupt = Assert.Single(Directory.GetFiles(_temp.Root, "notes.corrupt-*.json"));
        Assert.Equal("garbage", await File.ReadAllTextAsync(corrupt));
        Assert.True(File.Exists(_temp.Paths.NotesFile));
    }
}

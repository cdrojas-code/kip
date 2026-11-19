using StickyNotes.Core.Models;

namespace StickyNotes.Infrastructure.Tests;

public sealed class SettingsAndPathsTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void AppPathsCreatesDirectories()
    {
        var paths = _temp.Paths;

        Assert.True(Directory.Exists(paths.DataDirectory));
        Assert.True(Directory.Exists(paths.BackupDirectory));
        Assert.True(Directory.Exists(paths.LogDirectory));
        Assert.Equal(Path.Combine(paths.DataDirectory, "notes.json"), paths.NotesFile);
        Assert.Equal(Path.Combine(paths.DataDirectory, "settings.json"), paths.SettingsFile);
    }

    [Fact]
    public async Task SettingsRoundTrip()
    {
        var store = new JsonSettingsStore(_temp.Paths);
        var settings = new AppSettings
        {
            StartWithWindows = false,
            HideOnScreenShare = false,
            ToggleHotkey = "Ctrl+Shift+F12",
            ConfirmDelete = false,
            DefaultColor = NoteColor.Orange,
            DefaultSize = NoteSize.Small,
            DefaultFontSize = 20,
            BackupRetentionDays = 7,
            TrashRetentionDays = 3,
        };

        await store.SaveAsync(settings);
        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.False(loaded.StartWithWindows);
        Assert.False(loaded.HideOnScreenShare);
        Assert.Equal("Ctrl+Shift+F12", loaded.ToggleHotkey);
        Assert.False(loaded.ConfirmDelete);
        Assert.Equal(NoteColor.Orange, loaded.DefaultColor);
        Assert.Equal(NoteSize.Small, loaded.DefaultSize);
        Assert.Equal(20, loaded.DefaultFontSize);
        Assert.Equal(7, loaded.BackupRetentionDays);
        Assert.Equal(3, loaded.TrashRetentionDays);
        Assert.False(File.Exists(_temp.Paths.SettingsFile + ".tmp"));
    }

    [Fact]
    public async Task MissingOrCorruptSettingsReturnDefaults()
    {
        var store = new JsonSettingsStore(_temp.Paths);
        Assert.Equal("Ctrl+Alt+N", (await store.LoadAsync()).ToggleHotkey);

        await File.WriteAllTextAsync(_temp.Paths.SettingsFile, "{ roto");
        Assert.Equal(30, (await store.LoadAsync()).TrashRetentionDays);
    }
}

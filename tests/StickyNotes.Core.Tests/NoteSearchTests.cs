using StickyNotes.Core.Models;
using StickyNotes.Core.Services;

namespace StickyNotes.Core.Tests;

public sealed class NoteSearchTests
{
    private static readonly DateTimeOffset Base = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AccentAndCaseInsensitive()
    {
        var note = new Note { Title = "EJECUCION de sentencia" };

        Assert.Single(NoteSearch.Search([note], "ejecución"));
        Assert.Single(NoteSearch.Search([new Note { PlainText = "ejecución" }], "EJECUCION"));
    }

    [Fact]
    public void MultipleWordsMustAllMatch()
    {
        var both = new Note { Title = "Cliente Pérez", PlainText = "expediente 123" };
        var one = new Note { Title = "Cliente García" };

        var result = NoteSearch.Search([both, one], "cliente  perez");

        Assert.Same(both, Assert.Single(result));
    }

    [Fact]
    public void WordsCanComeFromDifferentFields()
    {
        var note = new Note { Title = "Pérez", Checklist = [new ChecklistItem { Text = "Llamar al juzgado" }] };

        Assert.Single(NoteSearch.Search([note], "perez juzgado"));
    }

    [Fact]
    public void MatchesChecklistText()
    {
        var note = new Note { Checklist = [new ChecklistItem { Text = "Enviar NOTIFICACIÓN" }] };

        Assert.Single(NoteSearch.Search([note], "notificacion"));
        Assert.Empty(NoteSearch.Search([note], "audiencia"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyQueryReturnsAllOrderedByUpdatedDesc(string query)
    {
        var old = new Note { UpdatedAt = Base };
        var recent = new Note { UpdatedAt = Base.AddDays(2) };
        var middle = new Note { UpdatedAt = Base.AddDays(1) };

        var result = NoteSearch.Search([old, recent, middle], query);

        Assert.Equal([recent, middle, old], result);
    }

    [Fact]
    public async Task ServiceSearchIgnoresTrashedNotes()
    {
        var clock = new FakeClock(Base);
        var repo = new InMemoryNoteRepository();
        repo.Stored.Add(new Note { Title = "activa informe" });
        repo.Stored.Add(new Note { Title = "papelera informe", DeletedAt = Base });
        using var service = new NoteService(repo, new FakeSettingsStore(), new FakeBackupService(), clock, TimeSpan.FromHours(1));
        await service.InitializeAsync();

        var result = service.Search("informe");

        Assert.Equal("activa informe", Assert.Single(result).Title);
    }
}

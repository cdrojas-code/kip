using StickyNotes.Core.Abstractions;
using StickyNotes.Core.Models;

namespace StickyNotes.Infrastructure.Tests;

/// <summary>Directorio temporal único por prueba; se borra al finalizar.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "StickyNotesTests", Guid.NewGuid().ToString("N"));
        Paths = new AppPaths(Root);
    }

    public string Root { get; }

    public AppPaths Paths { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Limpieza best-effort.
        }
    }
}

internal sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset Now { get; set; } = now;
}

internal static class SampleNotes
{
    public static Note FullyPopulated() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Cliente Pérez — expediente 42",
        RichContent = "<FlowDocument><Paragraph><Bold>Hola</Bold></Paragraph></FlowDocument>",
        PlainText = "Hola ejecución \"comillas\"",
        Checklist =
        [
            new ChecklistItem { Text = "Llamar", IsDone = true },
            new ChecklistItem { Text = "Enviar escrito", IsDone = false },
        ],
        Color = NoteColor.Purple,
        Size = NoteSize.Large,
        FontSize = 17.5,
        Left = -1234.5,
        Top = 678.25,
        IsPositionLocked = true,
        Reminder = new Reminder { DueAt = new DateTimeOffset(2026, 12, 1, 8, 30, 0, TimeSpan.FromHours(-5)), IsNotified = true },
        CreatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(1)),
        UpdatedAt = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.FromHours(-3)),
        DeletedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
    };

    public static void AssertEquivalent(Note expected, Note actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.RichContent, actual.RichContent);
        Assert.Equal(expected.PlainText, actual.PlainText);
        Assert.Equal(
            expected.Checklist.Select(i => (i.Id, i.Text, i.IsDone)),
            actual.Checklist.Select(i => (i.Id, i.Text, i.IsDone)));
        Assert.Equal(expected.Color, actual.Color);
        Assert.Equal(expected.Size, actual.Size);
        Assert.Equal(expected.FontSize, actual.FontSize);
        Assert.Equal(expected.Left, actual.Left);
        Assert.Equal(expected.Top, actual.Top);
        Assert.Equal(expected.IsPositionLocked, actual.IsPositionLocked);
        Assert.Equal(expected.Reminder?.DueAt, actual.Reminder?.DueAt);
        Assert.Equal(expected.Reminder?.DueAt.Offset, actual.Reminder?.DueAt.Offset);
        Assert.Equal(expected.Reminder?.IsNotified, actual.Reminder?.IsNotified);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.UpdatedAt, actual.UpdatedAt);
        Assert.Equal(expected.DeletedAt, actual.DeletedAt);
    }
}

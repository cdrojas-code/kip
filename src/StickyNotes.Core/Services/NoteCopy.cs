using StickyNotes.Core.Models;

namespace StickyNotes.Core.Services;

/// <summary>Copia profunda de notas para tomar instantáneas que se guardan fuera del hilo de UI.</summary>
internal static class NoteCopy
{
    public static Note Clone(Note source) => new()
    {
        Id = source.Id,
        Title = source.Title,
        RichContent = source.RichContent,
        PlainText = source.PlainText,
        // ToArray no valida la versión de la lista: nunca lanza aunque la UI la modifique a la vez.
        Checklist = [.. source.Checklist.ToArray().Select(i => new ChecklistItem { Id = i.Id, Text = i.Text, IsDone = i.IsDone })],
        Color = source.Color,
        Size = source.Size,
        FontSize = source.FontSize,
        Left = source.Left,
        Top = source.Top,
        IsPositionLocked = source.IsPositionLocked,
        Reminder = source.Reminder is { } r ? new Reminder { DueAt = r.DueAt, IsNotified = r.IsNotified } : null,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        DeletedAt = source.DeletedAt,
    };
}

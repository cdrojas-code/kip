using StickyNotes.Core.Models;

namespace StickyNotes.Core.Services;

/// <summary>Valida y acota notas de origen no confiable (importaciones) antes de incorporarlas.</summary>
internal static class NoteSanitizer
{
    /// <summary>Tamaño máximo (caracteres) del contenido enriquecido; si se supera se conserva sólo el texto plano.</summary>
    public const int MaxRichContentLength = 1_000_000;

    public const int MaxChecklistItems = 500;

    /// <summary>Corrige la nota en sitio y la devuelve.</summary>
    public static Note Sanitize(Note note)
    {
        note.Title ??= string.Empty;
        note.PlainText ??= string.Empty;
        note.RichContent ??= string.Empty;
        if (note.RichContent.Length > MaxRichContentLength)
        {
            note.RichContent = string.Empty;
        }

        note.FontSize = AppSettings.ClampFontSize(note.FontSize);

        if (!Enum.IsDefined(note.Color))
        {
            note.Color = NoteColor.Yellow;
        }

        if (!Enum.IsDefined(note.Size))
        {
            note.Size = NoteSize.Medium;
        }

        if (!double.IsFinite(note.Left))
        {
            note.Left = 0;
        }

        if (!double.IsFinite(note.Top))
        {
            note.Top = 0;
        }

        note.Checklist ??= [];
        note.Checklist.RemoveAll(i => i is null);
        if (note.Checklist.Count > MaxChecklistItems)
        {
            note.Checklist.RemoveRange(MaxChecklistItems, note.Checklist.Count - MaxChecklistItems);
        }

        foreach (var item in note.Checklist)
        {
            item.Text ??= string.Empty;
            if (item.Id == Guid.Empty)
            {
                item.Id = Guid.NewGuid();
            }
        }

        return note;
    }
}

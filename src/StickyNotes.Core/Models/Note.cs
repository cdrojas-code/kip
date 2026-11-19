namespace StickyNotes.Core.Models;

public enum NoteColor { Yellow, Pink, Green, Blue, Orange, Purple }

/// <summary>Tamaños fijos, como los blocs de post-it. Dimensiones en DIPs: ver <see cref="NoteSizeExtensions"/>.</summary>
public enum NoteSize { Small, Medium, Large }

public static class NoteSizeExtensions
{
    public static (double Width, double Height) ToDimensions(this NoteSize size) => size switch
    {
        NoteSize.Small => (200, 200),
        NoteSize.Medium => (280, 280),
        NoteSize.Large => (380, 380),
        _ => (280, 280),
    };
}

public sealed class ChecklistItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = string.Empty;
    public bool IsDone { get; set; }
}

public sealed class Reminder
{
    /// <summary>Momento del aviso (hora local con offset).</summary>
    public DateTimeOffset DueAt { get; set; }

    /// <summary>True cuando ya se mostró la notificación de Windows.</summary>
    public bool IsNotified { get; set; }
}

public sealed class Note
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Etiqueta visible: cliente, expediente…</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>FlowDocument serializado en XAML (formato básico). Vacío = nota sin texto.</summary>
    public string RichContent { get; set; } = string.Empty;

    /// <summary>Texto plano derivado de <see cref="RichContent"/>, usado para búsqueda.</summary>
    public string PlainText { get; set; } = string.Empty;

    public List<ChecklistItem> Checklist { get; set; } = [];

    public NoteColor Color { get; set; } = NoteColor.Yellow;
    public NoteSize Size { get; set; } = NoteSize.Medium;

    /// <summary>Tamaño base de letra en DIPs.</summary>
    public double FontSize { get; set; } = 14;

    /// <summary>Posición (esquina superior izquierda) en PÍXELES FÍSICOS de la pantalla virtual.
    /// Se usan píxeles y no DIPs porque con monitores de escalas distintas las DIPs no son continuas.</summary>
    public double Left { get; set; }
    public double Top { get; set; }

    public bool IsPositionLocked { get; set; }

    public Reminder? Reminder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Distinto de null = la nota está en la papelera.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsInTrash => DeletedAt is not null;
}

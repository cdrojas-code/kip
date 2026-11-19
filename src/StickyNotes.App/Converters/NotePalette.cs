using System.Windows;
using System.Windows.Media;
using StickyNotes.Core.Models;

namespace StickyNotes.App.Converters;

public enum NoteColorPart { Body, Header, Border }

/// <summary>Acceso a la paleta definida en Themes/NoteColors.xaml.</summary>
public static class NotePalette
{
    public static Brush GetBrush(NoteColor color, NoteColorPart part) =>
        Application.Current?.TryFindResource($"Note.{color}.{part}") as Brush ?? Brushes.LightYellow;

    /// <summary>Nombre en español para tooltips.</summary>
    public static string DisplayName(NoteColor color) => color switch
    {
        NoteColor.Yellow => "Amarillo",
        NoteColor.Pink => "Rosa",
        NoteColor.Green => "Verde",
        NoteColor.Blue => "Azul",
        NoteColor.Orange => "Naranja",
        NoteColor.Purple => "Morado",
        _ => color.ToString(),
    };
}

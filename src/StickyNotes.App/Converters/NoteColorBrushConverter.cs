using System.Globalization;
using System.Windows.Data;
using StickyNotes.Core.Models;

namespace StickyNotes.App.Converters;

/// <summary>NoteColor → Brush. ConverterParameter: "Body" (por defecto), "Header" o "Border".</summary>
public sealed class NoteColorBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var color = value is NoteColor c ? c : NoteColor.Yellow;
        var part = Enum.TryParse<NoteColorPart>(parameter as string, ignoreCase: true, out var p) ? p : NoteColorPart.Body;
        return NotePalette.GetBrush(color, part);
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

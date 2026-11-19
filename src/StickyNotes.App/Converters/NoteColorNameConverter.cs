using System.Globalization;
using System.Windows.Data;
using StickyNotes.Core.Models;

namespace StickyNotes.App.Converters;

/// <summary>NoteColor → nombre en español (tooltips del selector de color).</summary>
public sealed class NoteColorNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is NoteColor c ? NotePalette.DisplayName(c) : string.Empty;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

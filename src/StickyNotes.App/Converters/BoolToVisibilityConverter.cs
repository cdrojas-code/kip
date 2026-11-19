using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StickyNotes.App.Converters;

/// <summary>bool → Visibility. Con <see cref="Invert"/> = true, false se muestra.
/// También acepta string: visible si no está vacío (o vacío, invertido).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value switch
        {
            bool b => b,
            string s => !string.IsNullOrEmpty(s),
            int i => i != 0,
            null => false,
            _ => true,
        };
        return flag ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

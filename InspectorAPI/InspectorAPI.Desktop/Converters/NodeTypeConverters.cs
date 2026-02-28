using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace InspectorAPI.Desktop.Converters;

/// <summary>
/// Converts a hex color string (e.g. "#00875A") to a SolidColorBrush for use in AXAML bindings.
/// </summary>
public sealed class StringToBrushConverter : IValueConverter
{
    public static readonly StringToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex)
        {
            try { return new SolidColorBrush(Color.Parse(hex)); }
            catch { /* fall through */ }
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

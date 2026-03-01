using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

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

/// <summary>
/// Converts an HTTP method string to the matching SolidColorBrush defined in App.axaml
/// (e.g. "GET" → GetMethodBrush, "POST" → PostMethodBrush, …).
/// </summary>
public sealed class MethodBrushConverter : IValueConverter
{
    public static readonly MethodBrushConverter Instance = new();

    private static readonly Dictionary<string, string> _resourceKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GET"]     = "GetMethodBrush",
        ["POST"]    = "PostMethodBrush",
        ["PUT"]     = "PutMethodBrush",
        ["DELETE"]  = "DeleteMethodBrush",
        ["PATCH"]   = "PatchMethodBrush",
        ["HEAD"]    = "HeadMethodBrush",
        ["OPTIONS"] = "OptionsMethodBrush",
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is string method && _resourceKeys.TryGetValue(method, out var k)
            ? k : "GetMethodBrush";

        if (Application.Current?.Resources.TryGetResource(key, ThemeVariant.Default, out var res) == true
            && res is IBrush brush)
            return brush;

        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

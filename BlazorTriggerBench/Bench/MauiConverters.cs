using System.Globalization;

namespace BlazorTriggerBench.Bench;

// Converters for the MAUI-native bench page (mirror the same per-kind palette as Blazor/Reactor).

public sealed class HexToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && s.Length > 0 ? Color.FromArgb(s) : Colors.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class FragForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is FragKind k ? Color.FromArgb(Fg(k)) : Colors.White;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string Fg(FragKind k) => k switch
    {
        FragKind.Keyword => "#C586C0",
        FragKind.Editable => "#9CDCFE",
        FragKind.Operator => "#D7BA7D",
        FragKind.Paren => "#808080",
        FragKind.Type => "#FFFFFF",
        FragKind.Badge => "#1E1E1E",
        _ => "#CCCCCC",
    };
}

public sealed class FragBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string hex = value is FragKind k ? Bg(k) : "";
        return hex.Length > 0 ? Color.FromArgb(hex) : Colors.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string Bg(FragKind k) => k switch
    {
        FragKind.Type => "#264F78",
        FragKind.Badge => "#4EC9B0",
        FragKind.Editable => "#2D2D30",
        _ => "",
    };
}

public sealed class IndentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new Thickness(value is int d ? d * 16 : 0, 0, 0, 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

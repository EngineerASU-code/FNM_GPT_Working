using System;
using System.Globalization;
using System.Windows.Data;

namespace Configurator;

public sealed class LastPathSegmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return text;
        var parts = text.Split(new[] { '.', '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? text : parts[^1];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

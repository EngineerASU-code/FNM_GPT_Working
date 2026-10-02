using System;
using System.Globalization;
using System.Windows.Data;

namespace Configurator;

public sealed class BoolToAvailabilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? "Используется" : "Не используется";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OpenClawCompanion.Converters;

public class BoolInvertConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            var inverted = !b;
            // If target is Visibility, return the proper enum value
            if (targetType == typeof(Visibility))
                return inverted ? Visibility.Visible : Visibility.Collapsed;
            return inverted;
        }
        return targetType == typeof(Visibility) ? Visibility.Collapsed : false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        if (value is Visibility v)
            return v != Visibility.Visible;
        return false;
    }
}

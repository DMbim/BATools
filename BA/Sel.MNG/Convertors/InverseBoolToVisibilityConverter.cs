using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Visibility = System.Windows.Visibility;

namespace BATools.SelectionManager.Converters
{
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true ? System.Windows.Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility.Collapsed;
        }
    }
}
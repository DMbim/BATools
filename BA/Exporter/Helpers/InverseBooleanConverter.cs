using System;
using System.Globalization;
using System.Windows.Data;

namespace BA_Tools.ScheduleExporter.Helpers
{
    /// <summary>
    /// Inverts a boolean value. Used in XAML to bind IsEnabled/IsChecked to the
    /// complement of a bool property (e.g., ComboBox enabled when UseActiveSchedule = false).
    /// </summary>
    [ValueConversion(typeof(bool), typeof(bool))]
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? (object)!b : false;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? (object)!b : false;
    }
}

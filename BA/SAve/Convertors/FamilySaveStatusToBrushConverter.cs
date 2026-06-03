using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using BA.Families.Models;
using WpfColor = System.Windows.Media.Color;

namespace BA.Families.Converters
{
    public sealed class FamilySaveStatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not FamilySaveStatus status)
                return SystemColors.ControlTextBrush;

            return status switch
            {
                FamilySaveStatus.Saved => new SolidColorBrush(WpfColor.FromRgb(46, 160, 67)),
                FamilySaveStatus.Error => new SolidColorBrush(WpfColor.FromRgb(200, 40, 40)),
                FamilySaveStatus.Skipped => new SolidColorBrush(WpfColor.FromRgb(150, 120, 20)),
                FamilySaveStatus.Saving => new SolidColorBrush(WpfColor.FromRgb(30, 100, 200)),
                _ => SystemColors.GrayTextBrush
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
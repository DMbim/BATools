// FILE: BA_Tools/Warnings/Views/SeverityBadgeConverters.cs
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BA.Warnings.Views
{
    // Hardcoded hex colors, not tied to AppResources.xaml brush keys: I don't
    // have that resource dictionary's key names in front of me, and an
    // undefined StaticResource reference throws at XAML parse time, which is
    // worse than a plain hex color that just doesn't match the theme exactly.
    // Swap these for real Brush.* keys once you confirm what's available.
    public sealed class SeverityToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int severity = value is int i ? i : 0;
            return severity switch
            {
                5 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB0, 0x2A, 0x2A)),
                4 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x6A, 0x1F)),
                3 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB0, 0x9A, 0x1F)),
                2 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x6E, 0x8F)),
                1 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x5A, 0x5A, 0x5A)),
                _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0x38, 0x38))
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public sealed class SeverityToLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int severity = value is int i ? i : 0;
            return severity == 0 ? "?" : severity.ToString(culture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using BA.Core.Enums;
using Color = System.Windows.Media.Color;

namespace BA.UI.Converters
{
    [ValueConversion(typeof(ComputationStatus), typeof(Brush))]
    public sealed class StatusToColorConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType,
            object? parameter, CultureInfo culture)
        {
            if (value is not ComputationStatus status)
                return Brushes.Gray;

            return status switch
            {
                ComputationStatus.Success =>
                    new SolidColorBrush(Color.FromRgb(0, 200, 80)),
                ComputationStatus.SkippedNotPlaced =>
                    new SolidColorBrush(Color.FromRgb(108, 112, 134)),
                ComputationStatus.SkippedInsufficientGeometry =>
                    new SolidColorBrush(Color.FromRgb(255, 165, 0)),
                ComputationStatus.SkippedExcludedByISOCategory =>
                    new SolidColorBrush(Color.FromRgb(108, 112, 134)),
                ComputationStatus.Failed =>
                    new SolidColorBrush(Color.FromRgb(237, 135, 150)),
                _ =>
                    Brushes.Gray
            };
        }

        public object ConvertBack(object? value, Type targetType,
            object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
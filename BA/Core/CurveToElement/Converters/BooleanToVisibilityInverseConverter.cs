// File: BA/Core/CurveToElement/Converters/BooleanToVisibilityInverseConverter.cs
// Action: CREATE NEW

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace BA.Core.CurveToElement.Converters
{
    /// <summary>
    /// True -> Collapsed, False -> Visible. Used to toggle the Unconnected-height panel vs the
    /// Up-To-Level panel in CurveToElementWindow based on which one is NOT the active mode.
    /// </summary>
    public class BooleanToVisibilityInverseConverter : MarkupExtension, IValueConverter
    {
        private static BooleanToVisibilityInverseConverter _instance;

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return _instance ??= new BooleanToVisibilityInverseConverter();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return boolValue ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            return System.Windows.Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("BooleanToVisibilityInverseConverter does not support ConvertBack.");
        }
    }
}
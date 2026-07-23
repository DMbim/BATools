// File: BA/Core/CurveToElement/Converters/EnumToBooleanConverter.cs
// Action: CREATE NEW

using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using Binding = System.Windows.Data.Binding;

namespace BA.Core.CurveToElement.Converters
{
    /// <summary>
    /// Standard enum-to-RadioButton binding converter. Binds IsChecked directly to an enum
    /// property (e.g. HeightMode) with ConverterParameter set to the enum value that RadioButton
    /// represents. Two-way: checking the RadioButton writes that enum value back to the source
    /// property, which is what CurveTypeGroupViewModel.HeightMode's setter expects - unlike the
    /// read-only IsUnconnectedHeightMode/IsUpToLevelHeightMode properties, which cannot support
    /// a write-back and should not be bound directly to IsChecked.
    /// </summary>
    public class EnumToBooleanConverter : MarkupExtension, IValueConverter
    {
        private static EnumToBooleanConverter _instance;

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return _instance ??= new EnumToBooleanConverter();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;
            return value.Equals(parameter);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isChecked && isChecked && parameter != null)
                return parameter;
            return Binding.DoNothing;
        }
    }
}
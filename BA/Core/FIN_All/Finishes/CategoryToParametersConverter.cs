using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace BA.UI.Converters
{
    /// <summary>
    /// Maps a category key string (e.g. "Ceiling", "Floor") to the list of available
    /// parameter names discovered on elements of that category in the active document.
    /// Map is populated and replaced by the owning window after each model scan. This
    /// converter has no Revit API dependency of its own, it is a pure lookup.
    /// </summary>
    public sealed class CategoryToParametersConverter : IValueConverter
    {
        public Dictionary<string, List<string>> Map { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string category && Map.TryGetValue(category, out var list))
                return list;

            return Array.Empty<string>();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("CategoryToParametersConverter is one way only.");
        }
    }
}
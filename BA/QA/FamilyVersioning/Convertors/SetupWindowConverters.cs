using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Visibility = System.Windows.Visibility;

namespace BA.QA.FamilyVersioning.Converters
{
    /// <summary>
    /// Standard bool-to-Visibility converter: true -> Visible, false -> Collapsed.
    /// Defined locally rather than referencing BATools.SelectionManager.Converters.
    /// InverseBoolToVisibilityConverter elsewhere in the codebase, that converter has
    /// the opposite mapping (true -> Collapsed) and lives in an unrelated feature
    /// module under a legacy "BATools" namespace root that is being phased out in
    /// favor of "BA", referencing it would both invert this window's logic and
    /// create an unwanted cross-module dependency.
    /// </summary>
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility.Visible;
        }
    }

    /// <summary>
    /// Converts the SelectedBuilding.Enabled bool into the label text for the
    /// toggle button ("Disable" when currently enabled, "Enable" when currently
    /// disabled). Bound as a one-way converter, the button's actual state change
    /// happens through ToggleEnabledCommand in the ViewModel, not through this
    /// converter, this only controls what the button says.
    /// </summary>
    [ValueConversion(typeof(bool), typeof(string))]
    public sealed class EnabledToToggleLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool enabled)
            {
                return enabled ? "Disable Selected" : "Enable Selected";
            }

            // SelectedBuilding may be null (nothing selected), Binding.DoNothing keeps
            // whatever the button currently displays rather than throwing or showing
            // a converter failure placeholder.
            return "Enable / Disable Selected";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException(
                "EnabledToToggleLabelConverter is one-way only, it converts a bool state into display text " +
                "and has no meaningful inverse, ConvertBack should never be invoked given the binding mode " +
                "used in FamilyVersioningSetupWindow.xaml is OneWay by default for this converter's usage.");
        }
    }
}

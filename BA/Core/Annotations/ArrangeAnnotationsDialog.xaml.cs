using Autodesk.Revit.DB;
using System;
using System.Globalization;
using System.Windows;

using BA.BIM.Core.Annotations;

namespace BA.BIM.Commands.Anno
{
    public partial class ArrangeAnnotationsDialog : Window
    {
        private ArrangeConfig _result;

        private ArrangeAnnotationsDialog()
        {
            InitializeComponent();

            // Default selection on open: Resolve collisions
            RbResolveCollisions.IsChecked = true;
        }

        /// <summary>
        /// Shows the dialog modally and returns the configured ArrangeConfig,
        /// or null if the user cancelled / closed the dialog without applying.
        /// cfg.Mode will be ArrangeMode.Cancel if the dialog was dismissed without Apply.
        /// </summary>
        public static ArrangeConfig GetConfig()
        {
            var dlg = new ArrangeAnnotationsDialog();
            bool? dialogResult = dlg.ShowDialog();

            if (dialogResult != true || dlg._result == null)
                return null;

            return dlg._result;
        }

        private void ModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (GbResolve == null || GbSnapGrid == null)
                return; // fires during InitializeComponent before all controls are ready

            var rb = sender as System.Windows.Controls.RadioButton;
            string tag = rb?.Tag as string;

            GbResolve.Visibility = tag == "ResolveCollisions" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            GbSnapGrid.Visibility = tag == "SnapGrid" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        private void CbAutoMargin_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (TbFixedMargin == null || CbAutoMargin == null)
                return;

            TbFixedMargin.IsEnabled = CbAutoMargin.IsChecked != true;
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            ArrangeMode mode = GetSelectedMode();

            if (mode == ArrangeMode.Cancel)
            {
                MessageBox.Show("Select a mode.", "Arrange Annotations", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParseMm(TbGap.Text, "Gap", out double gapInternal))
                return;

            var cfg = new ArrangeConfig
            {
                Mode = mode,
                Gap = gapInternal,
                Iterations = 30,
                Damping = 0.75,
                UseAutoMargin = true,
                FixedMargin = UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters),
                MaxDisplacementFactor = 3.0,
            };

            if (mode == ArrangeMode.ResolveCollisions)
            {
                if (!TryParseInt(TbIterations.Text, "Iterations", out int iterations))
                    return;

                if (!TryParseDouble(TbDamping.Text, "Damping", out double damping))
                    return;

                if (damping < 0 || damping > 1)
                {
                    MessageBox.Show("Damping must be between 0 and 1.", "Arrange Annotations", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (iterations < 1)
                {
                    MessageBox.Show("Iterations must be at least 1.", "Arrange Annotations", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                cfg.Iterations = iterations;
                cfg.Damping = damping;
                cfg.UseAutoMargin = CbAutoMargin.IsChecked == true;

                if (!cfg.UseAutoMargin)
                {
                    if (!TryParseMm(TbFixedMargin.Text, "Fixed margin", out double fixedMarginInternal))
                        return;

                    cfg.FixedMargin = fixedMarginInternal;
                }
            }
            else if (mode == ArrangeMode.SnapGrid)
            {
                if (!TryParseDouble(TbMaxDisplacementFactor.Text, "Max displacement factor", out double factor))
                    return;

                if (factor <= 0)
                {
                    MessageBox.Show("Max displacement factor must be greater than 0.", "Arrange Annotations", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                cfg.MaxDisplacementFactor = factor;
            }

            _result = cfg;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _result = new ArrangeConfig { Mode = ArrangeMode.Cancel };
            DialogResult = false;
            Close();
        }

        private ArrangeMode GetSelectedMode()
        {
            if (RbResolveCollisions.IsChecked == true) return ArrangeMode.ResolveCollisions;
            if (RbSnapGrid.IsChecked == true) return ArrangeMode.SnapGrid;
            if (RbDistributeHorizontal.IsChecked == true) return ArrangeMode.DistributeHorizontal;
            if (RbDistributeVertical.IsChecked == true) return ArrangeMode.DistributeVertical;
            if (RbStackVertical.IsChecked == true) return ArrangeMode.StackListVertical;
            if (RbStackHorizontal.IsChecked == true) return ArrangeMode.StackListHorizontal;
            if (RbSnapToGuideLine.IsChecked == true) return ArrangeMode.SnapToGuideLine;
            return ArrangeMode.Cancel;
        }

        // ---------------- Parsing helpers ----------------

        private static bool TryParseDouble(string text, string fieldName, out double value)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;

            // fall back to current culture in case the user typed a comma decimal separator
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                return true;

            MessageBox.Show($"'{fieldName}' must be a valid number.", "Arrange Annotations", MessageBoxButton.OK, MessageBoxImage.Warning);
            value = 0;
            return false;
        }

        private static bool TryParseInt(string text, string fieldName, out int value)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return true;

            MessageBox.Show($"'{fieldName}' must be a whole number.", "Arrange Annotations", MessageBoxButton.OK, MessageBoxImage.Warning);
            value = 0;
            return false;
        }

        private static bool TryParseMm(string text, string fieldName, out double internalUnits)
        {
            if (!TryParseDouble(text, fieldName, out double mm))
            {
                internalUnits = 0;
                return false;
            }

            if (mm < 0)
            {
                MessageBox.Show($"'{fieldName}' cannot be negative.", "Arrange Annotations", MessageBoxButton.OK, MessageBoxImage.Warning);
                internalUnits = 0;
                return false;
            }

            internalUnits = UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
            return true;
        }
    }
}

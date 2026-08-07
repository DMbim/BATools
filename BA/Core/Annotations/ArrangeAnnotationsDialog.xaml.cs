using Autodesk.Revit.DB;
using BA.Core.Settings;
using System;
using System.Globalization;
using System.Windows;
using BA.BIM.Core.Annotations;

namespace BA.BIM.Commands.Anno
{
    public partial class ArrangeAnnotationsDialog : Window
    {
        // ---- Settings keys ----
        private const string KeyLeft = "ArrangeDlg.Left";
        private const string KeyTop = "ArrangeDlg.Top";
        private const string KeyMode = "ArrangeDlg.Mode";
        private const string KeyGap = "ArrangeDlg.Gap";
        private const string KeyIterations = "ArrangeDlg.Iterations";
        private const string KeyDamping = "ArrangeDlg.Damping";
        private const string KeyAutoMargin = "ArrangeDlg.UseAutoMargin";
        private const string KeyMaxFactor = "ArrangeDlg.MaxDisplacementFactor";

        private ArrangeConfig _result;

        private ArrangeAnnotationsDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Shows the dialog modally. Returns null if cancelled.
        /// cfg.Mode == ArrangeMode.Cancel if dismissed without Apply.
        /// </summary>
        public static ArrangeConfig GetConfig()
        {
            var dlg = new ArrangeAnnotationsDialog();
            bool? result = dlg.ShowDialog();

            if (result != true || dlg._result == null)
                return null;

            return dlg._result;
        }

        // ---- Lifecycle ----

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var s = PluginSettingsStore.Load();

            double left = s.GetDouble(KeyLeft, double.NaN);
            double top = s.GetDouble(KeyTop, double.NaN);

            if (!double.IsNaN(left) && !double.IsNaN(top) && IsPositionOnScreen(left, top))
            {
                Left = left;
                Top = top;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
                    Top = (SystemParameters.PrimaryScreenHeight - ActualHeight) / 2;
                }));
            }

            RestoreFields(s);
            UpdateFieldAvailability();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var s = PluginSettingsStore.Load();

            s.SetDouble(KeyLeft, Left);
            s.SetDouble(KeyTop, Top);

            PersistFields(s);

            PluginSettingsStore.Save(s);
        }

        // ---- Field restore/persist ----

        private void RestoreFields(PluginSettings s)
        {
            string modeName = s.GetString(KeyMode, ArrangeMode.ResolveCollisions.ToString());
            if (Enum.TryParse<ArrangeMode>(modeName, out var mode))
                SelectMode(mode);
            else
                RbResolveCollisions.IsChecked = true;

            double gapMm = s.GetDouble(KeyGap, 6.0);
            TbGap.Text = gapMm.ToString("G", CultureInfo.InvariantCulture);

            int iterations = (int)s.GetDouble(KeyIterations, 30);
            TbIterations.Text = iterations.ToString(CultureInfo.InvariantCulture);

            double damping = s.GetDouble(KeyDamping, 0.75);
            TbDamping.Text = damping.ToString("G", CultureInfo.InvariantCulture);

            bool autoMargin = s.GetBool(KeyAutoMargin, true);
            CbAutoMargin.IsChecked = autoMargin;

            double maxFactor = s.GetDouble(KeyMaxFactor, 3.0);
            TbMaxDisplacementFactor.Text = maxFactor.ToString("G", CultureInfo.InvariantCulture);
        }

        private void PersistFields(PluginSettings s)
        {
            s.SetString(KeyMode, GetSelectedMode().ToString());

            if (TryParseDouble(TbGap.Text, out double gapMm))
                s.SetDouble(KeyGap, gapMm);

            if (TryParseDouble(TbIterations.Text, out double iter))
                s.SetDouble(KeyIterations, Math.Round(iter));

            if (TryParseDouble(TbDamping.Text, out double damping))
                s.SetDouble(KeyDamping, damping);

            s.SetBool(KeyAutoMargin, CbAutoMargin.IsChecked == true);

            if (TryParseDouble(TbMaxDisplacementFactor.Text, out double factor))
                s.SetDouble(KeyMaxFactor, factor);
        }

        private void SelectMode(ArrangeMode mode)
        {
            switch (mode)
            {
                case ArrangeMode.ResolveCollisions: RbResolveCollisions.IsChecked = true; break;
                case ArrangeMode.SnapGrid: RbSnapGrid.IsChecked = true; break;
                case ArrangeMode.DistributeHorizontal: RbDistributeHorizontal.IsChecked = true; break;
                case ArrangeMode.DistributeVertical: RbDistributeVertical.IsChecked = true; break;
                case ArrangeMode.StackListVertical: RbStackVertical.IsChecked = true; break;
                case ArrangeMode.StackListHorizontal: RbStackHorizontal.IsChecked = true; break;
                case ArrangeMode.SnapToGuideLine: RbSnapToGuideLine.IsChecked = true; break;
                case ArrangeMode.SpiralPack: RbSpiralPack.IsChecked = true; break;
                default: RbResolveCollisions.IsChecked = true; break;
            }
        }

        // ---- Mode radio ----

        private void ModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            UpdateFieldAvailability();
        }

        private void CbAutoMargin_CheckedChanged(object sender, RoutedEventArgs e)
        {
            UpdateFieldAvailability();
        }

        /// <summary>
        /// Single source of truth for which fields actually affect the currently
        /// selected mode. Groups whose settings are unused by the mode are hidden
        /// entirely (Resolve collisions settings, Snap to grid settings). The Gap
        /// field is grayed out (disabled + dimmed) rather than hidden when it has
        /// no effect, since Gap is a shared control across most modes and hiding
        /// it would be more disruptive than dimming it in place.
        /// </summary>
        private void UpdateFieldAvailability()
        {
            if (GbResolve == null || GbSnapGrid == null || GbSpacing == null)
                return; // fires during InitializeComponent before all elements exist

            ArrangeMode mode = GetSelectedMode();

            GbResolve.Visibility = mode == ArrangeMode.ResolveCollisions ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            GbSnapGrid.Visibility = mode == ArrangeMode.SnapGrid ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            // Gap has zero effect on Distribute modes: they only align on the shared
            // axis and preserve position on the other axis, there is no spacing to
            // control.
            bool gapAppliesToMode = mode != ArrangeMode.DistributeHorizontal
                                  && mode != ArrangeMode.DistributeVertical;

            // In ResolveCollisions, Gap is only read when Auto margin is off.
            bool gapAppliesGivenAutoMargin = mode != ArrangeMode.ResolveCollisions
                                           || CbAutoMargin.IsChecked != true;

            bool gapEnabled = gapAppliesToMode && gapAppliesGivenAutoMargin;

            GbSpacing.IsEnabled = gapEnabled;
            GbSpacing.Opacity = gapEnabled ? 1.0 : 0.5;

            GbSpacing.ToolTip = gapEnabled
                ? null
                : (!gapAppliesToMode
                    ? "Gap has no effect in this mode. Horizontal/vertical spacing between elements is preserved as-is."
                    : "Gap is ignored while Auto margin is enabled above.");
        }

        // ---- Apply / Cancel ----

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            ArrangeMode mode = GetSelectedMode();

            if (mode == ArrangeMode.Cancel)
            {
                MessageBox.Show("Select a mode.", "Arrange Annotations",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MaxDisplacementFactor = 3.0,
            };

            if (mode == ArrangeMode.ResolveCollisions)
            {
                if (!TryParseInt(TbIterations.Text, "Iterations", out int iterations))
                    return;

                if (!TryParseDouble(TbDamping.Text, out double damping) || damping < 0 || damping > 1)
                {
                    MessageBox.Show("Damping must be between 0 and 1.", "Arrange Annotations",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (iterations < 1)
                {
                    MessageBox.Show("Iterations must be at least 1.", "Arrange Annotations",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                cfg.Iterations = iterations;
                cfg.Damping = damping;
                cfg.UseAutoMargin = CbAutoMargin.IsChecked == true;
            }
            else if (mode == ArrangeMode.SnapGrid)
            {
                if (!TryParseDouble(TbMaxDisplacementFactor.Text, out double factor) || factor <= 0)
                {
                    MessageBox.Show("Max displacement factor must be greater than 0.", "Arrange Annotations",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
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
            if (RbSpiralPack.IsChecked == true) return ArrangeMode.SpiralPack;
            return ArrangeMode.Cancel;
        }

        // ---- Screen bounds guard ----

        private static bool IsPositionOnScreen(double left, double top)
        {
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var b = screen.WorkingArea;
                if (left + 100 > b.Left && left < b.Right &&
                    top + 50 > b.Top && top < b.Bottom)
                    return true;
            }
            return false;
        }

        // ---- Parsing helpers ----

        private static bool TryParseDouble(string text, out double value)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                return true;
            return false;
        }

        private static bool TryParseInt(string text, string fieldName, out int value)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return true;

            MessageBox.Show($"'{fieldName}' must be a whole number.", "Arrange Annotations",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            value = 0;
            return false;
        }

        private static bool TryParseMm(string text, string fieldName, out double internalUnits)
        {
            if (!TryParseDouble(text, out double mm))
            {
                MessageBox.Show($"'{fieldName}' must be a valid number.", "Arrange Annotations",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                internalUnits = 0;
                return false;
            }

            if (mm < 0)
            {
                MessageBox.Show($"'{fieldName}' cannot be negative.", "Arrange Annotations",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                internalUnits = 0;
                return false;
            }

            internalUnits = UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
            return true;
        }
    }
}
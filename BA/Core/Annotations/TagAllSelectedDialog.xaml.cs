using Autodesk.Revit.DB;
using BA.Core.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BA.BIM.Commands.Anno
{
    public partial class TagAllSelectedDialog : Window
    {
        // ---- Settings keys ----
        private const string KeyLeft = "TagAllDlg.Left";
        private const string KeyTop = "TagAllDlg.Top";
        private const string KeyUseLeader = "TagAllDlg.UseLeader";
        private const string KeyGap = "TagAllDlg.Gap";
        private const string KeyIterations = "TagAllDlg.Iterations";
        private const string KeyDamping = "TagAllDlg.Damping";
        // Tag type per category stored as "TagAllDlg.TagType.{CategoryName}" -> "FamilyName:TypeName"

        private readonly List<CategoryTagOptions> _categoryOptions;
        private readonly Dictionary<long, ComboBox> _comboByCategoryKey = new();
        private TagAllSettingsResult _result;

        private TagAllSelectedDialog(List<CategoryTagOptions> categoryOptions)
        {
            InitializeComponent();
            _categoryOptions = categoryOptions;
            BuildCategoryRows();
        }

        public static TagAllSettingsResult GetSettings(List<CategoryTagOptions> categoryOptions)
        {
            var dlg = new TagAllSelectedDialog(categoryOptions);
            bool? result = dlg.ShowDialog();

            if (result != true || dlg._result == null)
                return null;

            return dlg._result;
        }

        // ---- Lifecycle ----

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var s = PluginSettingsStore.Load();

            // Restore window position.
            double left = s.GetDouble(KeyLeft, double.NaN);
            double top = s.GetDouble(KeyTop, double.NaN);

            if (!double.IsNaN(left) && !double.IsNaN(top) && IsPositionOnScreen(left, top))
            {
                Left = left;
                Top = top;
            }
            else
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
                    Top = (SystemParameters.PrimaryScreenHeight - ActualHeight) / 2;
                }));
            }

            RestoreFields(s);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var s = PluginSettingsStore.Load();

            s.SetDouble(KeyLeft, Left);
            s.SetDouble(KeyTop, Top);

            PersistFields(s);

            PluginSettingsStore.Save(s);
        }

        // ---- Field restore / persist ----

        private void RestoreFields(PluginSettings s)
        {
            CbUseLeader.IsChecked = s.GetBool(KeyUseLeader, false);

            double gapMm = s.GetDouble(KeyGap, 6.0);
            TbGap.Text = gapMm.ToString("G", CultureInfo.InvariantCulture);

            int iterations = (int)s.GetDouble(KeyIterations, 30);
            TbIterations.Text = iterations.ToString(CultureInfo.InvariantCulture);

            double damping = s.GetDouble(KeyDamping, 0.75);
            TbDamping.Text = damping.ToString("G", CultureInfo.InvariantCulture);

            // Restore tag type selection per category by matching "FamilyName:TypeName".
            // ElementIds change per project so we match by name, falling back to the
            // default if no match is found.
            foreach (var kvp in _comboByCategoryKey)
            {
                long catKey = kvp.Key;
                var combo = kvp.Value;
                var opts = _categoryOptions.FirstOrDefault(o => o.Category.Id.Value == catKey);

                if (opts == null) continue;

                string settingsKey = $"TagAllDlg.TagType.{SanitizeCategoryName(opts.Category.Name)}";
                string savedTypeKey = s.GetString(settingsKey, "");

                if (string.IsNullOrEmpty(savedTypeKey))
                    continue; // leave default selection

                // Try to match by "FamilyName:TypeName"
                for (int i = 0; i < combo.Items.Count; i++)
                {
                    if (combo.Items[i] is TagTypeComboItem item &&
                        item.PersistenceKey == savedTypeKey)
                    {
                        combo.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        private void PersistFields(PluginSettings s)
        {
            s.SetBool(KeyUseLeader, CbUseLeader.IsChecked == true);

            if (TryParseDouble(TbGap.Text, out double gapMm))
                s.SetDouble(KeyGap, gapMm);

            if (TryParseDouble(TbIterations.Text, out double iter))
                s.SetDouble(KeyIterations, Math.Round(iter));

            if (TryParseDouble(TbDamping.Text, out double damping))
                s.SetDouble(KeyDamping, damping);

            // Persist tag type selections by "FamilyName:TypeName".
            foreach (var kvp in _comboByCategoryKey)
            {
                long catKey = kvp.Key;
                var combo = kvp.Value;
                var opts = _categoryOptions.FirstOrDefault(o => o.Category.Id.Value == catKey);

                if (opts == null) continue;

                string settingsKey = $"TagAllDlg.TagType.{SanitizeCategoryName(opts.Category.Name)}";

                if (combo.SelectedItem is TagTypeComboItem selected)
                    s.SetString(settingsKey, selected.PersistenceKey);
            }
        }

        // ---- Build category rows ----

        private void BuildCategoryRows()
        {
            var s = PluginSettingsStore.Load();

            foreach (var opt in _categoryOptions.OrderBy(o => o.Category.Name))
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 3, 0, 3)
                };

                var label = new TextBlock
                {
                    Text = $"{opt.Category.Name} ({opt.Elements.Count}):",
                    Width = 160,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = $"Tag type to use for {opt.Category.Name} elements " +
                                        $"({opt.Elements.Count} selected). Only families " +
                                        $"compatible with this category are listed."
                };

                var combo = new ComboBox { MinWidth = 200 };

                foreach (var ft in opt.AvailableTagTypes)
                {
                    string famName = ft.Family?.Name ?? "?";
                    string typeName = ft.Name;

                    combo.Items.Add(new TagTypeComboItem
                    {
                        Label = $"{famName} : {typeName}",
                        FamilySymbolId = ft.Id,
                        PersistenceKey = $"{famName}:{typeName}"
                    });
                }

                // Pre-select default (or last-used if RestoreFields hasn't run yet;
                // RestoreFields runs after this in Window_Loaded so default first).
                int defaultIndex = 0;
                for (int i = 0; i < combo.Items.Count; i++)
                {
                    if (combo.Items[i] is TagTypeComboItem item &&
                        item.FamilySymbolId == opt.DefaultTagTypeId)
                    {
                        defaultIndex = i;
                        break;
                    }
                }

                if (combo.Items.Count > 0)
                    combo.SelectedIndex = defaultIndex;

                row.Children.Add(label);
                row.Children.Add(combo);
                CategoryRowsPanel.Children.Add(row);

                _comboByCategoryKey[opt.Category.Id.Value] = combo;
            }
        }

        // ---- Apply / Cancel ----

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParseMm(TbGap.Text, "Gap", out double gapInternal))
                return;

            if (!TryParseInt(TbIterations.Text, "Iterations", out int iterations))
                return;

            if (iterations < 1)
            {
                MessageBox.Show("Iterations must be at least 1.", "Tag All Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParseDouble(TbDamping.Text, out double damping) || damping < 0 || damping > 1)
            {
                MessageBox.Show("Damping must be between 0 and 1.", "Tag All Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = new TagAllSettingsResult
            {
                UseLeader = CbUseLeader.IsChecked == true,
                Gap = gapInternal,
                Iterations = iterations,
                Damping = damping
            };

            foreach (var kvp in _comboByCategoryKey)
            {
                if (kvp.Value.SelectedItem is TagTypeComboItem selected)
                    result.SelectedTagTypeIdByCategoryKey[kvp.Key] = selected.FamilySymbolId;
            }

            if (result.SelectedTagTypeIdByCategoryKey.Count == 0)
            {
                MessageBox.Show("No tag types selected.", "Tag All Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _result = result;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _result = null;
            DialogResult = false;
            Close();
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

            MessageBox.Show($"'{fieldName}' must be a whole number.", "Tag All Selected",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            value = 0;
            return false;
        }

        private static bool TryParseMm(string text, string fieldName, out double internalUnits)
        {
            if (!TryParseDouble(text, out double mm))
            {
                MessageBox.Show($"'{fieldName}' must be a valid number.", "Tag All Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                internalUnits = 0;
                return false;
            }

            if (mm < 0)
            {
                MessageBox.Show($"'{fieldName}' cannot be negative.", "Tag All Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                internalUnits = 0;
                return false;
            }

            internalUnits = UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
            return true;
        }

        // ---- Helpers ----

        /// <summary>
        /// Removes characters that are invalid in a settings dictionary key
        /// (spaces, slashes, dots that would confuse key namespacing).
        /// </summary>
        private static string SanitizeCategoryName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unknown";
            return name.Replace(" ", "_")
                       .Replace("/", "_")
                       .Replace("\\", "_")
                       .Replace(".", "_");
        }

        private sealed class TagTypeComboItem
        {
            public string Label { get; set; }
            public ElementId FamilySymbolId { get; set; }

            /// <summary>"FamilyName:TypeName" used for settings persistence.</summary>
            public string PersistenceKey { get; set; }

            public override string ToString() => Label;
        }
    }
}

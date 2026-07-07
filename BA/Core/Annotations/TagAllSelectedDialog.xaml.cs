using Autodesk.Revit.DB;
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
        private readonly List<CategoryTagOptions> _categoryOptions;
        private readonly Dictionary<long, ComboBox> _comboByCategoryKey = new();
        private TagAllSettingsResult _result;

        private TagAllSelectedDialog(List<CategoryTagOptions> categoryOptions)
        {
            InitializeComponent();
            _categoryOptions = categoryOptions;

            BuildCategoryRows();
        }

        /// <summary>
        /// Shows the dialog modally. Returns null if cancelled.
        /// </summary>
        public static TagAllSettingsResult GetSettings(List<CategoryTagOptions> categoryOptions)
        {
            var dlg = new TagAllSelectedDialog(categoryOptions);
            bool? dialogResult = dlg.ShowDialog();

            if (dialogResult != true || dlg._result == null)
                return null;

            return dlg._result;
        }

        private void BuildCategoryRows()
        {
            foreach (var opt in _categoryOptions.OrderBy(o => o.Category.Name))
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 2, 0, 2)
                };

                var label = new TextBlock
                {
                    Text = $"{opt.Category.Name} ({opt.Elements.Count}):",
                    Width = 150,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                var combo = new ComboBox
                {
                    MinWidth = 200,
                    Tag = opt
                };

                foreach (var ft in opt.AvailableTagTypes)
                {
                    string famName = ft.Family?.Name ?? "?";
                    string typeName = ft.Name;

                    combo.Items.Add(new TagTypeComboItem
                    {
                        Label = $"{famName} : {typeName}",
                        FamilySymbolId = ft.Id
                    });
                }

                // Pre-select default
                int defaultIndex = 0;
                for (int i = 0; i < combo.Items.Count; i++)
                {
                    if (((TagTypeComboItem)combo.Items[i]).FamilySymbolId == opt.DefaultTagTypeId)
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

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParseMm(TbGap.Text, "Gap", out double gapInternal))
                return;

            if (!TryParseInt(TbIterations.Text, "Iterations", out int iterations))
                return;

            if (iterations < 1)
            {
                MessageBox.Show("Iterations must be at least 1.", "Tag All Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParseDouble(TbDamping.Text, "Damping", out double damping))
                return;

            if (damping < 0 || damping > 1)
            {
                MessageBox.Show("Damping must be between 0 and 1.", "Tag All Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                var combo = kvp.Value;
                if (combo.SelectedItem is TagTypeComboItem selected)
                {
                    result.SelectedTagTypeIdByCategoryKey[kvp.Key] = selected.FamilySymbolId;
                }
            }

            if (result.SelectedTagTypeIdByCategoryKey.Count == 0)
            {
                MessageBox.Show("No tag types selected.", "Tag All Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        // ---------------- Parsing helpers ----------------

        private static bool TryParseDouble(string text, string fieldName, out double value)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                return true;

            MessageBox.Show($"'{fieldName}' must be a valid number.", "Tag All Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            value = 0;
            return false;
        }

        private static bool TryParseInt(string text, string fieldName, out int value)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return true;

            MessageBox.Show($"'{fieldName}' must be a whole number.", "Tag All Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show($"'{fieldName}' cannot be negative.", "Tag All Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                internalUnits = 0;
                return false;
            }

            internalUnits = UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
            return true;
        }

        private sealed class TagTypeComboItem
        {
            public string Label { get; set; }
            public ElementId FamilySymbolId { get; set; }

            public override string ToString() => Label;
        }
    }
}

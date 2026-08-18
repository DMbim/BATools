using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Overhead;
using BA.UI.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CheckBox = System.Windows.Controls.CheckBox;

namespace BA.UI.Overhead
{
    public partial class OverheadConfigDialog : Window
    {
        private readonly List<(BuiltInCategory bic, string name)> _allCats;

        // Captured at construction time. Ok_Click must carry this forward explicitly,
        // since OverheadSettings.Enabled defaults to true on a bare object initializer,
        // and Ok_Click is not the action responsible for changing this flag, only
        // TurnOff_Click and TurnOn_Click are. Without this, every OK click silently reset
        // Enabled to true regardless of the actual current state, a confirmed bug in the
        // previous version of this file.
        private readonly bool _initialEnabled;

        public OverheadSettings? ResultSettings { get; private set; }

        public bool DisableRequested { get; private set; }
        public bool EnableRequested { get; private set; }
        public bool SaveRequested { get; private set; }

        public OverheadConfigDialog(Autodesk.Revit.UI.UIApplication uiapp, OverheadSettings current, Document doc)
        {
            InitializeComponent();

            var cur = current ?? OverheadSettings.Default();
            cur.Normalize();

            _initialEnabled = cur.Enabled;

            _allCats = DefaultCategories();
            PopulateCategories(_allCats, cur.SelectedCategories);

            UseNextLevelRadio.IsChecked = cur.UseNextLevelAsTop;
            UseViewTopRadio.IsChecked = !cur.UseNextLevelAsTop;

            FallbackCutBox.Text = (cur.FallbackCutMm > 0 ? cur.FallbackCutMm : 1200.0)
                .ToString("F0", CultureInfo.InvariantCulture);

            TinyThresholdBox.Text = (cur.TinyThresholdMm >= 0 ? cur.TinyThresholdMm : 50.0)
                .ToString("F0", CultureInfo.InvariantCulture);

            // Turning off something already off, or turning on something already on, is a
            // no op in the services these buttons trigger. Disabling the inapplicable
            // button rather than leaving both always clickable avoids presenting a control
            // that silently does nothing when pressed.
            TurnOffButton.IsEnabled = _initialEnabled;
            TurnOnButton.IsEnabled = !_initialEnabled;

            SearchBox.TextChanged += SearchBox_TextChanged;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            double cutMm = ParseDoubleOr(FallbackCutBox.Text, 1200.0);
            double tinyMm = ParseDoubleOr(TinyThresholdBox.Text, 50.0);

            ResultSettings = new OverheadSettings
            {
                SelectedCategories = GetSelectedFromUI(),
                UseNextLevelAsTop = UseNextLevelRadio.IsChecked == true,
                FallbackCutMm = cutMm > 0 ? cutMm : 1200.0,
                TinyThresholdMm = tinyMm >= 0 ? tinyMm : 0.0,
                Enabled = _initialEnabled
            };

            ResultSettings.Normalize();
            SaveRequested = true;

            DialogResult = true;
            Close();
        }

        // This button only requests disable, no Revit API calls here, the command handles
        // the actual transaction.
        private void TurnOff_Click(object sender, RoutedEventArgs e)
        {
            DisableRequested = true;
            DialogResult = true;
            Close();
        }

        // Symmetric to TurnOff_Click, only requests enable, no Revit API calls here.
        private void TurnOn_Click(object sender, RoutedEventArgs e)
        {
            EnableRequested = true;
            DialogResult = true;
            Close();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var q = (SearchBox.Text ?? "").Trim().ToLowerInvariant();
            var filtered = string.IsNullOrEmpty(q)
                ? _allCats
                : _allCats.Where(c => c.name.ToLowerInvariant().Contains(q)).ToList();

            var selected = GetSelectedFromUI();
            PopulateCategories(filtered, selected);
        }

        private void PopulateCategories(IEnumerable<(BuiltInCategory bic, string name)> cats,
                                        IEnumerable<BuiltInCategory> selected)
        {
            CategoryList.Items.Clear();
            var selectedSet = new HashSet<BuiltInCategory>(selected ?? Enumerable.Empty<BuiltInCategory>());

            foreach (var c in cats)
            {
                CategoryList.Items.Add(new CheckBox
                {
                    Content = c.name,
                    Tag = (int)c.bic,
                    IsChecked = selectedSet.Contains(c.bic),
                    Margin = new Thickness(2)
                });
            }
        }

        private HashSet<BuiltInCategory> GetSelectedFromUI()
        {
            var set = new HashSet<BuiltInCategory>();
            foreach (var item in CategoryList.Items)
            {
                if (item is CheckBox cb && cb.IsChecked == true)
                    set.Add((BuiltInCategory)(int)cb.Tag);
            }
            return set;
        }

        private static double ParseDoubleOr(string s, double fallback)
        {
            var style = NumberStyles.Float | NumberStyles.AllowThousands;
            var cul = CultureInfo.InvariantCulture;
            return double.TryParse(s, style, cul, out var v) ? v : fallback;
        }

        private static List<(BuiltInCategory bic, string name)> DefaultCategories() => new()
        {
            (BuiltInCategory.OST_Walls, "Walls"),
            (BuiltInCategory.OST_Casework, "Casework"),
            (BuiltInCategory.OST_GenericModel, "Generic Models"),
            (BuiltInCategory.OST_StructuralFraming, "Structural Framing"),
            (BuiltInCategory.OST_StructuralColumns, "Structural Columns"),
            (BuiltInCategory.OST_Ceilings, "Ceilings"),
            (BuiltInCategory.OST_Roofs, "Roofs"),
            (BuiltInCategory.OST_Floors, "Floors"),
            (BuiltInCategory.OST_Furniture, "Furniture"),
            (BuiltInCategory.OST_MechanicalEquipment, "Mechanical Equipment"),
            (BuiltInCategory.OST_PlumbingFixtures, "Plumbing Fixtures"),
            (BuiltInCategory.OST_DuctCurves, "Ducts"),
            (BuiltInCategory.OST_PipeCurves, "Pipes"),
            (BuiltInCategory.OST_LightingFixtures, "Lighting Fixtures"),
            (BuiltInCategory.OST_ElectricalFixtures, "Electrical Fixtures"),
        };

        private void PresetArchitecture_Click(object sender, RoutedEventArgs e)
        {
            PopulateCategories(_allCats, new HashSet<BuiltInCategory>
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Casework,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_LightingFixtures
            });
        }

        private void PresetMEP_Click(object sender, RoutedEventArgs e)
        {
            PopulateCategories(_allCats, new HashSet<BuiltInCategory>
            {
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_LightingFixtures
            });
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using BA.Settings.Rooms;

namespace BA.UI.Rooms
{
    public partial class AreaTransferSettingsWindow : Window
    {
        private readonly AreaTransferSettings _settings;
        private readonly Document _doc;

        // Cached parameter lists built once at load time
        private List<string> _roomDoubleParams = new List<string>();

        public AreaTransferSettingsWindow(AreaTransferSettings settings, Document doc)
        {
            InitializeComponent();
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));

            CollectParameters();
            PopulateDropdowns();
            RestoreSavedValues();
        }

        // -----------------------------------------------------------------------
        // Parameter collection
        // -----------------------------------------------------------------------

        private void CollectParameters()
        {
            // Grab one live placed Room to enumerate its Double-storage parameters,
            // which are the valid write targets for the summed UP/PP area values.
            Room sampleRoom = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .FirstOrDefault(r => r.Area > 0);

            if (sampleRoom != null)
            {
                foreach (Parameter p in sampleRoom.Parameters)
                {
                    if (p.Definition == null) continue;
                    string name = p.Definition.Name;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    if (p.StorageType == StorageType.Double)
                        _roomDoubleParams.Add(name);
                }

                _roomDoubleParams = _roomDoubleParams.Distinct()
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        // -----------------------------------------------------------------------
        // Dropdown population
        // -----------------------------------------------------------------------

        private void PopulateDropdowns()
        {
            RoomAreaUpParamBox.ItemsSource = _roomDoubleParams;
            RoomAreaPpParamBox.ItemsSource = _roomDoubleParams;
        }

        // -----------------------------------------------------------------------
        // Restore saved values — runs after dropdowns are populated so that
        // setting SelectedItem has something to match against.
        // -----------------------------------------------------------------------

        private void RestoreSavedValues()
        {
            SelectOrFallback(RoomAreaUpParamBox, _settings.RoomAreaUpParam);
            SelectOrFallback(RoomAreaPpParamBox, _settings.RoomAreaPpParam);

            AreaSchemeSuffixUpBox.Text = _settings.AreaSchemeSuffixUp;
            AreaSchemeSuffixPpBox.Text = _settings.AreaSchemeSuffixPp;
        }

        // Selects the item matching savedValue if it exists in the dropdown,
        // otherwise leaves the selection empty (does not throw).
        private static void SelectOrFallback(System.Windows.Controls.ComboBox box, string savedValue)
        {
            if (string.IsNullOrWhiteSpace(savedValue)) return;

            foreach (string item in box.Items)
            {
                if (string.Equals(item, savedValue, StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedItem = item;
                    return;
                }
            }

            // Saved value not in list — leave unselected so user notices
            // and does not silently inherit a stale value.
        }

        // -----------------------------------------------------------------------
        // Event handlers
        // -----------------------------------------------------------------------

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string v_roomUp = RoomAreaUpParamBox.SelectedItem as string ?? string.Empty;
            string v_roomPp = RoomAreaPpParamBox.SelectedItem as string ?? string.Empty;
            string v_suffixUp = AreaSchemeSuffixUpBox.Text?.Trim() ?? string.Empty;
            string v_suffixPp = AreaSchemeSuffixPpBox.Text?.Trim() ?? string.Empty;

            var fields = new (string Label, string Value)[]
            {
                ("Room Area UP Parameter",              v_roomUp),
                ("Room Area PP Parameter",              v_roomPp),
                ("Area Scheme suffix for UP",           v_suffixUp),
                ("Area Scheme suffix for PP",           v_suffixPp),
            };

            foreach (var (label, value) in fields)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    MessageBox.Show(
                        $"'{label}' is required.",
                        "Area Transfer Settings",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            if (string.Equals(v_suffixUp, v_suffixPp, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "UP and PP scheme suffixes cannot be identical, they would match the same Area Scheme.",
                    "Area Transfer Settings",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _settings.RoomAreaUpParam = v_roomUp;
            _settings.RoomAreaPpParam = v_roomPp;
            _settings.AreaSchemeSuffixUp = v_suffixUp;
            _settings.AreaSchemeSuffixPp = v_suffixPp;

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
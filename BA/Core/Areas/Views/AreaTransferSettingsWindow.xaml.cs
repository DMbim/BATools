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
        private List<string> _roomStringParams = new List<string>();
        private List<string> _roomDoubleParams = new List<string>();
        private List<string> _areaStringParams = new List<string>();

        // Cached raw area elements for distinct-value queries
        private List<Area> _placedAreas = new List<Area>();

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
            // Grab one live placed Room to enumerate its parameters.
            // We use a live element rather than walking BindingMap because
            // BindingMap does not expose StorageType reliably without a binding
            // lookup per definition, and it includes type params mixed with
            // instance params with no clean separation flag.
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

                    if (p.StorageType == StorageType.String)
                        _roomStringParams.Add(name);
                    else if (p.StorageType == StorageType.Double)
                        _roomDoubleParams.Add(name);
                }

                _roomStringParams = _roomStringParams.Distinct()
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _roomDoubleParams = _roomDoubleParams.Distinct()
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            // Grab one live placed Area for string params (Area Number, Area Type)
            // and cache all placed areas for distinct-value population.
            _placedAreas = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Areas)
                .WhereElementIsNotElementType()
                .Cast<Area>()
                .Where(a => a.Area > 0)
                .ToList();

            Area sampleArea = _placedAreas.FirstOrDefault();

            if (sampleArea != null)
            {
                foreach (Parameter p in sampleArea.Parameters)
                {
                    if (p.Definition == null) continue;
                    string name = p.Definition.Name;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    if (p.StorageType == StorageType.String)
                        _areaStringParams.Add(name);
                }

                _areaStringParams = _areaStringParams.Distinct()
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        // -----------------------------------------------------------------------
        // Dropdown population
        // -----------------------------------------------------------------------

        private void PopulateDropdowns()
        {
            RoomNumberParamBox.ItemsSource = _roomStringParams;
            AreaNumberParamBox.ItemsSource = _areaStringParams;
            AreaTypeParamBox.ItemsSource = _areaStringParams;
            RoomAreaUpParamBox.ItemsSource = _roomDoubleParams;
            RoomAreaPpParamBox.ItemsSource = _roomDoubleParams;
        }

        // Called at load time and whenever the Area Type param selection changes.
        // Collects distinct non-null values of the selected parameter across all
        // placed areas and uses them as the suggested items for the UP/PP value boxes.
        private void RefreshAreaTypeValueSuggestions(string selectedAreaTypeParamName)
        {
            var distinctValues = new List<string>();

            if (!string.IsNullOrWhiteSpace(selectedAreaTypeParamName))
            {
                distinctValues = _placedAreas
                    .Select(a =>
                    {
                        Parameter p = a.LookupParameter(selectedAreaTypeParamName);
                        return p?.StorageType == StorageType.String ? p.AsString() : null;
                    })
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            // Preserve whatever the user has already typed or selected
            string currentUp = AreaTypeUpValueBox.Text;
            string currentPp = AreaTypePpValueBox.Text;

            AreaTypeUpValueBox.ItemsSource = distinctValues;
            AreaTypePpValueBox.ItemsSource = distinctValues;

            AreaTypeUpValueBox.Text = currentUp;
            AreaTypePpValueBox.Text = currentPp;
        }

        // -----------------------------------------------------------------------
        // Restore saved values — runs after dropdowns are populated so that
        // setting SelectedItem has something to match against.
        // -----------------------------------------------------------------------

        private void RestoreSavedValues()
        {
            SelectOrFallback(RoomNumberParamBox, _settings.RoomNumberParam);
            SelectOrFallback(AreaNumberParamBox, _settings.AreaNumberParam);
            SelectOrFallback(AreaTypeParamBox, _settings.AreaTypeParam);
            SelectOrFallback(RoomAreaUpParamBox, _settings.RoomAreaUpParam);
            SelectOrFallback(RoomAreaPpParamBox, _settings.RoomAreaPpParam);

            // Trigger the value suggestions for the restored Area Type param
            RefreshAreaTypeValueSuggestions(_settings.AreaTypeParam);

            AreaTypeUpValueBox.Text = _settings.AreaTypeUpValue;
            AreaTypePpValueBox.Text = _settings.AreaTypePpValue;
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

        private void AreaTypeParamBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            string selected = AreaTypeParamBox.SelectedItem as string;
            RefreshAreaTypeValueSuggestions(selected);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string roomNumberParam = AreaTypeParamBox.SelectedItem as string ?? string.Empty; // wrong — see below
            string areaTypeUpValue = AreaTypeUpValueBox.Text?.Trim() ?? string.Empty;
            string areaTypePpValue = AreaTypePpValueBox.Text?.Trim() ?? string.Empty;

            // Read all seven values explicitly to give clear per-field error context
            string v_roomNumber = RoomNumberParamBox.SelectedItem as string ?? string.Empty;
            string v_areaNumber = AreaNumberParamBox.SelectedItem as string ?? string.Empty;
            string v_areaType = AreaTypeParamBox.SelectedItem as string ?? string.Empty;
            string v_roomUp = RoomAreaUpParamBox.SelectedItem as string ?? string.Empty;
            string v_roomPp = RoomAreaPpParamBox.SelectedItem as string ?? string.Empty;
            string v_upValue = AreaTypeUpValueBox.Text?.Trim() ?? string.Empty;
            string v_ppValue = AreaTypePpValueBox.Text?.Trim() ?? string.Empty;

            var fields = new (string Label, string Value)[]
            {
                ("Room Number Parameter",    v_roomNumber),
                ("Area Number Parameter",    v_areaNumber),
                ("Area Type Parameter",      v_areaType),
                ("Room Area UP Parameter",   v_roomUp),
                ("Room Area PP Parameter",   v_roomPp),
                ("UP value",                 v_upValue),
                ("PP value",                 v_ppValue),
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

            _settings.RoomNumberParam = v_roomNumber;
            _settings.AreaNumberParam = v_areaNumber;
            _settings.AreaTypeParam = v_areaType;
            _settings.RoomAreaUpParam = v_roomUp;
            _settings.RoomAreaPpParam = v_roomPp;
            _settings.AreaTypeUpValue = v_upValue;
            _settings.AreaTypePpValue = v_ppValue;

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
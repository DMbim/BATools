using System;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Settings.Rooms;

namespace BA.UI.Rooms
{
    public partial class AxisToRoomSettingsWindow : Window
    {
        private readonly Document _doc;
        private readonly AxisToRoomSettings _settings;

        public RevitLinkInstance? SelectedLinkInstance { get; private set; }

        public AxisToRoomSettingsWindow(ExternalCommandData commandData, AxisToRoomSettings settings)
        {
            InitializeComponent();

            _doc = commandData?.Application?.ActiveUIDocument?.Document
                ?? throw new ArgumentNullException(nameof(commandData));

            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            PopulateLinksDropdown();
            RestoreSavedSelection();
        }

        private void PopulateLinksDropdown()
        {
            var links = new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .OrderBy(l => l.Name)
                .ToList();

            AxisLinksComboBox.ItemsSource = links;
        }

        private void RestoreSavedSelection()
        {
            var links = AxisLinksComboBox.ItemsSource as System.Collections.Generic.IEnumerable<RevitLinkInstance>;
            if (links == null) return;

            // Prefer UniqueId (stable), fallback to Name
            var pre =
                (!string.IsNullOrWhiteSpace(_settings.SelectedLinkInstanceUniqueId)
                    ? links.FirstOrDefault(x => x.UniqueId.Equals(_settings.SelectedLinkInstanceUniqueId, StringComparison.OrdinalIgnoreCase))
                    : null)
                ?? (!string.IsNullOrWhiteSpace(_settings.SelectedLinkInstanceName)
                    ? links.FirstOrDefault(x => x.Name.Equals(_settings.SelectedLinkInstanceName, StringComparison.OrdinalIgnoreCase))
                    : null);

            if (pre != null)
                AxisLinksComboBox.SelectedItem = pre;

            DimensionCheckbox.IsChecked = _settings.PlaceDimensionVariant;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (AxisLinksComboBox.SelectedItem is not RevitLinkInstance link)
            {
                MessageBox.Show("Please select a valid Revit link.", "Axis to Room", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedLinkInstance = link;

            _settings.SelectedLinkInstanceUniqueId = link.UniqueId;
            _settings.SelectedLinkInstanceName = link.Name;
            _settings.PlaceDimensionVariant = DimensionCheckbox.IsChecked == true;

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

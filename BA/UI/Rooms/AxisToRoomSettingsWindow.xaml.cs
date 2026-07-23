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
            // Link selection is now OPTIONAL. This window configures settings shared by
            // Cmd_AxisToRoom_Local (never uses the link) and Cmd_AxisToRoom_Link (does,
            // and already enforces it at RUN time via LinkResolver.FindLinkInstance
            // returning null -- see that command's own error message). Blocking OK here
            // whenever no link was picked made it impossible to save the dimension
            // checkbox for Local-only use, which is the bug being fixed.
            if (AxisLinksComboBox.SelectedItem is RevitLinkInstance link)
            {
                SelectedLinkInstance = link;
                _settings.SelectedLinkInstanceUniqueId = link.UniqueId;
                _settings.SelectedLinkInstanceName = link.Name;
            }
            else
            {
                SelectedLinkInstance = null;
                // Deliberately NOT clearing _settings.SelectedLinkInstanceUniqueId/Name here --
                // same reasoning as Element -> Room's Local/Link handling: if a link was
                // previously configured, leave it saved so Cmd_AxisToRoom_Link keeps working
                // even if someone opens this dialog later just to toggle the dimension checkbox
                // without touching the link dropdown.
            }

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

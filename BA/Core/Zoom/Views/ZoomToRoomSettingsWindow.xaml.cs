using System;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BATools.Zoom.Settings;

namespace BATools.Zoom.Views
{
    public partial class ZoomToRoomSettingsWindow : Window
    {
        private readonly Document _doc;
        private readonly ZoomToRoomSettings _settings;

        public ZoomToRoomSettingsWindow(ExternalCommandData commandData, ZoomToRoomSettings settings)
        {
            InitializeComponent();

            _doc = commandData?.Application?.ActiveUIDocument?.Document
                ?? throw new ArgumentNullException(nameof(commandData));

            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            PopulateLinksDropdown();
            RestoreSavedValues();
        }

        private void PopulateLinksDropdown()
        {
            var links = new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .OrderBy(l => l.Name)
                .ToList();

            LinksComboBox.ItemsSource = links;
        }

        private void RestoreSavedValues()
        {
            // ZoomToRoomSettings only stores the link by Name (no UniqueId field, unlike
            // ElementToRoomSettings/AxisToRoomSettings) -- matched by name here, consistent
            // with how the commands already resolved it before this change.
            if (!string.IsNullOrWhiteSpace(_settings.SelectedRevitLinkName))
            {
                var links = LinksComboBox.ItemsSource as System.Collections.Generic.IEnumerable<RevitLinkInstance>;
                var pre = links?.FirstOrDefault(l => l.Name.Equals(_settings.SelectedRevitLinkName, StringComparison.OrdinalIgnoreCase));
                if (pre != null)
                    LinksComboBox.SelectedItem = pre;
            }

            switch (_settings.RoomIdParamMode)
            {
                case "ByName":
                    RbByName.IsChecked = true;
                    break;
                case "Shared":
                    RbShared.IsChecked = true;
                    break;
                default:
                    RbBuiltIn.IsChecked = true;
                    break;
            }

            NameTextBox.Text = _settings.RoomIdName ?? "BA_ID";
            GuidTextBox.Text = _settings.RoomIdSharedGuid ?? string.Empty;

            UpdateFieldEnablement();
        }

        private void ModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            // Guard: can fire during InitializeComponent before NameTextBox/GuidTextBox exist.
            if (NameTextBox == null || GuidTextBox == null) return;
            UpdateFieldEnablement();
        }

        private void UpdateFieldEnablement()
        {
            NameTextBox.IsEnabled = RbByName.IsChecked == true;
            GuidTextBox.IsEnabled = RbShared.IsChecked == true;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Link selection is optional -- Cmd_ZoomToRoom (local) never uses it, and
            // Cmd_ZoomToRoom_Link enforces it at RUN time (same pattern as
            // Cmd_AxisToRoom_Link / LinkResolver). Leaving a previously saved link
            // untouched if nothing's selected here, same reasoning as the other
            // shared settings windows this session.
            if (LinksComboBox.SelectedItem is RevitLinkInstance link)
                _settings.SelectedRevitLinkName = link.Name;

            if (RbByName.IsChecked == true)
            {
                var name = (NameTextBox.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show("Please enter a parameter name.", "Zoom to Room", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _settings.RoomIdParamMode = "ByName";
                _settings.RoomIdName = name;
                _settings.RoomIdSharedGuid = null;
            }
            else if (RbShared.IsChecked == true)
            {
                if (!Guid.TryParse(GuidTextBox.Text, out var guid))
                {
                    MessageBox.Show("Invalid GUID. Please enter a valid GUID or choose a different mode.", "Zoom to Room", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _settings.RoomIdParamMode = "Shared";
                _settings.RoomIdSharedGuid = guid.ToString("D");
                _settings.RoomIdName = null;
            }
            else
            {
                _settings.RoomIdParamMode = "BuiltIn";
                _settings.RoomIdName = null;
                _settings.RoomIdSharedGuid = null;
            }

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

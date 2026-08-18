using System;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Settings.Rooms;

namespace BA.UI.Rooms
{
    public partial class ElementToRoomSettingsWindow : Window
    {
        private readonly Document _doc;
        private readonly ElementToRoomSettings _settings;

        public string? SelectedCategoryName { get; private set; }
        public RevitLinkInstance? SelectedLinkInstance { get; private set; }

        public ElementToRoomSettingsWindow(ExternalCommandData commandData, ElementToRoomSettings settings)
        {
            InitializeComponent();

            _doc = commandData?.Application?.ActiveUIDocument?.Document
                ?? throw new ArgumentNullException(nameof(commandData));

            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            PopulateCategoryDropdown();
            PopulateLinkDropdown();

            // Default mode: Link if there's saved link data, otherwise Local.
            // ModeRadio_Checked fires immediately on whichever we set here and
            // handles the LblLink/E2RLinks visibility toggle.
            bool defaultToLink = !string.IsNullOrWhiteSpace(_settings.SelectedLinkInstanceUniqueId)
                || !string.IsNullOrWhiteSpace(_settings.SelectedLinkInstanceName);
            RbLink.IsChecked = defaultToLink;
            RbLocal.IsChecked = !defaultToLink;

            RestoreSavedValues();
        }

        private void ModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            // Guard: this can fire during InitializeComponent before LblLink/E2RLinks exist.
            if (LblLink == null || E2RLinks == null)
                return;

            bool isLink = RbLink.IsChecked == true;
            LblLink.Visibility = isLink ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            E2RLinks.Visibility = isLink ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        private void PopulateCategoryDropdown()
        {
            var categories = _doc.Settings.Categories
                .Cast<Category>()
                .Where(c => c != null && c.AllowsBoundParameters)
                .Select(c => c.Name)
                .OrderBy(n => n)
                .ToList();

            E2RCategories.ItemsSource = categories;
        }

        private void PopulateLinkDropdown()
        {
            var links = new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .OrderBy(l => l.Name)
                .ToList();

            E2RLinks.ItemsSource = links;
        }

        private void RestoreSavedValues()
        {
            if (!string.IsNullOrWhiteSpace(_settings.SelectedCategoryToken))
                E2RCategories.SelectedItem = _settings.SelectedCategoryToken;

            var links = E2RLinks.ItemsSource as System.Collections.Generic.IEnumerable<RevitLinkInstance>;
            if (links != null)
            {
                var pre =
                    (!string.IsNullOrWhiteSpace(_settings.SelectedLinkInstanceUniqueId)
                        ? links.FirstOrDefault(x => x.UniqueId.Equals(_settings.SelectedLinkInstanceUniqueId, StringComparison.OrdinalIgnoreCase))
                        : null)
                    ?? (!string.IsNullOrWhiteSpace(_settings.SelectedLinkInstanceName)
                        ? links.FirstOrDefault(x => x.Name.Equals(_settings.SelectedLinkInstanceName, StringComparison.OrdinalIgnoreCase))
                        : null);

                if (pre != null)
                    E2RLinks.SelectedItem = pre;
            }

            E2RLocalSourceCB.Text = _settings.SourceParameter ?? "";
            E2RTargetCB.Text = _settings.DestinationParameter ?? "";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (E2RCategories.SelectedItem is not string cat || string.IsNullOrWhiteSpace(cat))
            {
                MessageBox.Show("Please select a category.", "Element → Room", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var src = (E2RLocalSourceCB.Text ?? "").Trim();
            var dst = (E2RTargetCB.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst))
            {
                MessageBox.Show("Please specify both source and destination parameters.", "Element → Room",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedCategoryName = cat;
            _settings.SelectedCategoryToken = cat;
            _settings.SourceParameter = src;
            _settings.DestinationParameter = dst;

            if (RbLink.IsChecked == true)
            {
                if (E2RLinks.SelectedItem is not RevitLinkInstance link)
                {
                    MessageBox.Show("Please select a Revit link instance.", "Element → Room", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SelectedLinkInstance = link;
                _settings.SelectedLinkInstanceUniqueId = link.UniqueId;
                _settings.SelectedLinkInstanceName = link.Name;
            }
            // Local mode: leave any previously saved link-instance fields untouched
            // rather than clearing them, so switching back to Link mode later
            // restores the last selection instead of forcing a re-pick.

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

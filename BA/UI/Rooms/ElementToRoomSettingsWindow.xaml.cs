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
        private readonly bool _linkMode;

        public string? SelectedCategoryName { get; private set; }
        public RevitLinkInstance? SelectedLinkInstance { get; private set; }

        public ElementToRoomSettingsWindow(ExternalCommandData commandData, ElementToRoomSettings settings, bool linkMode)
        {
            InitializeComponent();

            _doc = commandData?.Application?.ActiveUIDocument?.Document
                ?? throw new ArgumentNullException(nameof(commandData));

            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _linkMode = linkMode;

            PopulateCategoryDropdown();
            PopulateLinkDropdown();

            // Show/hide link controls based on mode
            LblLink.Visibility = _linkMode ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            E2RLinks.Visibility = _linkMode ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            RestoreSavedValues();
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
            // Category
            if (!string.IsNullOrWhiteSpace(_settings.SelectedCategoryToken))
                E2RCategories.SelectedItem = _settings.SelectedCategoryToken;

            // Link
            if (_linkMode)
            {
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

            // Save to settings
            SelectedCategoryName = cat;
            _settings.SelectedCategoryToken = cat;
            _settings.SourceParameter = src;
            _settings.DestinationParameter = dst;

            if (_linkMode)
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

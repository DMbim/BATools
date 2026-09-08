using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using BA.Settings.Rooms;
using BA.Commands.Rooms;
using BA.Core.Rooms;
using BA.Core.Classification;
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
            PopulateSourceRoomParameterDropdown();
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
        private void E2RCategories_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (E2RTargetCB == null)
                return;
            PopulateDestinationParameterDropdown();
            // The previously selected destination parameter almost certainly does not
            // belong to the newly chosen category's parameter set. Clear it explicitly
            // rather than leaving a stale selection the user can no longer see is wrong.
            E2RTargetCB.SelectedItem = null;
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
        /// <summary>
        /// Source Parameter always reads from the Rooms category, independent of whatever
        /// is selected in E2RCategories -- that combo only chooses the destination category.
        /// Populated once at window load since it does not change with the category selection.
        /// </summary>
        private void PopulateSourceRoomParameterDropdown()
        {
            var roomCategory = Category.GetCategory(_doc, BuiltInCategory.OST_Rooms);
            E2RLocalSourceCB.ItemsSource = GetParameterNamesForCategory(_doc, roomCategory);
        }
        /// <summary>
        /// Destination Parameter reads from whichever category is currently selected in
        /// E2RCategories. Called from E2RCategories_SelectionChanged and once from
        /// RestoreSavedValues via the SelectionChanged that assignment triggers.
        /// </summary>
        private void PopulateDestinationParameterDropdown()
        {
            var selectedCategoryName = E2RCategories.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(selectedCategoryName))
            {
                E2RTargetCB.ItemsSource = null;
                return;
            }
            var category = _doc.Settings.Categories
                .Cast<Category>()
                .FirstOrDefault(c => c != null && c.Name != null &&
                    c.Name.Equals(selectedCategoryName, StringComparison.OrdinalIgnoreCase));
            E2RTargetCB.ItemsSource = category != null
                ? GetParameterNamesForCategory(_doc, category)
                : null;
        }
        /// <summary>
        /// Enumerates the parameter names applicable to a category via
        /// ParameterFilterUtilities.GetFilterableParametersInCommon -- this works from the
        /// category definition alone, no placed instance of that category needs to exist in
        /// the model. Covers both built-in and project/shared parameters bound to it.
        /// </summary>
        private static List<string> GetParameterNamesForCategory(Document doc, Category? category)
        {
            if (category == null) return new List<string>();
            try
            {
                var paramIds = ParameterFilterUtilities.GetFilterableParametersInCommon(
                    doc, new List<ElementId> { category.Id });
                return paramIds
                    .Select(id => GetParameterDisplayName(doc, id))
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList()!;
            }
            catch
            {
                return new List<string>();
            }
        }
        private static string? GetParameterDisplayName(Document doc, ElementId paramId)
        {
            if (paramId.Value < 0)
            {
                try
                {
                    return LabelUtils.GetLabelFor((BuiltInParameter)(int)paramId.Value);
                }
                catch
                {
                    return null;
                }
            }
            var pe = doc.GetElement(paramId) as ParameterElement;
            return pe?.GetDefinition()?.Name;
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
            if (!string.IsNullOrWhiteSpace(_settings.SourceParameter))
                E2RLocalSourceCB.SelectedItem = _settings.SourceParameter;
            if (!string.IsNullOrWhiteSpace(_settings.DestinationParameter))
                E2RTargetCB.SelectedItem = _settings.DestinationParameter;
        }
        /// <summary>
        /// Shared validation for OK and Run: reads category, source, destination and, in Link
        /// mode, the selected RevitLinkInstance straight off the live UI controls rather than
        /// from _settings, so Run acts on exactly what's on screen even if OK was never clicked.
        /// Shows a warning dialog and returns false on the first missing piece.
        /// </summary>
        private bool TryValidateSelection(out string category, out string sourceParam, out string destParam, out RevitLinkInstance? link)
        {
            category = string.Empty;
            sourceParam = string.Empty;
            destParam = string.Empty;
            link = null;
            if (E2RCategories.SelectedItem is not string cat || string.IsNullOrWhiteSpace(cat))
            {
                MessageBox.Show("Please select a category.", "Element → Room", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (E2RLocalSourceCB.SelectedItem is not string src || string.IsNullOrWhiteSpace(src))
            {
                MessageBox.Show("Please select a source (Room) parameter.", "Element → Room", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (E2RTargetCB.SelectedItem is not string dst || string.IsNullOrWhiteSpace(dst))
            {
                MessageBox.Show("Please select a destination (Element) parameter.", "Element → Room", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            category = cat;
            sourceParam = src;
            destParam = dst;
            if (RbLink.IsChecked == true)
            {
                if (E2RLinks.SelectedItem is not RevitLinkInstance selectedLink)
                {
                    MessageBox.Show("Please select a Revit link instance.", "Element → Room", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                link = selectedLink;
            }
            return true;
        }
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryValidateSelection(out var cat, out var src, out var dst, out var link))
                return;
            SelectedCategoryName = cat;
            _settings.SelectedCategoryToken = cat;
            _settings.SourceParameter = src;
            _settings.DestinationParameter = dst;
            if (RbLink.IsChecked == true)
            {
                SelectedLinkInstance = link;
                _settings.SelectedLinkInstanceUniqueId = link!.UniqueId;
                _settings.SelectedLinkInstanceName = link.Name;
            }
            // Local mode: leave any previously saved link-instance fields untouched
            // rather than clearing them, so switching back to Link mode later
            // restores the last selection instead of forcing a re-pick.
            DialogResult = true;
            Close();
        }
        /// <summary>
        /// Runs the same ElementToRoomService call Cmd_ElementToRoom_Local /
        /// Cmd_ElementToRoom_Link make, directly against the current UI selection, without
        /// closing this window. Requires the hosting command (Cmd_ElementToRoom_Settings) to
        /// be TransactionMode.Manual -- a ReadOnly host will throw the moment t.Start() runs,
        /// since the transaction restriction applies to the whole external command execution,
        /// including code invoked from this modal dialog's nested message loop.
        /// </summary>
        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryValidateSelection(out var cat, out var src, out var dst, out var link))
                return;
            var category = BA.Commands.Rooms.CategoryResolver.TryResolveCategory(_doc, cat);
            if (category == null)
            {
                MessageBox.Show($"Category '{cat}' could not be resolved.", "Element → Room", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            bool isLink = RbLink.IsChecked == true;
            // Persist the current selection before running so the standalone ribbon commands
            // pick up whatever configuration was just used here, even if OK is never clicked.
            _settings.SelectedCategoryToken = cat;
            _settings.SourceParameter = src;
            _settings.DestinationParameter = dst;
            if (link != null)
            {
                _settings.SelectedLinkInstanceUniqueId = link.UniqueId;
                _settings.SelectedLinkInstanceName = link.Name;
            }
            _settings.Save();
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            try
            {
                using (var t = new Transaction(_doc, isLink ? "BA – Element To Room (Link)" : "BA – Element To Room (Local)"))
                {
                    t.Start();
                    var stats = isLink
                        ? ElementToRoomService.AssignFromLinkedRooms(_doc, link!, category, src, dst)
                        : ElementToRoomService.AssignFromLocalRooms(_doc, category, src, dst);
                    t.Commit();
                    TaskDialog.Show(isLink ? "Element → Room (Link)" : "Element → Room (Local)",
                        $"Considered: {stats.ElementsConsidered}\n" +
                        $"Category: {cat}\n" +
                        $"Written: {stats.ElementsWritten}\n" +
                        $"  (via source fallback: {stats.ElementsWrittenViaSourceFallback}, via destination fallback: {stats.ElementsWrittenViaDestinationFallback})\n" +
                        $"No point: {stats.ElementsNoPoint}\n" +
                        $"No room: {stats.ElementsNoRoom}\n" +
                        $"Missing params: {stats.ElementsNoParams}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Run failed: {ex.Message}", "Element → Room", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
            }
        }
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
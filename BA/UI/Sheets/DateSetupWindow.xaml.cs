using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Settings;

namespace BA.UI.Sheets
{
    public partial class DateSetupWindow : Window
    {
        private static readonly string[] DateFormats =
        {
            "yy/MM/dd",
            "dd/MM/yy",
            "MM/dd/yy",
            "yyyy-MM-dd",
            "dd.MM.yyyy",
        };

        private readonly DateToolSettings _settings;

        public DateSetupWindow(ExternalCommandData commandData, DateToolSettings settings)
        {
            InitializeComponent();
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            Document doc = commandData?.Application?.ActiveUIDocument?.Document
                ?? throw new ArgumentNullException(nameof(commandData));

            PopulateFormatDropdown();

            List<string> sheetStringParams = CollectSheetStringParams(doc);
            DateParameterComboBox.ItemsSource = sheetStringParams;
            RevisionParameterComboBox.ItemsSource = sheetStringParams;

            RestoreSavedValues(sheetStringParams);
        }

        // -----------------------------------------------------------------------
        // Population
        // -----------------------------------------------------------------------

        private void PopulateFormatDropdown()
        {
            foreach (string fmt in DateFormats)
                FormatComboBox.Items.Add(fmt);
        }

        // Collects all distinct string-typed parameter names from one live
        // placed ViewSheet. ViewSheet is used because that is the element
        // category this tool writes to — parameters available here are the
        // only ones that will actually be writable at run time.
        private static List<string> CollectSheetStringParams(Document doc)
        {
            ViewSheet sampleSheet = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .FirstOrDefault(s => !s.IsPlaceholder);

            if (sampleSheet == null)
                return new List<string>();

            return sampleSheet.Parameters
                .Cast<Parameter>()
                .Where(p => p.Definition != null
                         && p.StorageType == StorageType.String
                         && !string.IsNullOrWhiteSpace(p.Definition.Name))
                .Select(p => p.Definition.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // -----------------------------------------------------------------------
        // Restore
        // -----------------------------------------------------------------------

        private void RestoreSavedValues(List<string> availableParams)
        {
            // Format: restore text directly since it is an editable combobox
            FormatComboBox.Text = string.IsNullOrWhiteSpace(_settings.SelectedFormat)
                ? DateFormats[0]
                : _settings.SelectedFormat;

            SelectOrFallback(DateParameterComboBox, availableParams, _settings.SelectedDateParam, "Issue Date");
            SelectOrFallback(RevisionParameterComboBox, availableParams, _settings.SelectedRevParam, "Revision");
        }

        // Selects savedValue if it exists in the list.
        // Falls back to fallbackValue if savedValue is missing.
        // Falls back to unselected if neither exists — user will see the empty
        // state and must pick, which is safer than silently using the wrong param.
        private static void SelectOrFallback(
            System.Windows.Controls.ComboBox box,
            List<string> items,
            string savedValue,
            string fallbackValue)
        {
            string toSelect = items.FirstOrDefault(i =>
                string.Equals(i, savedValue, StringComparison.OrdinalIgnoreCase));

            if (toSelect == null)
            {
                toSelect = items.FirstOrDefault(i =>
                    string.Equals(i, fallbackValue, StringComparison.OrdinalIgnoreCase));
            }

            if (toSelect != null)
                box.SelectedItem = toSelect;
        }

        // -----------------------------------------------------------------------
        // OK
        // -----------------------------------------------------------------------

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string format = FormatComboBox.Text?.Trim() ?? string.Empty;
            string dateParam = DateParameterComboBox.SelectedItem as string ?? string.Empty;
            string revParam = RevisionParameterComboBox.SelectedItem as string ?? string.Empty;

            var fields = new (string Label, string Value)[]
            {
                ("Date format",              format),
                ("Date parameter name",      dateParam),
                ("Revision parameter name",  revParam),
            };

            foreach (var (label, value) in fields)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    MessageBox.Show(
                        $"'{label}' is required.",
                        "Sheet Date/Revision Setup",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            _settings.SelectedFormat = format;
            _settings.SelectedDateParam = dateParam;
            _settings.SelectedRevParam = revParam;

            DialogResult = true;
            Close();
        }
    }
}
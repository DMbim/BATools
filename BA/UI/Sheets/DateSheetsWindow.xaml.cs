using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Sheets;
using BA.Settings;
using BA.UI.ViewModels;

namespace BA.UI.Sheets
{
    public partial class DateSheetsWindow : Window
    {
        public ObservableCollection<SheetRowViewModel> Sheets { get; } = new();

        private readonly Document _doc;
        private readonly DateToolSettings _settings;

        public DateSheetsWindow(ExternalCommandData commandData, DateToolSettings settings)
        {
            InitializeComponent();

            _doc = commandData.Application.ActiveUIDocument.Document;
            _settings = settings;

            LoadSheets();
            SheetsGrid.ItemsSource = Sheets;
        }

        private void LoadSheets()
        {
            var allSheets = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Sheets)
                .WhereElementIsNotElementType()
                .OfType<ViewSheet>();

            foreach (var sheet in allSheets)
            {
                var issueDateParam = sheet.LookupParameter(_settings.SelectedDateParam);
                var revisionParam = sheet.LookupParameter(_settings.SelectedRevParam);

                var issue = issueDateParam?.AsString();
                if (string.IsNullOrEmpty(issue) && issueDateParam != null && issueDateParam.StorageType != StorageType.String)
                    issue = issueDateParam.AsValueString();

                var rev = revisionParam?.StorageType == StorageType.Integer
                    ? revisionParam.AsInteger().ToString()
                    : (revisionParam?.AsString() ?? revisionParam?.AsValueString());

                Sheets.Add(new SheetRowViewModel
                {
                    SheetNumber = sheet.SheetNumber ?? "",
                    SheetName = sheet.Name ?? "",
                    IssueDate = string.IsNullOrWhiteSpace(issue) ? "—" : issue,
                    CurrentRevision = string.IsNullOrWhiteSpace(rev) ? "—" : rev
                });
            }
        }

        public System.Collections.Generic.List<SheetUpdateRow> GetSelectedRows()
            => Sheets
                .Where(s => s.UpdateDate || s.UpdateRevision || s.UpdateBoth)
                .Select(s => new SheetUpdateRow
                {
                    SheetNumber = s.SheetNumber,
                    UpdateDate = s.UpdateDate,
                    UpdateRevision = s.UpdateRevision,
                    UpdateBoth = s.UpdateBoth
                })
                .ToList();

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Parameters;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace BA.UI.Parameters
{
    public partial class CreateSharedParameterWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly Document _doc;

        private List<SharedDefRow> _defs = new();
        private List<CategoryPick> _cats = new();

        public string SharedParamFilePath { get; set; } = string.Empty;

        public CreateSharedParameterWindow(UIApplication uiApp, Document doc)
        {
            InitializeComponent();

            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));

            // Instance/type options
            CmbInstanceType.ItemsSource = new[] { "Instance", "Type" };
            CmbInstanceType.SelectedIndex = 0;

            // Groups (common)
            CmbGroup.ItemsSource = GroupCatalog.CommonGroups;
            CmbGroup.DisplayMemberPath = "Label";
            CmbGroup.SelectedIndex = 0;

            LoadCategories();
            ReloadSharedDefs();
        }

        private void LoadCategories()
        {
            _cats = _doc.Settings.Categories
                .Cast<Category>()
                .Where(c => c != null && c.AllowsBoundParameters && c.CategoryType != CategoryType.Internal)
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new CategoryPick(c))
                .ToList();

            CatsList.ItemsSource = _cats;
        }

        private void ReloadSharedDefs()
        {
            var path = string.IsNullOrWhiteSpace(SharedParamFilePath)
                ? (_uiApp.Application.SharedParametersFilename ?? string.Empty)
                : SharedParamFilePath;

            TxtPath.Text = path ?? string.Empty;

            _defs = SharedParameterFileReader.ReadAll(_uiApp.Application, path)
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ListDefs.ItemsSource = _defs;
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Shared Parameter File",
                Filter = "TXT files (*.txt)|*.txt|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dlg.ShowDialog() == true)
            {
                SharedParamFilePath = dlg.FileName;
                _uiApp.Application.SharedParametersFilename = dlg.FileName;
                ReloadSharedDefs();
            }
        }

        private void TxtFilter_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var s = (TxtFilter.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
            {
                ListDefs.ItemsSource = _defs;
                return;
            }

            ListDefs.ItemsSource = _defs.Where(d =>
                d.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void BtnBind_Click(object sender, RoutedEventArgs e)
        {
            if (ListDefs.SelectedItem is not SharedDefRow def)
            {
                TaskDialog.Show("BA – Bind Shared", "Select a shared parameter definition first.");
                return;
            }

            var selectedCats = _cats.Where(c => c.IsSelected).Select(c => c.Category).ToList();
            if (selectedCats.Count == 0)
            {
                TaskDialog.Show("BA – Bind Shared", "Select at least one category.");
                return;
            }

            bool isInstance = (CmbInstanceType.SelectedItem?.ToString() ?? "Instance") == "Instance";
            var group = (CmbGroup.SelectedItem as GroupPick)?.GroupId ?? GroupCatalog.DefaultGroupId;

            var path = TxtPath.Text ?? string.Empty;
            bool createIfMissing = ChkCreateIfMissing.IsChecked == true;

            try
            {
                using var t = new Transaction(_doc, "BA – Bind Shared Parameter");
                t.Start();

                SharedParameterBinder.BindSharedParameter(
                    _uiApp.Application,
                    _doc,
                    path,
                    def.Name,
                    def.Guid,
                    group,
                    isInstance,
                    selectedCats,
                    createIfMissing);

                t.Commit();

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BA – Bind Shared", ex.Message);
            }
        }
    }
}

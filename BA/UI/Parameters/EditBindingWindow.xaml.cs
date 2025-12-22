using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace BA.UI.Parameters
{
    public partial class EditBindingWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly Document _doc;
        private readonly ParameterRow _row;

        private List<CategoryPick> _cats = new();

        public string Header { get; set; }

        public EditBindingWindow(UIApplication uiApp, Document doc, ParameterRow row)
        {
            InitializeComponent();

            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _row = row ?? throw new ArgumentNullException(nameof(row));

            Header = $"{_row.Name}";
            DataContext = this;

            CmbInstanceType.ItemsSource = new[] { "Instance", "Type" };
            CmbInstanceType.SelectedItem = _row.InstanceOrType;

            CmbGroup.ItemsSource = GroupCatalog.CommonGroups;
            CmbGroup.DisplayMemberPath = "Label";
            CmbGroup.SelectedItem = GroupCatalog.CommonGroups.FirstOrDefault(g => g.GroupId.Equals(_row.GroupId))
                                    ?? GroupCatalog.CommonGroups.FirstOrDefault();

            LoadCategories();
        }

        private void LoadCategories()
        {
            var boundIds = new HashSet<ElementId>(_row.CategoryIds ?? new List<ElementId>());

            _cats = _doc.Settings.Categories
                .Cast<Category>()
                .Where(c => c != null && c.AllowsBoundParameters && c.CategoryType != CategoryType.Internal)
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c =>
                {
                    var pick = new CategoryPick(c);
                    pick.IsSelected = boundIds.Contains(c.Id);
                    return pick;
                })
                .ToList();

            CatsList.ItemsSource = _cats;
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            var selectedCats = _cats.Where(c => c.IsSelected).Select(c => c.Category).ToList();
            if (selectedCats.Count == 0)
            {
                TaskDialog.Show("BA – Edit Binding", "Select at least one category.");
                return;
            }

            bool isInstance = (CmbInstanceType.SelectedItem?.ToString() ?? "Instance") == "Instance";
            var groupId = (CmbGroup.SelectedItem as GroupPick)?.GroupId ?? GroupCatalog.DefaultGroupId;

            try
            {
                using var t = new Transaction(_doc, "BA – Edit Parameter Binding");
                t.Start();

                ParameterBindingEditor.Rebind(_uiApp.Application, _doc, _row.Definition, groupId, isInstance, selectedCats);

                t.Commit();

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BA – Edit Binding", ex.Message);
            }
        }
    }
}

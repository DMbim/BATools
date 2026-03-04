using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Parameters;
using BA.UI.ExternalEvents;
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
        private readonly RevitExternalInvoker _revit;
        private readonly List<ParameterRow> _rows;

        private List<CategoryPick> _cats = new();

        public string Header { get; set; }

        public EditBindingWindow(UIApplication uiApp, Document doc, IReadOnlyList<ParameterRow> rows, RevitExternalInvoker revit)
        {
            InitializeComponent();

            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _revit = revit ?? throw new ArgumentNullException(nameof(revit));
            _rows = rows?.ToList() ?? throw new ArgumentNullException(nameof(rows));

            if (_rows.Count == 0)
                throw new InvalidOperationException("No parameters provided.");

            Header = _rows.Count == 1 ? $"Edit: {_rows[0].Name}" : $"Edit bindings ({_rows.Count} parameters)";
            DataContext = this;

            ListSelectedParams.ItemsSource = _rows.Select(r => r.Name).ToList();
            TxtSelCount.Text = $"{_rows.Count}";

            // Instance/type default: if mixed -> Instance
            CmbInstanceType.ItemsSource = new[] { "Instance", "Type" };
            CmbInstanceType.SelectedItem = GetCommonInstanceOrType(_rows) ?? "Instance";

            // Group default: if mixed -> first common group else default
            CmbGroup.ItemsSource = GroupCatalog.CommonGroups;
            CmbGroup.DisplayMemberPath = "Label";
            var commonGroup = GetCommonGroupId(_rows);
            CmbGroup.SelectedItem = GroupCatalog.CommonGroups.FirstOrDefault(g => commonGroup != null && g.GroupId.Equals(commonGroup))
                                   ?? GroupCatalog.CommonGroups.FirstOrDefault();

            Loaded += (_, __) => LoadCategoriesViaRevit();
        }

        // Backwards compatible ctor for old callers
        public EditBindingWindow(UIApplication uiApp, Document doc, ParameterRow row, RevitExternalInvoker revit)
            : this(uiApp, doc, new List<ParameterRow> { row }, revit)
        {
        }

        private static Document ResolveDoc(UIApplication app, Document preferred)
        {
            if (preferred != null && preferred.IsValidObject) return preferred;
            return app.ActiveUIDocument?.Document;
        }

        private static string GetCommonInstanceOrType(IList<ParameterRow> rows)
        {
            var set = new HashSet<string>(rows.Select(r => (r.InstanceOrType ?? "").Trim()), StringComparer.OrdinalIgnoreCase);
            set.Remove("");
            return set.Count == 1 ? set.First() : null;
        }

        private static ForgeTypeId GetCommonGroupId(IList<ParameterRow> rows)
        {
            var set = rows.Select(r => r.GroupId).Distinct().ToList();
            return set.Count == 1 ? set[0] : null;
        }

        private HashSet<long> GetUnionBoundCategoryIds()
        {
            var union = new HashSet<long>();
            foreach (var r in _rows)
            {
                if (r.CategoryIdValues == null) continue;
                foreach (var id in r.CategoryIdValues) union.Add(id);
            }
            return union;
        }

        private HashSet<long> GetIntersectionBoundCategoryIds()
        {
            HashSet<long> intersection = null;

            foreach (var r in _rows)
            {
                var set = new HashSet<long>(r.CategoryIdValues ?? new List<long>());
                if (intersection == null)
                    intersection = set;
                else
                    intersection.IntersectWith(set);
            }

            return intersection ?? new HashSet<long>();
        }

        private void LoadCategoriesViaRevit()
        {
            // Pre-check categories that are common across selection (safe default)
            var preChecked = GetIntersectionBoundCategoryIds();

            _revit.Run(app =>
            {
                var d = ResolveDoc(app, _doc);
                if (d == null) throw new InvalidOperationException("No active document.");

                return d.Settings.Categories
                    .Cast<Category>()
                    .Where(c => c != null && c.AllowsBoundParameters && c.CategoryType != CategoryType.Internal)
                    .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(c =>
                    {
                        var pick = new CategoryPick(c);
                        pick.IsSelected = preChecked.Contains(pick.CategoryIdValue);
                        return pick;
                    })
                    .ToList();
            },
            onCompleted: picks =>
            {
                _cats = picks ?? new List<CategoryPick>();
                CatsList.ItemsSource = _cats;

                var union = GetUnionBoundCategoryIds();
                var inter = GetIntersectionBoundCategoryIds();

                TxtCatCount.Text = $"Loaded: {_cats.Count} | Common: {inter.Count} | Any: {union.Count}";
            },
            onError: ex => TaskDialog.Show("BA – Edit Binding", ex.ToString()));
        }

        private void BtnCatsAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var c in _cats) c.IsSelected = true;
            CatsList.Items.Refresh();
        }

        private void BtnCatsNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var c in _cats) c.IsSelected = false;
            CatsList.Items.Refresh();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            var selectedIds = _cats
                .Where(c => c.IsSelected && c.CategoryIdValue > 0)
                .Select(c => c.CategoryIdValue)
                .Distinct()
                .ToList();

            if (selectedIds.Count == 0)
            {
                TaskDialog.Show("BA – Edit Binding", "Select at least one category.");
                return;
            }

            bool isInstance = (CmbInstanceType.SelectedItem?.ToString() ?? "Instance") == "Instance";
            var groupId = (CmbGroup.SelectedItem as ParamGroupPick)?.GroupId ?? GroupCatalog.DefaultGroupId;
            bool merge = ChkMergeCategories.IsChecked == true;

            _revit.Run(app =>
            {
                var d = ResolveDoc(app, _doc);
                if (d == null) throw new InvalidOperationException("No active document.");

                using var t = new Transaction(d, $"BA – Edit Parameter Bindings ({_rows.Count})");
                t.Start();

                int updated = 0;
                foreach (var r in _rows)
                {
                    var ids = merge ? MergeExisting(d, r, selectedIds) : selectedIds;

                    ParameterBindingEditor.Rebind(
                        app.Application,
                        d,
                        r.Name,
                        r.Guid,
                        groupId,
                        isInstance,
                        ids);

                    updated++;
                }

                t.Commit();
                return updated;
            },
            onCompleted: _ => { DialogResult = true; Close(); },
            onError: ex => TaskDialog.Show("BA – Edit Binding", ex.ToString()));
        }

        private static IList<long> MergeExisting(Document doc, ParameterRow row, IList<long> selected)
        {
            var set = new HashSet<long>(selected);

            // If we already have category IDs from the collector, use them (fast)
            if (row.CategoryIdValues != null)
            {
                foreach (var id in row.CategoryIdValues) set.Add(id);
                return set.ToList();
            }

            // Fallback: try to read the binding live
            var def = ParameterBindingFinder.FindDefinition(doc, row.Name, row.Guid);
            if (def == null) return set.ToList();

            var binding = doc.ParameterBindings.get_Item(def) as ElementBinding;
            if (binding == null) return set.ToList();

            foreach (Category c in binding.Categories)
            {
                if (c?.Id == null) continue;
                set.Add(c.Id.Value);
            }

            return set.ToList();
        }
    }
}

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Parameters;
using BA.UI.ExternalEvents;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace BA.UI.Parameters
{
    public partial class CreateSharedParameterWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly Document _doc;
        private readonly RevitExternalInvoker _revit;
        public event Action? Applied;
        private List<SharedDefRow> _defs = new();
        private List<CategoryPick> _cats = new();
        private bool _isInitialized;
        // Families (Inject mode)
        private List<FamilyPick> _familiesAll = new();
        private List<FamilyPick> _familiesView = new();
        private object FamiliesPanel;

        public string SharedParamFilePath { get; set; } = string.Empty;

        public CreateSharedParameterWindow(UIApplication uiApp, Document doc, RevitExternalInvoker revit)
        {
            InitializeComponent();

            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _revit = revit ?? throw new ArgumentNullException(nameof(revit));

            // Instance/type options
            CmbInstanceType.ItemsSource = new[] { "Instance", "Type" };
            CmbInstanceType.SelectedIndex = 0;

            // Groups (ForgeTypeId based)
            CmbGroup.ItemsSource = GroupCatalog.CommonGroups;
            CmbGroup.DisplayMemberPath = "Label";
            CmbGroup.SelectedIndex = 0;

            LoadCategories();
            LoadFamilies();
            ReloadSharedDefs();
            _isInitialized = true;
            UpdatePreview(Array.Empty<string>(), 0);
            UpdateModeUi();
            UpdateFamiliesCount();
        }

        // ------------------------
        // Loading
        // ------------------------

        private static Document ResolveDoc(UIApplication app, Document fallback)
        {
            var active = app.ActiveUIDocument?.Document;
            if (active != null && active.IsValidObject) return active;
            if (fallback != null && fallback.IsValidObject) return fallback;
            throw new InvalidOperationException("No valid document available.");
        }
        private Dictionary<long, ElementId> BuildFamilySampleInstanceMap(Document doc, IEnumerable<Family> families)
{
            // familyId -> one instance id in project (if exists)
            var result = new Dictionary<long, ElementId>();

            // Collect all family instances once
            var insts = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>();

            foreach (var fi in insts)
            {
                var fam = fi?.Symbol?.Family;
                if (fam == null) continue;

                long famId = fam.Id.Value;
                if (result.ContainsKey(famId)) continue;

                result[famId] = fi.Id;
            }

            return result;
        }

        private bool InstanceHasSharedParam(FamilyInstance fi, Guid guid, string nameFallback)
        {
            // Prefer GUID-based match for shared params (best), fallback to name if needed.
            foreach (Parameter p in fi.Parameters)
            {
                if (p?.Definition == null) continue;

                // Shared parameter check: ExternalDefinition has GUID
                if (p.IsShared && p.Definition is ExternalDefinition ext)
                {
                    if (ext.GUID == guid) return true;
                }

                // Fallback by name
                if (!string.IsNullOrWhiteSpace(nameFallback) &&
                    string.Equals(p.Definition.Name, nameFallback, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void UpdateInjectPreview()
        {
            var selectedDefs = ListDefs.SelectedItems.Cast<object>().OfType<SharedDefRow>().ToList();
            var selectedFamilies = _familiesAll.Where(f => f.IsSelected).Select(f => f.Family).ToList();

            TxtSelectedDefs.Text = $"Selected: {selectedDefs.Count}";
            TxtSelectedFamilies.Text = $"Families selected: {selectedFamilies.Count}";

            if (selectedDefs.Count == 0 || selectedFamilies.Count == 0)
            {
                TxtAlreadyBound.Text = "Already exists in selected families: 0";
                ListBoundPreview.ItemsSource = Array.Empty<string>();
                return;
            }

            _revit.Run(app =>
            {
                var d = ResolveDoc(app, _doc);

                // Map each family to one representative instance in the project
                var famToInst = BuildFamilySampleInstanceMap(d, selectedFamilies);

                // For each def, count how many selected families already have it
                var lines = new List<string>();

                foreach (var defRow in selectedDefs)
                {
                    int have = 0;
                    int total = selectedFamilies.Count;

                    foreach (var fam in selectedFamilies)
                    {
                        long famId = fam.Id.Value;
                        if (!famToInst.TryGetValue(famId, out var instId))
                            continue; // no instances -> cannot verify quickly

                        var fi = d.GetElement(instId) as FamilyInstance;
                        if (fi == null) continue;

                        if (InstanceHasSharedParam(fi, defRow.Guid, defRow.Name))
                            have++;
                    }

                    lines.Add($"{defRow.Name}  —  exists in {have}/{total} selected families");
                }

                int totalHave = lines.Count(s => s.Contains("exists in ") && !s.EndsWith("0/")); // only display, not critical
                return lines;
            },
            onCompleted: lines =>
            {
                var list = lines ?? new List<string>();
                ListBoundPreview.ItemsSource = list;

                // summary line
                // (count totals by parsing is optional; keep it simple)
                TxtAlreadyBound.Text = "Already exists in selected families (per parameter):";
            },
            onError: ex => TaskDialog.Show("BA – Preview", ex.ToString()));
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

        private void LoadFamilies()
        {
            // Loadable families only (Family element). System families cannot receive FamilyParameters.
            _familiesAll = new FilteredElementCollector(_doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Select(f => new FamilyPick(f))
                .ToList();

            _familiesView = _familiesAll.ToList();
            FamiliesList.ItemsSource = _familiesView;
        }

        private void ReloadSharedDefs()
        {
            var path = string.IsNullOrWhiteSpace(SharedParamFilePath)
                ? (_uiApp.Application.SharedParametersFilename ?? string.Empty)
                : SharedParamFilePath;

            TxtPath.Text = path ?? string.Empty;

            _defs = SharedParameterFileReader.ReadAll(_uiApp.Application, path)
                .OrderBy(d => d.GroupName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ListDefs.ItemsSource = _defs;
        }

        // ------------------------
        // UI Events
        // ------------------------

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Shared Parameter File",
                Filter = "TXT files (*.txt)|*.txt|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dlg.ShowDialog() != true) return;

            SharedParamFilePath = dlg.FileName;
            _uiApp.Application.SharedParametersFilename = dlg.FileName;
            ReloadSharedDefs();

            ListDefs_SelectionChanged(ListDefs, null);
        }

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            var s = (TxtFilter.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
            {
                ListDefs.ItemsSource = _defs;
                return;
            }

            ListDefs.ItemsSource = _defs.Where(d =>
                d.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                || d.GroupName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                || d.Guid.ToString().IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
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

        private void BtnFamiliesAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var f in _familiesAll) f.IsSelected = true;
            FamiliesList.Items.Refresh();
            UpdateFamiliesCount();
        }

        private void BtnFamiliesNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var f in _familiesAll) f.IsSelected = false;
            FamiliesList.Items.Refresh();
            UpdateFamiliesCount();
            if (RbInjectIntoFamilies.IsChecked == true)
                UpdateInjectPreview();
        }

        private void TxtFamilyFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            var s = (TxtFamilyFilter.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
            {
                _familiesView = _familiesAll.ToList();
                FamiliesList.ItemsSource = _familiesView;
                return;
            }

            _familiesView = _familiesAll
                .Where(f => f.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            FamiliesList.ItemsSource = _familiesView;
            if (RbInjectIntoFamilies.IsChecked == true)
                UpdateInjectPreview();
        }




        private void Mode_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
                return;

            UpdateModeUi();
            ListDefs_SelectionChanged(ListDefs, null);
        }

        private void UpdateModeUi()
        {
            bool inject = RbInjectIntoFamilies.IsChecked == true;
            if (inject)
                UpdateInjectPreview();

            BorderFamilies.Visibility = inject ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            BorderCategories.Visibility = inject ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

            // Merge categories only meaningful for project binding mode
            ChkMergeCategories.IsEnabled = !inject;

            // Show/hide project binding preview
            TxtAlreadyBound.Visibility = inject ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            ListBoundPreview.Visibility = inject ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

            UpdateFamiliesCount();
        }


        private void ListDefs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = ListDefs.SelectedItems.Cast<object>().OfType<SharedDefRow>().ToList();
            TxtSelectedDefs.Text = $"Selected: {selected.Count}";
            UpdateFamiliesCount();

            // Inject mode: don't query project binding state
            if (RbInjectIntoFamilies.IsChecked == true)
            {
                UpdateInjectPreview();
                return;
            }

            if (selected.Count == 0)
            {
                UpdatePreview(Array.Empty<string>(), 0);
                return;
            }

            var guids = selected.Select(x => x.Guid).ToList();
            var names = selected.Select(x => x.Name).ToList();

            _revit.Run(app =>
            {
                var d = ResolveDoc(app, _doc);
                var bound = new List<string>();

                for (int i = 0; i < names.Count; i++)
                {
                    var def = ParameterBindingFinder.FindDefinition(d, names[i], guids[i].ToString());
                    if (def != null)
                        bound.Add(names[i]);
                }

                return bound;
            },
            onCompleted: boundNames =>
            {
                UpdatePreview(boundNames ?? new List<string>(), selected.Count);
            },
            onError: ex => TaskDialog.Show("BA – Bind Shared", ex.ToString()));
        }

        private void UpdatePreview(IList<string> boundNames, int selectedCount)
        {
            boundNames ??= Array.Empty<string>();

            TxtAlreadyBound.Text = $"Already bound in project: {boundNames.Count}";
            ListBoundPreview.ItemsSource = boundNames;

            if (selectedCount == 0)
                TxtSelectedDefs.Text = "Selected: 0";
        }

        private void UpdateFamiliesCount()
        {
            int famCount = _familiesAll?.Count(f => f.IsSelected) ?? 0;
            TxtSelectedFamilies.Text = $"Families selected: {famCount}";
        }

        // ------------------------
        // Bind / Inject
        // ------------------------

        private void BtnBind_Click(object sender, RoutedEventArgs e)
        {
            var selectedDefs = ListDefs.SelectedItems.Cast<object>().OfType<SharedDefRow>().ToList();
            if (selectedDefs.Count == 0)
            {
                TaskDialog.Show("BA", "Select shared parameter definitions (Ctrl/Shift).");
                return;
            }

            bool injectToFamilies = (RbInjectIntoFamilies.IsChecked == true);

            // capture UI state here (safe)
            bool isInstance = (CmbInstanceType.SelectedItem?.ToString() ?? "Instance") == "Instance";
            string spPath = TxtPath.Text ?? "";
            bool createIfMissing = ChkCreateIfMissing.IsChecked == true;
            bool mergeCategories = ChkMergeCategories.IsChecked == true;
            var groupId = (CmbGroup.SelectedItem as ParamGroupPick)?.GroupId ?? GroupCatalog.DefaultGroupId;

            var selectedCatIds = _cats.Where(c => c.IsSelected).Select(c => c.CategoryIdValue).Distinct().ToList();
            var selectedFamilies = _familiesAll.Where(f => f.IsSelected).Select(f => f.Family).ToList();

            _revit.Run(app =>
            {
                var d = ResolveDoc(app, _doc);
                if (d == null) throw new InvalidOperationException("No active document.");

                if (injectToFamilies)
                {
                    if (selectedFamilies.Count == 0)
                        throw new InvalidOperationException("Select at least one family.");

                    int changedTotal = 0, skippedTotal = 0;

                    foreach (var defRow in selectedDefs)
                    {
                        var extDef =
                            SharedParameterFileReader.FindExternalDefinitionByGuid(app.Application, spPath, defRow.Guid)
                            ?? SharedParameterFileReader.FindExternalDefinitionByName(app.Application, spPath, defRow.Name);

                        if (extDef == null)
                            throw new InvalidOperationException($"Definition not found in SP file: {defRow.Name} ({defRow.Guid})");

                        var (changed, skipped) = FamilySharedParameterInjector.InjectIntoFamilies(
                            app, d, selectedFamilies, extDef, groupId, isInstance);

                        changedTotal += changed;
                        skippedTotal += skipped;
                    }

                    return $"Inject done. Changed: {changedTotal}, Skipped: {skippedTotal}";
                }
                else
                {
                    if (selectedCatIds.Count == 0)
                        throw new InvalidOperationException("Select at least one category.");

                    using (var t = new Transaction(d, "BA – Bind Shared Parameters"))
                    {
                        t.Start();

                        foreach (var defRow in selectedDefs)
                        {
                            var finalCatIds = mergeCategories
                                ? MergeWithExisting(d, defRow, selectedCatIds)
                                : selectedCatIds;

                            var categories = finalCatIds
                                .Select(idVal => Category.GetCategory(d, new ElementId(idVal)))
                                .Where(c => c != null)
                                .ToList();

                            SharedParameterBinder.BindSharedParameterByGuid(
                                app.Application,
                                d,
                                spPath,
                                defRow.Guid,
                                defRow.Name,
                                groupId,
                                isInstance,
                                categories,
                                createIfMissing);
                        }

                        t.Commit();
                    }

                    return "Bind done.";
                }
            },
            onCompleted: msg =>
            {
                // back on UI thread
                if (!string.IsNullOrWhiteSpace(msg))
                    TaskDialog.Show("BA", msg);

                Applied?.Invoke();
                Close();
            },
            onError: ex => TaskDialog.Show("BA – Bind", ex.ToString()));
        }

        private static IList<long> MergeWithExisting(Document doc, SharedDefRow defRow, IList<long> selectedCatIds)
        {
            var set = new HashSet<long>(selectedCatIds);

            var def = ParameterBindingFinder.FindDefinition(doc, defRow.Name, defRow.Guid.ToString());
            if (def == null) return set.ToList();

            var map = doc.ParameterBindings;
            var binding = map.get_Item(def) as ElementBinding;
            if (binding == null) return set.ToList();

            foreach (Category c in binding.Categories)
            {
                if (c?.Id == null) continue;
                set.Add(c.Id.Value);
            }

            return set.ToList();
        }

        // ------------------------
        // Family Injection Helper
        // ------------------------

        internal static class FamilySharedParameterInjector
        {
            public static (int Changed, int Skipped) InjectIntoFamilies(
                UIApplication uiapp,
                Document projectDoc,
                IList<Family> families,
                ExternalDefinition extDef,
                ForgeTypeId groupTypeId,
                bool isInstance)
            {
                if (uiapp == null) throw new ArgumentNullException(nameof(uiapp));
                if (projectDoc == null) throw new ArgumentNullException(nameof(projectDoc));
                if (families == null || families.Count == 0) throw new ArgumentException("No families provided.", nameof(families));
                if (extDef == null) throw new ArgumentNullException(nameof(extDef));

                int changed = 0, skipped = 0;

                foreach (var fam in families.Distinct(new FamilyIdComparer()))
                {
                    if (fam == null) { skipped++; continue; }

                    Document famDoc = null;
                    try
                    {
                        famDoc = projectDoc.EditFamily(fam);
                        if (famDoc == null || !famDoc.IsFamilyDocument) { skipped++; continue; }

                        var fm = famDoc.FamilyManager;
                        if (fm == null) { skipped++; continue; }

                        if (FamilyHasSharedParameter(fm, extDef)) { skipped++; continue; }

                        using (var tf = new Transaction(famDoc, $"BA – Add {extDef.Name}"))
                        {
                            tf.Start();
                            AddFamilyParameterCompat(fm, extDef, groupTypeId, isInstance);
                            tf.Commit();
                        }

                        using (var tp = new Transaction(projectDoc, $"BA – Reload family {fam.Name}"))
                        {
                            tp.Start();
                            famDoc.LoadFamily(projectDoc, new AlwaysOverwriteFamilyLoadOptions());
                            tp.Commit();
                        }

                        changed++;
                    }
                    catch
                    {
                        skipped++;
                    }
                    finally
                    {
                        if (famDoc != null)
                        {
                            try { famDoc.Close(false); } catch { }
                        }
                    }
                }

                return (changed, skipped);
            }

            private static bool FamilyHasSharedParameter(FamilyManager fm, ExternalDefinition extDef)
            {
                foreach (FamilyParameter p in fm.Parameters)
                {
                    if (p.IsShared)
                    {
                        var guid = TryGetFamilyParamGuid(p);
                        if (guid != Guid.Empty && guid == extDef.GUID)
                            return true;
                    }

                    var defName = p.Definition?.Name ?? "";
                    if (string.Equals(defName, extDef.Name, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }

            private static Guid TryGetFamilyParamGuid(FamilyParameter p)
            {
                try
                {
                    var prop = p.GetType().GetProperty("GUID", BindingFlags.Public | BindingFlags.Instance);
                    if (prop == null) return Guid.Empty;

                    var val = prop.GetValue(p);
                    if (val is Guid g) return g;

                    if (val != null && val.GetType().FullName == "System.Nullable`1[System.Guid]")
                    {
                        var hasValue = (bool)val.GetType().GetProperty("HasValue")!.GetValue(val);
                        if (!hasValue) return Guid.Empty;
                        return (Guid)val.GetType().GetProperty("Value")!.GetValue(val);
                    }

                    return Guid.Empty;
                }
                catch { return Guid.Empty; }
            }

            private static void AddFamilyParameterCompat(FamilyManager fm, ExternalDefinition extDef, ForgeTypeId groupTypeId, bool isInstance)
            {
                // Prefer AddParameter(ExternalDefinition, ForgeTypeId, bool)
                var miForge = fm.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "AddParameter") return false;
                        var ps = m.GetParameters();
                        return ps.Length == 3
                            && typeof(ExternalDefinition).IsAssignableFrom(ps[0].ParameterType)
                            && ps[1].ParameterType.FullName == "Autodesk.Revit.DB.ForgeTypeId"
                            && ps[2].ParameterType == typeof(bool);
                    });

                if (miForge != null)
                {
                    miForge.Invoke(fm, new object[] { extDef, groupTypeId, isInstance });
                    return;
                }

                // Fallback older API: AddParameter(ExternalDefinition, BuiltInParameterGroup, bool) if it exists
                var bipgType = fm.GetType().Assembly.GetType("Autodesk.Revit.DB.BuiltInParameterGroup");
                if (bipgType != null)
                {
                    var miOld = fm.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m =>
                        {
                            if (m.Name != "AddParameter") return false;
                            var ps = m.GetParameters();
                            return ps.Length == 3
                                && typeof(ExternalDefinition).IsAssignableFrom(ps[0].ParameterType)
                                && ps[1].ParameterType == bipgType
                                && ps[2].ParameterType == typeof(bool);
                        });

                    if (miOld != null)
                    {
                        object pgData = Enum.Parse(bipgType, "PG_DATA");
                        miOld.Invoke(fm, new object[] { extDef, pgData, isInstance });
                        return;
                    }
                }

                throw new InvalidOperationException("No compatible FamilyManager.AddParameter overload found.");
            }

            private sealed class AlwaysOverwriteFamilyLoadOptions : IFamilyLoadOptions
            {
                public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
                {
                    overwriteParameterValues = false;
                    return true;
                }

                public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
                {
                    source = FamilySource.Family;
                    overwriteParameterValues = false;
                    return true;
                }
            }




            private sealed class FamilyIdComparer : IEqualityComparer<Family>
            {
                public bool Equals(Family x, Family y)
                {
                    if (ReferenceEquals(x, y)) return true;
                    if (x is null || y is null) return false;
                    return x.Id.Value == y.Id.Value;
                }

                public int GetHashCode(Family obj) => (int)(obj?.Id.Value ?? 0);
            }
        }
    }
}

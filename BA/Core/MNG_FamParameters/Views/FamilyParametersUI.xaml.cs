// BA/UI/FamilyParametersUI.xaml.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Newtonsoft.Json; // <- ADD this using at top of file if not already present
using System.Windows.Data;
using System.Windows.Input;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using TextBox = System.Windows.Controls.TextBox;

namespace BA.UI
{
    public partial class FamilyParametersUI : Window
    {
        private readonly UIApplication _uiApp;
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;

        private ParameterPreview _lastSelToggleAnchor;

        public ObservableCollection<ParameterPreview> Parameters { get; } = new();

        // ---- Event handlers (held as fields to prevent GC) ----
        private readonly HarmonizerEventHandler _handler;
        private readonly ExternalEvent _extEvent;

        private readonly DuplicateParamEventHandler _duplicateHandler;
        private readonly ExternalEvent _duplicateEvent;

        private readonly AddParamEventHandler _addParamHandler;
        private readonly ExternalEvent _addParamEvent;
        private readonly ObservableCollection<FavoriteItem> _favorites = new(); // <- ADD
        // ===================== Constructors =====================

        public FamilyParametersUI(ExternalCommandData commandData)
        {
            InitializeComponent();

            _uiApp = commandData?.Application
                     ?? throw new ArgumentNullException(nameof(commandData));
            _uiDoc = _uiApp.ActiveUIDocument
                     ?? throw new InvalidOperationException("ActiveUIDocument is null.");
            _doc = _uiDoc.Document
                     ?? throw new InvalidOperationException("Active document is null.");

            DataContext = this;
            TxtSharedParamPath.Text = _uiApp.Application.SharedParametersFilename ?? string.Empty;

            LoadParameters();
            InitializeEventHandlers(out _handler, out _extEvent,
                                    out _duplicateHandler, out _duplicateEvent,
                                    out _addParamHandler, out _addParamEvent);
            InitializeFilterAndFavorites(); // <- ADD this line to both constructors
        }

        public FamilyParametersUI(UIDocument uiDoc)
        {
            InitializeComponent();

            _uiDoc = uiDoc ?? throw new ArgumentNullException(nameof(uiDoc));
            _uiApp = _uiDoc.Application
                     ?? throw new InvalidOperationException("UIApplication is null.");
            _doc = _uiDoc.Document
                     ?? throw new InvalidOperationException("Active document is null.");

            DataContext = this;
            TxtSharedParamPath.Text = _uiApp.Application.SharedParametersFilename ?? string.Empty;

            LoadParameters();
            InitializeEventHandlers(out _handler, out _extEvent,
                                    out _duplicateHandler, out _duplicateEvent,
                                    out _addParamHandler, out _addParamEvent);
            InitializeFilterAndFavorites(); // <- ADD this line to both constructors
        }

        // ===================== Event handler wiring =====================

        private void InitializeEventHandlers(
            out HarmonizerEventHandler handler, out ExternalEvent extEvent,
            out DuplicateParamEventHandler dupHandler, out ExternalEvent dupEvent,
            out AddParamEventHandler addHandler, out ExternalEvent addEvent)
        {
            handler = new HarmonizerEventHandler
            {
                UiApplication = _uiApp,
                UiDocument = _uiDoc,
                Document = _doc
            };
            extEvent = ExternalEvent.Create(handler);

            dupHandler = new DuplicateParamEventHandler { Document = _doc };
            dupEvent = ExternalEvent.Create(dupHandler);

            addHandler = new AddParamEventHandler { Document = _doc };
            addEvent = ExternalEvent.Create(addHandler);
        }

        // ===================== Parameter loading =====================

        private void LoadParameters()
        {
            Parameters.Clear();

            if (_doc == null || !_doc.IsFamilyDocument)
            {
                MessageBox.Show("Active document is not a family. Open a family first.");
                return;
            }

            var fm = _doc.FamilyManager;

            foreach (FamilyParameter fp in fm.GetParameters())
            {
                // Spec label
                string friendly = "<Unknown>";
                try
                {
                    var specId = fp.Definition.GetDataType();
                    friendly = LabelUtils.GetLabelForSpec(specId);
                }
                catch { }

                // Built-in flag
                bool isBuiltIn = fp.Definition is InternalDefinition intDef
                                 && intDef.BuiltInParameter != BuiltInParameter.INVALID;

                // Group
                string groupTypeId = "";
                string groupName = "";
                try
                {
                    var gid = fp.Definition.GetGroupTypeId();
                    if (gid != null)
                    {
                        groupTypeId = gid.TypeId;
                        groupName = gid.TypeId;
                    }
                }
                catch { }

                // Formula and CanAssignFormula
                string formula = "";
                bool canAssign = false;
                try
                {
                    canAssign = fp.CanAssignFormula;
                    if (canAssign)
                        formula = fp.Formula ?? "";
                }
                catch { }

                string scope = fp.IsInstance ? "Instance" : "Type";

                Parameters.Add(new ParameterPreview
                {
                    Name = fp.Definition.Name,
                    Scope = scope,
                    OriginalScope = scope,          // locked at load; drives ScopeChangeNeeded
                    IsShared = fp.IsShared,
                    IsBuiltIn = isBuiltIn,
                    Spec = friendly,
                    GroupTypeId = groupTypeId,
                    GroupName = groupName,
                    Formula = formula,
                    OriginalFormula = formula,        // locked at load; drives FormulaChanged
                    CanAssignFormula = canAssign,
                    TargetName = "",
                    MatchedShared = "",
                    DeleteRequested = false
                });
            }
        }

        // ===================== Selection helpers =====================

        private ParameterPreview[] GetSelectedRows()
            => DgParameters.SelectedItems
                .Cast<object>()
                .OfType<ParameterPreview>()
                .ToArray();

        private void SelCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb) return;
            if (cb.DataContext is not ParameterPreview clickedRow) return;

            if (!DgParameters.SelectedItems.Contains(clickedRow))
                DgParameters.SelectedItem = clickedRow;

            bool desired = cb.IsChecked == true;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                var selected = DgParameters.SelectedItems.Cast<object>()
                    .OfType<ParameterPreview>().ToList();

                if (selected.Count > 0)
                {
                    foreach (var r in selected) r.IsSelected = desired;
                    _lastSelToggleAnchor = clickedRow;
                    return;
                }
            }

            clickedRow.IsSelected = desired;
            _lastSelToggleAnchor = clickedRow;
        }

        // ===================== Bulk scope buttons =====================

        private void BtnSetInstanceSelected_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedRows();
            if (rows.Length == 0) { MessageBox.Show("Select one or more rows first."); return; }
            foreach (var r in rows)
                r.Scope = "Instance";
        }

        private void BtnSetTypeSelected_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedRows();
            if (rows.Length == 0) { MessageBox.Show("Select one or more rows first."); return; }
            foreach (var r in rows)
                r.Scope = "Type";
        }

        // ===================== Preview match =====================

        private void BtnPreview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var app = _uiApp.Application;
                var spOverride = TxtSharedParamPath?.Text ?? string.Empty;

                SharedParamUtils.LoadSharedParameterFile(
                    app, string.IsNullOrWhiteSpace(spOverride) ? null : spOverride);

                var lookup = SharedParamUtils.BuildExternalDefinitionLookup();

                foreach (var row in Parameters)
                {
                    var ext = SharedParamUtils.FindBestSharedDefinition(
                        row.Name, lookup, out string matched, minScore: 0.66);

                    if (ext != null && !row.IsBuiltIn && !row.IsShared)
                    {
                        row.MatchedShared = matched;
                        row.TargetName = matched;    // drives EffectiveAction = "Replace"
                        row.MatchScore = NameMatcher.ScoreTokens(
                            NameMatcher.Tokens(row.Name), NameMatcher.Tokens(matched));
                    }
                    else
                    {
                        row.MatchedShared = "";
                        row.TargetName = "";
                        row.MatchScore = 0;
                    }
                }

                MessageBox.Show("Preview updated.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Preview failed: " + ex.Message);
            }
        }

        // ===================== Custom replace =====================

        private void BtnCustom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var app = _uiApp.Application;
                var spOverride = TxtSharedParamPath?.Text ?? string.Empty;

                SharedParamUtils.LoadSharedParameterFile(
                    app, string.IsNullOrWhiteSpace(spOverride) ? null : spOverride);

                var lookup = SharedParamUtils.BuildExternalDefinitionLookup();

                var dlg = new CustomReplaceDialog(Parameters.ToList(), lookup) { Owner = this };
                if (dlg.ShowDialog() == true && dlg.Mapping.HasValue)
                {
                    var (familyParam, sharedParam) = dlg.Mapping.Value;
                    var row = Parameters.FirstOrDefault(p =>
                        p.Name.Equals(familyParam, StringComparison.OrdinalIgnoreCase));

                    if (row != null)
                    {
                        row.TargetName = sharedParam;
                        row.MatchedShared = sharedParam;
                        row.MatchScore = 1.00;
                        row.DeleteRequested = false;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Custom Replace failed: " + ex.Message);
            }
        }

        // ===================== Add shared params (existing dialog) =====================

        private void BtnAddShared_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null || !_doc.IsFamilyDocument)
            {
                MessageBox.Show("Active document is not a family.");
                return;
            }
            var dlg = new AddSharedParamsDialog(_uiApp, _doc) { Owner = this };
            dlg.ShowDialog();
        }

        // ===================== Add parameter (new dialog - shared or non-shared) =====================

        private void BtnAddParam_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null || !_doc.IsFamilyDocument)
            {
                MessageBox.Show("Active document is not a family.");
                return;
            }

            // Build shared param lookup; silently fall back to empty if no file configured
            var lookup = new Dictionary<string, Definition>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var spOverride = TxtSharedParamPath?.Text ?? string.Empty;
                SharedParamUtils.LoadSharedParameterFile(
                    _uiApp.Application,
                    string.IsNullOrWhiteSpace(spOverride) ? null : spOverride);
                lookup = SharedParamUtils.BuildExternalDefinitionLookup();
            }
            catch { }

            var dlg = new AddParameterDialog(lookup) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            _addParamHandler.ParamName = dlg.ResultParamName;
            _addParamHandler.SpecTypeId = dlg.ResultSpecTypeId;
            _addParamHandler.TargetGroupTypeId = dlg.ResultGroupTypeId;
            _addParamHandler.IsInstance = dlg.ResultIsInstance;
            _addParamHandler.IsShared = dlg.ResultIsShared;
            _addParamHandler.SharedDefinition = dlg.ResultSharedDefinition;
            _addParamHandler.Document = _doc;

            _addParamHandler.OnComplete = newName =>
            {
                if (newName != null)
                {
                    LoadParameters();
                    var newRow = Parameters.FirstOrDefault(p =>
                        p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));

                    if (newRow != null)
                    {
                        DgParameters.SelectedItem = newRow;
                        DgParameters.ScrollIntoView(newRow);
                    }
                }
                else
                {
                    MessageBox.Show(
                        $"Failed to add parameter.\n\n{_addParamHandler.Log}",
                        "Add Parameter", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            _addParamEvent.Raise();
        }

        // ===================== Duplicate =====================

        private void BtnDuplicate_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedRows();
            if (selected.Length == 0)
            {
                MessageBox.Show("Select one or more parameters to duplicate.");
                return;
            }

            _duplicateHandler.SourceParams.Clear();
            _duplicateHandler.SourceParams.AddRange(selected);
            _duplicateHandler.Document = _doc;

            _duplicateHandler.OnComplete = newNames =>
            {
                LoadParameters();

                var succeeded = newNames.Where(n => n != null).ToList();
                var failed = newNames.Count(n => n == null);

                var sb = new StringBuilder();
                sb.AppendLine($"Duplicated {succeeded.Count} of {selected.Length} parameter(s).");
                foreach (var n in succeeded) sb.AppendLine($"  \u2192 {n}");
                if (failed > 0)
                    sb.AppendLine($"\n{failed} parameter(s) failed. Check the log.");

                MessageBox.Show(sb.ToString(), "Duplicate Parameters");
            };

            _duplicateEvent.Raise();
        }

        // ===================== Toggle delete =====================

        private void BtnToggleDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedRows();
            if (rows.Length == 0) { MessageBox.Show("Select one or more rows first."); return; }
            bool anyNotDelete = rows.Any(r => !r.DeleteRequested);
            foreach (var r in rows) r.DeleteRequested = anyNotDelete;
        }

        // ===================== Run apply =====================

        private void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null || !_doc.IsFamilyDocument)
            {
                MessageBox.Show("Active document is not a family.");
                return;
            }

            _handler.Decisions.Clear();
            _handler.Decisions.AddRange(Parameters);
            _handler.Log.Clear();
            _handler.Log.AppendLine("Starting harmonization...");
            _handler.SharedParamOverridePath = TxtSharedParamPath?.Text ?? string.Empty;

            _extEvent.Raise();
            DialogResult = true;
        }

        // ===================== Prefix apply =====================

        private void BtnApplyPrefix_Click(object sender, RoutedEventArgs e)
        {
            var prefix = (TxtPrefixNew.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(prefix))
            {
                MessageBox.Show("Type a prefix first.");
                return;
            }

            var targets = DgParameters.SelectedItems.Cast<object>()
                .OfType<ParameterPreview>().ToList();

            if (targets.Count == 0)
            {
                MessageBox.Show("Select one or more rows first.");
                return;
            }

            foreach (var row in targets)
            {
                var baseName = string.IsNullOrWhiteSpace(row.TargetName)
                    ? row.Name : row.TargetName;

                if (!baseName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    row.TargetName = prefix + baseName;
            }
        }

        // ===================== Export CSV =====================

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "CSV Files (*.csv)|*.csv" };
            if (dialog.ShowDialog() == true)
            {
                using var sw = new System.IO.StreamWriter(dialog.FileName, false, Encoding.UTF8);
                sw.WriteLine(
                    "Name,Scope,OriginalScope,ScopeChanged,IsShared,Spec,Decision," +
                    "TargetName,MatchedShared,Score,DeleteRequested,Formula,FormulaChanged");

                foreach (var p in Parameters)
                    sw.WriteLine(
                        $"{p.Name},{p.Scope},{p.OriginalScope},{p.ScopeChangeNeeded}," +
                        $"{p.IsShared},{p.Spec},{p.EffectiveAction},{p.TargetName}," +
                        $"{p.MatchedShared},{p.MatchScore},{p.DeleteRequested}," +
                        $"{p.Formula},{p.FormulaChanged}");

                MessageBox.Show("Exported successfully.");
            }
        }

        // ===================== Metric units =====================

        private void BtnMetric_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) { MessageBox.Show("No active document."); return; }
            try
            {
                using var t = new Transaction(_doc, "Set metric units");
                t.Start();
                _doc.SetUnits(new Units(UnitSystem.Metric));
                t.Commit();
                MessageBox.Show("Family units set to metric.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to set metric units: " + ex.Message);
            }
        }

        // ===================== Browse SP file =====================

        private void BtnBrowseSP_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Shared Parameter File",
                Filter = "TXT files (*.txt)|*.txt|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true && TxtSharedParamPath != null)
                TxtSharedParamPath.Text = dlg.FileName;
        }
        // ===================== Filter + favorites initialization =====================

        private void InitializeFilterAndFavorites()
        {
            // Wire DataGrid ItemsSource if not already set from XAML
            if (DgParameters.ItemsSource == null)
                DgParameters.ItemsSource = Parameters;

            // Wire single-click edit events (not set in XAML)
            DgParameters.PreviewMouseLeftButtonDown += DgParameters_PreviewMouseLeftButtonDown;
            DgParameters.PreviewKeyDown += DgParameters_PreviewKeyDown;

            // Wire collection view filter
            var view = CollectionViewSource.GetDefaultView(Parameters);
            view.Filter = FilterRow;
            UpdateVisibleCount(view);
            // existing last line:
            FavoritesList.ItemsSource = _favorites;

            // ADD immediately after it:
            LoadFavorites(); // <- ADD: populate from disk on open
        }

        // ===================== Filters =====================

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            var view = CollectionViewSource.GetDefaultView(Parameters);
            view.Refresh();
            UpdateVisibleCount(view);
        }

        private bool FilterRow(object obj)
        {
            if (obj is not ParameterPreview row) return false;

            // Built-in filter
            if (RbBuiltInOnly?.IsChecked == true && !row.IsBuiltIn) return false;
            if (RbBuiltInNone?.IsChecked == true && row.IsBuiltIn) return false;

            // Shared filter
            if (RbSharedOnly?.IsChecked == true && !row.IsShared) return false;
            if (RbSharedNone?.IsChecked == true && row.IsShared) return false;

            // Scope filter — checks current desired Scope, not OriginalScope
            if (RbScopeType?.IsChecked == true &&
                !string.Equals(row.Scope, "Type", StringComparison.OrdinalIgnoreCase)) return false;
            if (RbScopeInstance?.IsChecked == true &&
                !string.Equals(row.Scope, "Instance", StringComparison.OrdinalIgnoreCase)) return false;

            return true;
        }

        private void UpdateVisibleCount(ICollectionView view)
        {
            if (TxtVisibleCount == null) return;
            int visible = view.Cast<object>().Count();
            TxtVisibleCount.Text = $"Showing {visible} of {Parameters.Count}";
        }

        // ===================== Favorites =====================

        private void BtnSaveFavorite_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedRows();
            if (rows.Length == 0)
            {
                MessageBox.Show("Select one or more rows first.");
                return;
            }

            int added = 0;
            int skipped = 0;

            foreach (var row in rows)
            {
                if (_favorites.Any(f =>
                        f.ParamName.Equals(row.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    skipped++;
                    continue;
                }

                var action = row.EffectiveAction;
                string subText = action != "Keep" && !string.IsNullOrWhiteSpace(row.TargetName)
                    ? $"{action} \u2192 {row.TargetName} | {row.Spec}"
                    : $"{row.Scope} | {action} | {row.Spec}";

                _favorites.Add(new FavoriteItem
                {
                    ParamName = row.Name,
                    DisplayName = row.Name,
                    SubText = subText,
                    TargetName = row.TargetName ?? "",
                    MatchedShared = row.MatchedShared ?? "",
                });
                added++;
            }

            var msg = $"Saved {added} favorite(s).";
            if (skipped > 0) msg += $" {skipped} already in list — skipped.";
            MessageBox.Show(msg);
            SaveFavorites(); // <- ADD: persist after every save
        }

        private void BtnRemoveFavorite_Click(object sender, RoutedEventArgs e)
        {
            var toRemove = _favorites.Where(f => f.IsChecked).ToList();
            if (toRemove.Count == 0)
            {
                MessageBox.Show("Check one or more favorites to remove.");
                return;
            }
            foreach (var f in toRemove) _favorites.Remove(f);
            MessageBox.Show($"Removed {toRemove.Count} favorite(s).");
            SaveFavorites(); // <- ADD: persist after every remove
        }

        private void BtnLoadFavorite_Click(object sender, RoutedEventArgs e)
        {
            var checkedFavs = _favorites.Where(f => f.IsChecked).ToList();
            if (checkedFavs.Count == 0)
            {
                MessageBox.Show("Check one or more favorites to load.");
                return;
            }

            int applied = 0;
            int notFound = 0;

            foreach (var fav in checkedFavs)
            {
                var match = Parameters.FirstOrDefault(p =>
                    p.Name.Equals(fav.ParamName, StringComparison.OrdinalIgnoreCase));

                if (match == null) { notFound++; continue; }

                match.TargetName = fav.TargetName;
                match.MatchedShared = fav.MatchedShared;
                applied++;
            }

            var msg = $"Applied {applied} favorite decision(s).";
            if (notFound > 0)
                msg += $"\n{notFound} parameter name(s) not found in this family.";

            MessageBox.Show(msg);
        }
        // ===================== Favorites persistence =====================

        private static readonly string FavoritesPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BA", "FamilyHarmonizer", "FavoriteParams.json");

        private void SaveFavorites()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(FavoritesPath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(
                    _favorites, Newtonsoft.Json.Formatting.Indented);

                System.IO.File.WriteAllText(FavoritesPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // Non-fatal: favorites remain in memory for this session
                System.Diagnostics.Debug.WriteLine($"SaveFavorites failed: {ex.Message}");
            }
        }

        private void LoadFavorites()
        {
            try
            {
                if (!System.IO.File.Exists(FavoritesPath)) return;

                var json = System.IO.File.ReadAllText(FavoritesPath, Encoding.UTF8);
                var items = Newtonsoft.Json.JsonConvert.DeserializeObject<List<FavoriteItem>>(json);

                if (items == null) return;

                _favorites.Clear();
                foreach (var item in items)
                    _favorites.Add(item); // IsChecked defaults to false (not persisted)
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadFavorites failed: {ex.Message}");
            }
        }
        // ===================== DataGrid editing helpers =====================

        private void DgParameters_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject dep = (DependencyObject)e.OriginalSource;
            while (dep != null && dep is not DataGridCell && dep is not DataGridColumnHeader)
                dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);

            if (dep is not DataGridCell cell) return;
            if (cell.IsEditing || cell.IsReadOnly) return;

            if (e.OriginalSource is FrameworkElement fe)
            {
                if (fe is CheckBox) return;
                if (FindAncestor<ComboBox>(fe) != null) return;
                if (FindAncestor<CheckBox>(fe) != null) return;
                if (FindAncestor<Button>(fe) != null) return;
            }

            if (!cell.IsSelected) { cell.Focus(); cell.IsSelected = true; }
            DgParameters.BeginEdit();
            e.Handled = true;
        }

        private void DgParameters_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2 || e.Key == Key.Enter || e.Key == Key.Delete) return;

            if ((e.Key >= Key.A && e.Key <= Key.Z) ||
                (e.Key >= Key.D0 && e.Key <= Key.D9) ||
                (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9) ||
                e.Key == Key.Space)
            {
                if (!DgParameters.IsReadOnly && DgParameters.CurrentCell.Column != null)
                    DgParameters.BeginEdit();
            }
        }

        private void DgParameters_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete) return;
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;

            var rows = GetSelectedRows();
            if (rows.Length == 0) return;

            bool undo = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            foreach (var r in rows) r.DeleteRequested = !undo;
            e.Handled = true;
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T hit) return hit;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
    public sealed class FavoriteItem : INotifyPropertyChanged
    {
        private bool _isChecked;

        public string ParamName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string SubText { get; set; } = "";
        public string TargetName { get; set; } = "";
        public string MatchedShared { get; set; } = "";

        [JsonIgnore]   // <- ADD: UI toggle state must not be persisted
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
    internal static class ListExtensions
    {
        public static void AddRange<T>(this IList<T> list, IEnumerable<T> items)
        {
            foreach (var i in items) list.Add(i);
        }
    }
}
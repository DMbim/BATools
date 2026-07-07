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
    /// <summary>
    /// ViewModel for a single entry in the favorites panel ItemsControl.
    /// </summary>
    public sealed class FavoriteVm : INotifyPropertyChanged
    {
        public Guid Guid { get; set; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked == value) return; _isChecked = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Current name resolved from the SP file, or LastKnownName + " (stale)" if not resolvable.
        /// </summary>
        public string DisplayName { get; set; } = "";

        /// <summary>
        /// Secondary info line: spec, instance/type, and stale marker.
        /// </summary>
        public string SubText { get; set; } = "";

        public bool IsStale { get; set; }

        public FamilyHarmonizerFavorite Source { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class FamilyParametersUI : Window
    {
        private readonly UIApplication _uiApp;
        private ParameterPreview? _lastSelToggleAnchor;
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;

        public ObservableCollection<ParameterPreview> Parameters { get; } = new();

        private readonly CollectionViewSource _parametersView = new();

        private readonly HarmonizerEventHandler _handler;
        private readonly ExternalEvent _extEvent;

        public FamilyParametersUI(ExternalCommandData commandData)
        {

            InitializeComponent();

            _uiApp = commandData?.Application ?? throw new ArgumentNullException(nameof(commandData));
            _uiDoc = _uiApp.ActiveUIDocument ?? throw new InvalidOperationException("ActiveUIDocument is null.");
            _doc = _uiDoc.Document ?? throw new InvalidOperationException("Active document is null.");

            DataContext = this;

            if (TxtSharedParamPath != null)
                TxtSharedParamPath.Text = _uiApp.Application.SharedParametersFilename ?? string.Empty;

            InitParametersView();
            LoadParameters();
            RefreshFavoritesList();

            _handler = new HarmonizerEventHandler
            {
                UiApplication = _uiApp,
                UiDocument = _uiDoc,
                Document = _doc
            };

            _extEvent = ExternalEvent.Create(_handler);
        }

        public FamilyParametersUI(UIDocument uiDoc)
        {

            InitializeComponent();

            _uiDoc = uiDoc ?? throw new ArgumentNullException(nameof(uiDoc));
            _uiApp = _uiDoc.Application ?? throw new InvalidOperationException("UIApplication is null.");
            _doc = _uiDoc.Document ?? throw new InvalidOperationException("Active document is null.");

            DataContext = this;

            if (TxtSharedParamPath != null)
                TxtSharedParamPath.Text = _uiApp.Application.SharedParametersFilename ?? string.Empty;

            InitParametersView();
            LoadParameters();
            RefreshFavoritesList();

            _handler = new HarmonizerEventHandler
            {
                UiApplication = _uiApp,
                UiDocument = _uiDoc,
                Document = _doc
            };

            _extEvent = ExternalEvent.Create(_handler);
        }

        // ============================
        // Filtered view setup
        // ============================

        private void InitParametersView()
        {
            _parametersView.Source = Parameters;
            _parametersView.Filter += ParametersView_Filter;
            DgParameters.ItemsSource = _parametersView.View;
        }

        private void ParametersView_Filter(object sender, FilterEventArgs e)
        {
            if (e.Item is not ParameterPreview row)
            {
                e.Accepted = false;
                return;
            }

            bool accept = true;

            // Built-in group
            if (RbBuiltInOnly?.IsChecked == true) accept &= row.IsBuiltIn;
            else if (RbBuiltInNone?.IsChecked == true) accept &= !row.IsBuiltIn;

            // Shared group
            if (RbSharedOnly?.IsChecked == true) accept &= row.IsShared;
            else if (RbSharedNone?.IsChecked == true) accept &= !row.IsShared;

            // Scope group
            if (RbScopeType?.IsChecked == true) accept &= string.Equals(row.Scope, "Type", StringComparison.OrdinalIgnoreCase);
            else if (RbScopeInstance?.IsChecked == true) accept &= string.Equals(row.Scope, "Instance", StringComparison.OrdinalIgnoreCase);

            e.Accepted = accept;
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            _parametersView.View?.Refresh();
            UpdateVisibleCount();
        }

        private void UpdateVisibleCount()
        {
            if (TxtVisibleCount == null) return;

            int visible = 0;
            foreach (var _ in _parametersView.View) visible++;

            TxtVisibleCount.Text = $"Showing {visible} of {Parameters.Count} parameters";
        }

        private void LoadParameters()
        {
            Parameters.Clear();

            if (_doc == null || !_doc.IsFamilyDocument)
            {
                MessageBox.Show("Active document is not a family. Please open a family first.");
                return;
            }

            var fm = _doc.FamilyManager;

            foreach (FamilyParameter fp in fm.GetParameters())
            {
                string friendly = "<Unknown>";
                try
                {
                    var specId = fp.Definition.GetDataType();
                    friendly = LabelUtils.GetLabelForSpec(specId);
                }
                catch { }

                bool isBuiltIn = fp.Definition is InternalDefinition intDef
                                 && intDef.BuiltInParameter != BuiltInParameter.INVALID;

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

                Guid sharedGuid = Guid.Empty;
                if (fp.IsShared && fp.Definition is ExternalDefinition extDef)
                {
                    sharedGuid = extDef.GUID;
                }

                Parameters.Add(new ParameterPreview
                {
                    Name = fp.Definition.Name,
                    Scope = fp.IsInstance ? "Instance" : "Type",
                    IsShared = fp.IsShared,
                    IsBuiltIn = isBuiltIn,
                    Spec = friendly,
                    GroupTypeId = groupTypeId,
                    GroupName = groupName,
                    TargetName = "",
                    MatchedShared = "",
                    DeleteRequested = false,
                    SharedGuid = sharedGuid
                });
            }

            UpdateVisibleCount();
        }



        // ============================
        // Selection helpers
        // ============================

        private ParameterPreview[] GetSelectedRows()
        {
            return DgParameters.SelectedItems
                .Cast<object>()
                .OfType<ParameterPreview>()
                .ToArray();
        }

        /// <summary>
        /// Shift-click on Sel checkbox applies same check to all currently selected rows.
        /// </summary>
        private void SelCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb) return;
            if (cb.DataContext is not ParameterPreview clickedRow) return;

            // Ensure row is part of selection when user clicks checkbox
            if (!DgParameters.SelectedItems.Contains(clickedRow))
            {
                DgParameters.SelectedItem = clickedRow;
            }

            bool desired = cb.IsChecked == true;

            // Shift-click => apply to all currently selected rows
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                var selected = DgParameters.SelectedItems.Cast<object>()
                    .OfType<ParameterPreview>()
                    .ToList();

                if (selected.Count > 0)
                {
                    foreach (var r in selected)
                        r.IsSelected = desired;

                    _lastSelToggleAnchor = clickedRow;
                    return;
                }
            }

            // normal click
            clickedRow.IsSelected = desired;
            _lastSelToggleAnchor = clickedRow;
        }

        // ============================
        // Bulk ops
        // ============================

        private void BtnPreview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var app = _uiApp.Application;
                var spOverride = TxtSharedParamPath?.Text ?? string.Empty;

                SharedParamUtils.LoadSharedParameterFile(app, string.IsNullOrWhiteSpace(spOverride) ? null : spOverride);
                var lookup = SharedParamUtils.BuildExternalDefinitionLookup();

                var fm = _doc.FamilyManager;

                foreach (var row in Parameters)
                {
                    string matched;
                    var ext = SharedParamUtils.FindBestSharedDefinition(row.Name, lookup, out matched, minScore: 0.66);

                    if (ext != null && !row.IsBuiltIn && !row.IsShared)
                    {
                        row.MatchedShared = matched;
                        row.TargetName = matched;   // without it EffectiveAction stays "Keep"

                        var sTokens = NameMatcher.Tokens(matched);
                        var fTokens = NameMatcher.Tokens(row.Name);
                        row.MatchScore = NameMatcher.ScoreTokens(fTokens, sTokens);
                    }
                    else
                    {
                        row.MatchedShared = "";
                        row.TargetName = "";        // clear stale target on re-scan
                        row.MatchScore = 0;
                    }

                    // Refresh SharedGuid against the new lookup so favorites saved
                    // after this preview resolve to the correct definition.
                    row.SharedGuid = FamilyParamUtils.ResolveSharedGuid(fm, row, lookup);
                    row.IsFavoriteStale = false;
                }

                _parametersView.View?.Refresh();
                UpdateVisibleCount();

                MessageBox.Show("Preview updated.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Preview failed: " + ex.Message);
            }
        }
        private void BtnAddShared_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null || !_doc.IsFamilyDocument)
            {
                MessageBox.Show("Active document is not a family. Please open a family first.");
                return;
            }

            var dlg = new AddSharedParamsDialog(_uiApp, _doc) { Owner = this };
            dlg.ShowDialog();
        }

        private void BtnCustom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var app = _uiApp.Application;
                var spOverride = TxtSharedParamPath?.Text ?? string.Empty;

                SharedParamUtils.LoadSharedParameterFile(app, string.IsNullOrWhiteSpace(spOverride) ? null : spOverride);
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

                        var fm = _doc.FamilyManager;
                        row.SharedGuid = FamilyParamUtils.ResolveSharedGuid(fm, row, lookup);
                        row.IsFavoriteStale = false;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Custom Replace failed: " + ex.Message);
            }
        }

        private void BtnToggleDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedRows();
            if (rows.Length == 0) { MessageBox.Show("Select one or more rows first."); return; }
            bool anyNotDelete = rows.Any(r => !r.DeleteRequested);
            foreach (var r in rows)
                r.DeleteRequested = anyNotDelete;
        }



        // ============================
        // Favorites panel
        // ============================

        private void RefreshFavoritesList()
        {
            var favorites = FamilyHarmonizerFavoritesStore.Load();

            // Resolve current names against the currently configured SP file, if available.
            Dictionary<string, Definition> lookup = null;
            Dictionary<Guid, string> guidToCurrentName = null;

            try
            {
                var app = _uiApp.Application;
                var spOverride = TxtSharedParamPath?.Text ?? string.Empty;

                SharedParamUtils.LoadSharedParameterFile(app, string.IsNullOrWhiteSpace(spOverride) ? null : spOverride);
                lookup = SharedParamUtils.BuildExternalDefinitionLookup();

                guidToCurrentName = lookup.Values
                    .OfType<ExternalDefinition>()
                    .GroupBy(d => d.GUID)
                    .ToDictionary(g => g.Key, g => g.First().Name);
            }
            catch
            {
                // No SP file configured / loadable yet; favorites will show as stale until one is set.
            }

            var vms = new List<FavoriteVm>();

            foreach (var fav in favorites)
            {
                bool resolved = guidToCurrentName != null && guidToCurrentName.TryGetValue(fav.Guid, out var currentName);
                string displayName = resolved ? guidToCurrentName[fav.Guid] : fav.LastKnownName;

                string sub = resolved
                    ? $"{fav.Spec} | {(fav.IsInstance ? "Instance" : "Type")}"
                    : $"{fav.Spec} | {(fav.IsInstance ? "Instance" : "Type")} | not in current SP file (last known: '{fav.LastKnownName}')";

                vms.Add(new FavoriteVm
                {
                    Guid = fav.Guid,
                    DisplayName = displayName,
                    SubText = sub,
                    IsStale = !resolved,
                    Source = fav,
                    IsChecked = false
                });
            }

            FavoritesList.ItemsSource = vms;
        }

        private void BtnSaveFavorite_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedRows();
            if (rows.Length == 0)
            {
                MessageBox.Show("Select one or more rows first.");
                return;
            }

            try
            {
                var app = _uiApp.Application;
                var spOverride = TxtSharedParamPath?.Text ?? string.Empty;

                SharedParamUtils.LoadSharedParameterFile(app, string.IsNullOrWhiteSpace(spOverride) ? null : spOverride);
                var lookup = SharedParamUtils.BuildExternalDefinitionLookup();

                var fm = _doc.FamilyManager;

                var savedNames = new List<string>();
                var skippedNames = new List<string>();

                foreach (var row in rows)
                {
                    var guid = FamilyParamUtils.ResolveSharedGuid(fm, row, lookup);
                    if (guid == Guid.Empty)
                    {
                        skippedNames.Add(row.Name);
                        continue;
                    }

                    row.SharedGuid = guid;

                    // Resolve current name/spec from the lookup for accurate persisted hints.
                    var def = lookup.Values.OfType<ExternalDefinition>().FirstOrDefault(d => d.GUID == guid);
                    string nameForFavorite = def?.Name ?? (string.IsNullOrWhiteSpace(row.TargetName) ? row.Name : row.TargetName);
                    string specForFavorite = row.Spec;

                    FamilyHarmonizerFavoritesStore.AddOrUpdate(new FamilyHarmonizerFavorite
                    {
                        Guid = guid,
                        LastKnownName = nameForFavorite,
                        Spec = specForFavorite,
                        IsInstance = row.DesiredIsInstance,
                        GroupTypeId = row.GroupTypeId
                    });

                    savedNames.Add(nameForFavorite);
                }

                RefreshFavoritesList();

                if (skippedNames.Count > 0)
                {
                    MessageBox.Show(
                        $"Saved: {(savedNames.Count > 0 ? string.Join(", ", savedNames) : "(none)")}\n\n" +
                        $"Skipped (no shared parameter could be resolved for): {string.Join(", ", skippedNames)}");
                }
                else
                {
                    MessageBox.Show($"Saved as favorite: {string.Join(", ", savedNames)}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Save favorite failed: " + ex.Message);
            }
        }

        private void BtnRemoveFavorite_Click(object sender, RoutedEventArgs e)
        {
            var items = FavoritesList.ItemsSource as IEnumerable<FavoriteVm>;
            var toRemove = items?.Where(f => f.IsChecked).ToList();

            if (toRemove == null || toRemove.Count == 0)
            {
                MessageBox.Show("Check one or more favorites to remove first.");
                return;
            }

            foreach (var f in toRemove)
                FamilyHarmonizerFavoritesStore.Remove(f.Guid);

            RefreshFavoritesList();
        }

        private void BtnLoadFavorite_Click(object sender, RoutedEventArgs e)
        {
            var items = FavoritesList.ItemsSource as IEnumerable<FavoriteVm>;
            var checked_ = items?.Where(f => f.IsChecked).ToList();

            if (checked_ == null || checked_.Count == 0)
            {
                MessageBox.Show("Check exactly one favorite to load.");
                return;
            }

            if (checked_.Count > 1)
            {
                MessageBox.Show("Check exactly one favorite to load (loading multiple favorites at once is not supported).");
                return;
            }

            var fav = checked_[0];

            var targetRows = GetSelectedRows();
            if (targetRows.Length == 0)
            {
                MessageBox.Show("Select one or more rows in the grid to apply this favorite to first.");
                return;
            }

            if (fav.IsStale)
            {
                MessageBox.Show(
                    $"Favorite '{fav.Source.LastKnownName}' (GUID {fav.Guid}) was not found in the currently loaded " +
                    $"shared parameter file. Set the correct shared param file path and try again.");
                return;
            }

            try
            {
                var app = _uiApp.Application;
                var spOverride = TxtSharedParamPath?.Text ?? string.Empty;

                SharedParamUtils.LoadSharedParameterFile(app, string.IsNullOrWhiteSpace(spOverride) ? null : spOverride);
                var lookup = SharedParamUtils.BuildExternalDefinitionLookup();

                var def = lookup.Values.OfType<ExternalDefinition>().FirstOrDefault(d => d.GUID == fav.Guid);
                if (def == null)
                {
                    MessageBox.Show("Favorite GUID could not be resolved against the current shared parameter file.");
                    return;
                }

                string currentName = def.Name;

                // "Load acts as replace": overwrite TargetName/MatchedShared on each selected row,
                // discarding whatever was there (previous favorite, preview match, manual edit).
                foreach (var row in targetRows)
                {
                    row.MatchedShared = currentName;
                    row.TargetName = currentName;
                    row.MatchScore = 1.0;
                    row.DeleteRequested = false;
                    row.SharedGuid = fav.Guid;
                    row.IsFavoriteStale = false;
                }

                _parametersView.View?.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Load favorite failed: " + ex.Message);
            }
        }

        // ============================
        // Grid editing helpers (unchanged)
        // ============================

        private void DgParameters_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Start edit on single click (Excel-like),
            // but don't interfere with checkbox/combobox clicks.
            DependencyObject dep = (DependencyObject)e.OriginalSource;

            // Ignore clicks on interactive controls
            while (dep != null && dep is not DataGridCell && dep is not DataGridColumnHeader)
                dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);

            if (dep is not DataGridCell cell) return;
            if (cell.IsEditing) return;
            if (cell.IsReadOnly) return;

            // If click was on checkbox/combobox, do not force edit
            if (e.OriginalSource is FrameworkElement fe)
            {
                if (fe is CheckBox) return;
                if (FindAncestor<ComboBox>(fe) != null) return;
                if (FindAncestor<CheckBox>(fe) != null) return;
                if (FindAncestor<Button>(fe) != null) return;
            }

            // Make sure the clicked row becomes current
            if (!cell.IsSelected)
            {
                cell.Focus();
                cell.IsSelected = true;
            }

            // Begin edit immediately
            DgParameters.BeginEdit();

            e.Handled = true;
        }

        private void DgParameters_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // If user starts typing while a row is selected, auto-begin edit
            if (e.Key == Key.F2 || e.Key == Key.Enter) return; // default behavior ok

            if (e.Key == Key.Delete) return; // your delete handler handles this elsewhere

            // letters, digits, space etc => start edit
            if (e.Key >= Key.A && e.Key <= Key.Z ||
                e.Key >= Key.D0 && e.Key <= Key.D9 ||
                e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9 ||
                e.Key == Key.Space)
            {
                if (!DgParameters.IsReadOnly && DgParameters.CurrentCell.Column != null)
                {
                    DgParameters.BeginEdit();
                }
            }
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T hit) return hit;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }
        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "CSV Files (*.csv)|*.csv" };
            if (dialog.ShowDialog() == true)
            {
                using var sw = new System.IO.StreamWriter(dialog.FileName, false, Encoding.UTF8);
                sw.WriteLine("Name,Scope,IsShared,Spec,Decision,TargetName,MatchedShared,Score,DeleteRequested,SharedGuid");
                foreach (var p in Parameters)
                    sw.WriteLine($"{p.Name},{p.Scope},{p.IsShared},{p.Spec},{p.EffectiveAction},{p.TargetName},{p.MatchedShared},{p.MatchScore},{p.DeleteRequested},{p.SharedGuid}");
                MessageBox.Show("Exported successfully.");
            }
        }

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

        private void BtnBrowseSP_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Shared Parameter File",
                Filter = "TXT files (*.txt)|*.txt|All files (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true && TxtSharedParamPath != null)
            {
                TxtSharedParamPath.Text = dlg.FileName;
                RefreshFavoritesList();
            }
        }

        private void BtnApplyPrefix_Click(object sender, RoutedEventArgs e)
        {
            var prefix = (TxtPrefixNew.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(prefix))
            {
                MessageBox.Show("Type a prefix first.");
                return;
            }

            var targets = DgParameters.SelectedItems.Cast<object>()
                .OfType<ParameterPreview>()
                .ToList();

            if (targets.Count == 0)
            {
                MessageBox.Show("Select one or more rows first.");
                return;
            }

            foreach (var row in targets)
            {
                var baseName = string.IsNullOrWhiteSpace(row.TargetName) ? row.Name : row.TargetName;

                if (!baseName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    row.TargetName = prefix + baseName;
            }
        }

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
        private void DgParameters_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete) return;
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;

            var rows = GetSelectedRows();
            if (rows.Length == 0) return;

            bool undo = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            foreach (var r in rows)
                r.DeleteRequested = !undo;

            e.Handled = true;
        }
        private void BtnSetInstanceSelected_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedRows();
            if (rows.Length == 0)
            {
                MessageBox.Show("Select one or more rows first.");
                return;
            }
            foreach (var r in rows)
                r.Scope = "Instance";
        }

        private void BtnSetTypeSelected_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedRows();
            if (rows.Length == 0)
            {
                MessageBox.Show("Select one or more rows first.");
                return;
            }
            foreach (var r in rows)
                r.Scope = "Type";
        }
    }
    internal static class ListExtensions
    {
        public static void AddRange<T>(this IList<T> list, IEnumerable<T> items)
        {
            foreach (var i in items) list.Add(i);
        }
    }
}

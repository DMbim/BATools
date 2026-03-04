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
using System.Windows.Data;
using System.Windows.Input;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace BA.UI
{
    public partial class FamilyParametersUI : Window
    {
        private readonly UIApplication _uiApp;
        private ParameterPreview? _lastSelToggleAnchor;
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;

        public ObservableCollection<ParameterPreview> Parameters { get; } = new();

        public string[] ActionOptions { get; } = { "Keep", "Replace", "Rename", "Delete" };
        public string[] ScopeOptions { get; } = { "Instance", "Type" };

        public ObservableCollection<ParamGroupPick> GroupOptions { get; } =
            new ObservableCollection<ParamGroupPick>(ParamGroupCatalog.GetAvailable());

        private readonly HarmonizerEventHandler _handler;
        private readonly ExternalEvent _extEvent;

        public FamilyParametersUI(ExternalCommandData commandData)
        {
            _uiApp = commandData?.Application ?? throw new ArgumentNullException(nameof(commandData));
            _uiDoc = _uiApp.ActiveUIDocument ?? throw new InvalidOperationException("ActiveUIDocument is null.");
            _doc = _uiDoc.Document ?? throw new InvalidOperationException("Active document is null.");

            InitializeComponent();
            DataContext = this;

            if (TxtSharedParamPath != null)
                TxtSharedParamPath.Text = _uiApp.Application.SharedParametersFilename ?? string.Empty;

            if (CmbBulkGroup != null && GroupOptions.Count > 0)
                CmbBulkGroup.SelectedIndex = 0;

            LoadParameters();
            Loaded += (_, __) => SetupGrouping();

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
            _uiDoc = uiDoc ?? throw new ArgumentNullException(nameof(uiDoc));
            _uiApp = _uiDoc.Application ?? throw new InvalidOperationException("UIApplication is null.");
            _doc = _uiDoc.Document ?? throw new InvalidOperationException("Active document is null.");

            InitializeComponent();
            DataContext = this;

            if (TxtSharedParamPath != null)
                TxtSharedParamPath.Text = _uiApp.Application.SharedParametersFilename ?? string.Empty;

            if (CmbBulkGroup != null && GroupOptions.Count > 0)
                CmbBulkGroup.SelectedIndex = 0;

            LoadParameters();
            Loaded += (_, __) => SetupGrouping();

            _handler = new HarmonizerEventHandler
            {
                UiApplication = _uiApp,
                UiDocument = _uiDoc,
                Document = _doc
            };

            _extEvent = ExternalEvent.Create(_handler);
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
            var options = GroupOptions.ToList();

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

                ForgeTypeId groupId = GroupTypeId.Data;
                try { groupId = fp.Definition.GetGroupTypeId(); } catch { }

                var pick = ParamGroupCatalog.FromForgeTypeId(groupId, options);

                if (!GroupOptions.Any(x => x.GroupTypeId == pick.GroupTypeId))
                    GroupOptions.Add(pick);

                Parameters.Add(new ParameterPreview
                {
                    Name = fp.Definition.Name,
                    Scope = fp.IsInstance ? "Instance" : "Type",
                    IsShared = fp.IsShared,
                    IsBuiltIn = isBuiltIn,
                    Spec = friendly,

                    GroupTypeId = pick.GroupTypeId,
                    GroupName = pick.Name,

                    Action = "Keep"
                });
            }
        }

        private void SetupGrouping()
        {
            var cvs = (CollectionViewSource)FindResource("CvsParameters");
            if (cvs == null) return;

            cvs.Source = Parameters;

            var view = cvs.View as ListCollectionView;
            if (view == null) return;

            using (view.DeferRefresh())
            {
                view.GroupDescriptions.Clear();

                // Group by Group(cur) = GroupName
                view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ParameterPreview.GroupName)));

                // CustomSort controls both group order + item order
                view.CustomSort = new ParameterPreviewRevitGroupComparer(GroupOptions);
            }
        }

        /// <summary>
        /// Sorts by GroupTypeId using GroupOptions order, then Name.
        /// </summary>
        private sealed class ParameterPreviewRevitGroupComparer : IComparer
        {
            private readonly Dictionary<string, int> _groupIndex;

            public ParameterPreviewRevitGroupComparer(ObservableCollection<ParamGroupPick> groupOptions)
            {
                _groupIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                int i = 0;
                foreach (var g in groupOptions ?? new ObservableCollection<ParamGroupPick>())
                {
                    var id = g?.GroupTypeId ?? "";
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    if (_groupIndex.ContainsKey(id)) continue;
                    _groupIndex[id] = i++;
                }
            }

            public int Compare(object x, object y)
            {
                var a = x as ParameterPreview;
                var b = y as ParameterPreview;
                if (a == null && b == null) return 0;
                if (a == null) return -1;
                if (b == null) return 1;

                int ai = GetGroupOrderIndex(a);
                int bi = GetGroupOrderIndex(b);
                int c = ai.CompareTo(bi);
                if (c != 0) return c;

                c = string.Compare(a.GroupName ?? "", b.GroupName ?? "", StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;

                c = string.Compare(a.Name ?? "", b.Name ?? "", StringComparison.OrdinalIgnoreCase);
                return c;
            }

            private int GetGroupOrderIndex(ParameterPreview p)
            {
                var id = p.GroupTypeId ?? "";
                if (!string.IsNullOrWhiteSpace(id) && _groupIndex.TryGetValue(id, out int idx))
                    return idx;

                return int.MaxValue - 1;
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
                TxtSharedParamPath.Text = dlg.FileName;
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

        private void BtnToggleDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedRows();
            if (rows.Length == 0) { MessageBox.Show("Select one or more rows first."); return; }

            bool anyNotDelete = rows.Any(r => !string.Equals(r.Action, "Delete", StringComparison.OrdinalIgnoreCase));
            foreach (var r in rows)
                r.Action = anyNotDelete ? "Delete" : "Keep";
        }

        private void BtnSetInstanceSelected_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedRows();
            if (rows.Length == 0) { MessageBox.Show("Select one or more rows first."); return; }
            foreach (var r in rows) r.Scope = "Instance";
        }

        private void BtnSetTypeSelected_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedRows();
            if (rows.Length == 0) { MessageBox.Show("Select one or more rows first."); return; }
            foreach (var r in rows) r.Scope = "Type";
        }

        private void BtnApplyGroupSelected_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedRows();
            if (rows.Length == 0) { MessageBox.Show("Select one or more rows first."); return; }

            var pick = CmbBulkGroup?.SelectedItem as ParamGroupPick;
            if (pick == null) { MessageBox.Show("Pick a group first."); return; }

            foreach (var r in rows)
            {
                r.GroupTypeId = pick.GroupTypeId;
                r.GroupName = pick.Name;
            }

            SetupGrouping();
        }

        // ============================
        // Your Preview (unchanged)
        // ============================

        private void BtnPreview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var app = _uiApp.Application;
                var spOverride = (TxtSharedParamPath?.Text ?? "").Trim();

                SharedParamUtils.LoadSharedParameterFile(app, string.IsNullOrWhiteSpace(spOverride) ? null : spOverride);
                var lookup = SharedParamUtils.BuildExternalDefinitionLookup();

                foreach (var row in Parameters)
                {
                    var target = (string.IsNullOrWhiteSpace(row.NewName) ? row.Name : row.NewName) ?? "";
                    target = target.Trim();

                    string matched;
                    var ext = SharedParamUtils.FindBestSharedDefinition(target, lookup, out matched, minScore: 0.66);

                    if (ext != null)
                    {
                        row.MatchedShared = matched;

                        var sTokens = NameMatcher.Tokens(matched);
                        var fTokens = NameMatcher.Tokens(target);
                        row.MatchScore = NameMatcher.ScoreTokens(fTokens, sTokens);

                        if (!row.IsBuiltIn && !row.IsShared
                            && string.IsNullOrWhiteSpace(row.NewName)
                            && (string.IsNullOrEmpty(row.Action) || row.Action == "Keep"))
                        {
                            row.Action = "Replace";
                        }
                    }
                    else
                    {
                        row.MatchedShared = "";
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

        /// <summary>
        /// Suggest switch = run Preview, then optionally mark Replace for selected rows.
        private void BtnSuggestSwitch_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // “Suggest switch” = run the same matching logic as Preview,
                // but don’t show the popup every time (more “button-like”).
                SuggestSwitchInternal(showMessage: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Suggest switch failed: " + ex.Message);
            }
        }
        private void SuggestSwitchInternal(bool showMessage)
        {
            var app = _uiApp.Application;
            var spOverride = (TxtSharedParamPath?.Text ?? "").Trim();

            SharedParamUtils.LoadSharedParameterFile(app, string.IsNullOrWhiteSpace(spOverride) ? null : spOverride);
            var lookup = SharedParamUtils.BuildExternalDefinitionLookup();

            int suggested = 0;

            foreach (var row in Parameters)
            {
                var target = (string.IsNullOrWhiteSpace(row.NewName) ? row.Name : row.NewName) ?? "";
                target = target.Trim();

                string matched;
                var ext = SharedParamUtils.FindBestSharedDefinition(target, lookup, out matched, minScore: 0.66);

                if (ext != null)
                {
                    row.MatchedShared = matched;

                    var sTokens = NameMatcher.Tokens(matched);
                    var fTokens = NameMatcher.Tokens(target);
                    row.MatchScore = NameMatcher.ScoreTokens(fTokens, sTokens);

                    // Suggest replace only when it makes sense
                    if (!row.IsBuiltIn && !row.IsShared
                        && string.IsNullOrWhiteSpace(row.NewName)
                        && (string.IsNullOrEmpty(row.Action) || row.Action == "Keep"))
                    {
                        row.Action = "Replace";
                        suggested++;
                    }
                }
                else
                {
                    row.MatchedShared = "";
                    row.MatchScore = 0;
                }
            }

            if (showMessage)
                MessageBox.Show($"Suggestion updated.\nRows suggested for Replace: {suggested}");
        }
        /// <summary>
        /// Opens your existing AddSharedParamsDialog (dialog handles the transaction itself).
        /// </summary>
        private void BtnAddShared_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_doc == null || !_doc.IsFamilyDocument)
                {
                    MessageBox.Show("Active document is not a family. Open a family and retry.");
                    return;
                }

                var dlg = new AddSharedParamsDialog(_uiApp, _doc)
                {
                    Owner = this
                };

                dlg.ShowDialog();

                // After adding parameters, reload to reflect in UI
                LoadParameters();
                SetupGrouping();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Add shared params failed: " + ex.Message);
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

            var targets = GetSelectedRows();
            if (targets.Length == 0)
            {
                MessageBox.Show("Select one or more rows first.");
                return;
            }

            foreach (var row in targets)
            {
                var baseName = string.IsNullOrWhiteSpace(row.NewName) ? row.Name : row.NewName;

                if (!baseName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    row.NewName = prefix + baseName;

                if (!string.Equals(row.Action, "Replace", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(row.Action, "Delete", StringComparison.OrdinalIgnoreCase))
                {
                    row.Action = "Rename";
                }
            }
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "CSV Files (*.csv)|*.csv" };
            if (dialog.ShowDialog() == true)
            {
                using var sw = new System.IO.StreamWriter(dialog.FileName, false, Encoding.UTF8);
                sw.WriteLine("Name,Scope,IsShared,Spec,GroupName,GroupTypeId,Action,NewName,MatchedShared,Score");
                foreach (var p in Parameters)
                    sw.WriteLine($"{p.Name},{p.Scope},{p.IsShared},{p.Spec},{p.GroupName},{p.GroupTypeId},{p.Action},{p.NewName},{p.MatchedShared},{p.MatchScore}");
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

        private void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null || !_doc.IsFamilyDocument)
            {
                MessageBox.Show("Active document is not a family.");
                return;
            }

            // Refresh GroupName from GroupTypeId so UI + execution are consistent
            foreach (var r in Parameters)
            {
                if (string.IsNullOrWhiteSpace(r.GroupTypeId))
                    r.GroupTypeId = GroupTypeId.Data.TypeId;

                var match = GroupOptions.FirstOrDefault(x => x.GroupTypeId == r.GroupTypeId);
                r.GroupName = match?.Name ?? r.GroupTypeId;
            }

            _handler.Decisions.Clear();
            _handler.Decisions.AddRange(Parameters);
            _handler.Log.Clear();
            _handler.Log.AppendLine("Starting apply...");

            _handler.SharedParamOverridePath = TxtSharedParamPath?.Text ?? string.Empty;

            _extEvent.Raise();
            DialogResult = true;
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
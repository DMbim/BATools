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
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using TextBox = System.Windows.Controls.TextBox;

namespace BA.UI
{
    public partial class FamilyParametersUI : Window
    {
        private readonly UIApplication _uiApp;
        private ParameterPreview? _lastSelToggleAnchor;
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;

        public ObservableCollection<ParameterPreview> Parameters { get; } = new();



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



            LoadParameters();


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


            LoadParameters();


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
                    DeleteRequested = false
                });
            }
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
        /// Sorts by GroupTypeId using GroupOptions order, then Name.
        /// </summary>
        /// 
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
                foreach (var row in Parameters)
                {
                    string matched;
                    var ext = SharedParamUtils.FindBestSharedDefinition(row.Name, lookup, out matched, minScore: 0.66);

                    if (ext != null && !row.IsBuiltIn && !row.IsShared)
                    {
                        row.MatchedShared = matched;

                        var sTokens = NameMatcher.Tokens(matched);
                        var fTokens = NameMatcher.Tokens(row.Name);
                        row.MatchScore = NameMatcher.ScoreTokens(fTokens, sTokens);
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
        // Your Preview (unchanged)
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
                sw.WriteLine("Name,Scope,IsShared,Spec,Decision,TargetName,MatchedShared,Score,DeleteRequested");
                foreach (var p in Parameters)
                    sw.WriteLine($"{p.Name},{p.Scope},{p.IsShared},{p.Spec},{p.EffectiveAction},{p.TargetName},{p.MatchedShared},{p.MatchScore},{p.DeleteRequested}");
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
                TxtSharedParamPath.Text = dlg.FileName;
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
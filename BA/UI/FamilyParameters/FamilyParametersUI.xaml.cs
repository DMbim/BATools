using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.ObjectModel;
using Microsoft.Win32;
using System;
using System.Linq;
using System.Text;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using BA.Core;

namespace BA.UI
{
    public partial class FamilyParametersUI : Window
    {
        private readonly UIApplication _uiApp;
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;

        // Bound by XAML
        public ObservableCollection<ParameterPreview> Parameters { get; } = new ObservableCollection<ParameterPreview>();
        public string[] ActionOptions { get; } = { "Keep", "Replace", "Rename" };

        // ExternalEvent (safe API calls from WPF)
        private readonly HarmonizerEventHandler _handler;
        private readonly ExternalEvent _extEvent;

        /// <summary>
        /// Preferred constructor when invoked from an IExternalCommand.Execute.
        /// </summary>
        public FamilyParametersUI(ExternalCommandData commandData)
        {
            InitializeComponent();

            _uiApp = commandData?.Application ?? throw new ArgumentNullException(nameof(commandData));
            _uiDoc = _uiApp.ActiveUIDocument ?? throw new InvalidOperationException("ActiveUIDocument is null.");
            _doc = _uiDoc.Document ?? throw new InvalidOperationException("Active document is null.");

            DataContext = this;

            LoadParameters();

            // External event setup
            _handler = new HarmonizerEventHandler
            {
                UiApplication = _uiApp,
                UiDocument = _uiDoc,
                Document = _doc
            };
            _extEvent = ExternalEvent.Create(_handler);
        }

        /// <summary>
        /// Back-compat overload; initializes from a UIDocument.
        /// </summary>
        public FamilyParametersUI(UIDocument uiDoc)
        {
            InitializeComponent();

            _uiDoc = uiDoc ?? throw new ArgumentNullException(nameof(uiDoc));
            _uiApp = _uiDoc.Application ?? throw new InvalidOperationException("UIApplication is null.");
            _doc = _uiDoc.Document ?? throw new InvalidOperationException("Active document is null.");

            DataContext = this;

            LoadParameters();

            // External event setup
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
                catch { /* ignore */ }

                bool isBuiltIn = false;
                if (fp.Definition is InternalDefinition intDef)
                {
                    isBuiltIn = intDef.BuiltInParameter != BuiltInParameter.INVALID;
                }

                Parameters.Add(new ParameterPreview
                {
                    Name = fp.Definition.Name,
                    IsInstance = fp.IsInstance,
                    IsShared = fp.IsShared,
                    IsBuiltIn = isBuiltIn,
                    Spec = friendly,
                    Action = "Keep"
                });
            }
        }


        // UI_FamilyHarmonizerWindow.xaml.cs
        private void BtnPreview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var app = _uiApp.Application;
                var spOverride = TxtSharedParamPath?.Text ?? string.Empty;

                BA.Core.SharedParamUtils.LoadSharedParameterFile(
                    app, string.IsNullOrWhiteSpace(spOverride) ? null : spOverride);

                var lookup = BA.Core.SharedParamUtils.BuildExternalDefinitionLookup();

                foreach (var row in Parameters)
                {
                    var target = string.IsNullOrWhiteSpace(row.NewName) ? row.Name : row.NewName;

                    string matched;
                    var ext = BA.Core.SharedParamUtils
                                    .FindBestSharedDefinition(target, lookup, out matched, minScore: 0.66);

                    if (ext != null)
                    {
                        row.MatchedShared = matched;

                        var sTokens = BA.Core.NameMatcher.Tokens(matched);
                        var fTokens = BA.Core.NameMatcher.Tokens(target);
                        row.MatchScore = BA.Core.NameMatcher.ScoreTokens(fTokens, sTokens);

                        // NEW: auto-switch non-built-in, non-shared params to Replace
                        if (!row.IsBuiltIn && !row.IsShared &&
                            (string.IsNullOrEmpty(row.Action) || row.Action == "Keep"))
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

                MessageBox.Show("Preview updated. Review the 'Proposed Shared' and 'Score' columns.");
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

            // Constructor takes UIApplication + Document (as before)
            var dlg = new AddSharedParamsDialog(_uiApp, _doc);
            dlg.Owner = this;
            dlg.ShowDialog(); // applies changes inside the dialog
        }

        private void BtnCustom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var app = _uiApp.Application;
                var spOverride = TxtSharedParamPath?.Text ?? string.Empty;

                BA.Core.SharedParamUtils.LoadSharedParameterFile(
                    app, string.IsNullOrWhiteSpace(spOverride) ? null : spOverride);

                var lookup = BA.Core.SharedParamUtils.BuildExternalDefinitionLookup();

                var dlg = new CustomReplaceDialog(Parameters.ToList(), lookup)
                {
                    Owner = this
                };

                if (dlg.ShowDialog() == true && dlg.Mapping.HasValue)
                {
                    var (familyParam, sharedParam) = dlg.Mapping.Value;

                    var row = Parameters.FirstOrDefault(p =>
                        p.Name.Equals(familyParam, StringComparison.OrdinalIgnoreCase));

                    if (row != null)
                    {
                        row.Action = "Replace";
                        row.NewName = sharedParam; // explicit shared param to use
                        row.MatchedShared = sharedParam;
                        row.MatchScore = 1.00;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Custom Replace failed: " + ex.Message);
            }
        }
        private void BtnMetric_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null)
            {
                MessageBox.Show("No active document.");
                return;
            }

            try
            {
                using (var t = new Transaction(_doc, "Set metric units"))
                {
                    t.Start();

                    var units = _doc.GetUnits();
                    Units ogUnits = _doc.GetUnits();
                    Units newUnits = new Units(UnitSystem.Metric);

                    _doc.SetUnits(newUnits);
                    t.Commit();
                }

                MessageBox.Show("Family units set to metric display (mm / m² / m³).");
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
            if (dlg.ShowDialog() == true)
                TxtSharedParamPath.Text = dlg.FileName;
        }
        private void BtnApplyPrefix_Click(object sender, RoutedEventArgs e)
        {
            var oldPrefix = TxtPrefixOld.Text ?? string.Empty;
            var newPrefix = TxtPrefixNew.Text ?? string.Empty;

            // Use selected rows; fall back to all
            var selected = DgParameters.SelectedItems
                .OfType<ParameterPreview>()
                .ToList();

            if (!selected.Any())
                selected = Parameters.ToList();

            if (!selected.Any())
            {
                MessageBox.Show("No parameters to modify.");
                return;
            }

            foreach (var row in selected)
            {
                // Work on NewName if set, otherwise on original Name
                var baseName = string.IsNullOrWhiteSpace(row.NewName) ? row.Name : row.NewName;
                var name = baseName ?? string.Empty;

                if (!string.IsNullOrEmpty(oldPrefix) && !string.IsNullOrEmpty(newPrefix))
                {
                    // Replace
                    if (name.StartsWith(oldPrefix, StringComparison.Ordinal))
                        name = newPrefix + name.Substring(oldPrefix.Length);
                }
                else if (!string.IsNullOrEmpty(oldPrefix))
                {
                    // Remove
                    if (name.StartsWith(oldPrefix, StringComparison.Ordinal))
                        name = name.Substring(oldPrefix.Length);
                }
                else if (!string.IsNullOrEmpty(newPrefix))
                {
                    // Add
                    if (!name.StartsWith(newPrefix, StringComparison.Ordinal))
                        name = newPrefix + name;
                }

                row.NewName = name;
            }
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "CSV Files (*.csv)|*.csv" };
            if (dialog.ShowDialog() == true)
            {
                using var sw = new System.IO.StreamWriter(dialog.FileName, false, Encoding.UTF8);
                sw.WriteLine("Name,IsInstance,IsShared,Spec,Action,NewName");
                foreach (var p in Parameters)
                    sw.WriteLine($"{p.Name},{p.IsInstance},{p.IsShared},{p.Spec},{p.Action},{p.NewName}");
                MessageBox.Show("Exported successfully.");
            }
        }

        private void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null || !_doc.IsFamilyDocument)
            {
                MessageBox.Show("Active document is not a family.");
                return;
            }

            // Feed handler + raise external event
            _handler.Decisions.Clear();
            _handler.Decisions.AddRange(Parameters);
            _handler.Log.Clear();

            _handler.Log.AppendLine("Starting harmonization...");
            var spOverride = TxtSharedParamPath?.Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(spOverride))
                _handler.Log.AppendLine($"Using override SP file: {spOverride}");
            // Load happens inside the handler via SharedParamUtils

            _extEvent.Raise(); // Will run HarmonizeFamilyParameters.Execute on Revit UI thread

            DialogResult = true; // close after event completes (Revit shows TaskDialog on completion)
        }
    }

    internal static class ListExtensions
    {
        public static void AddRange<T>(this System.Collections.Generic.List<T> list, System.Collections.Generic.IEnumerable<T> items)
        {
            foreach (var i in items) list.Add(i);
        }
    }

}


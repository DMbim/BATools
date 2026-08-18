// File: BA.UI/Management/TemplateCheckerWindow.xaml.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Standards;
using BA.UI.ExternalEvents;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using VTM = BA.Core.Standards;
using View = Autodesk.Revit.DB.View;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace BA.UI.Management
{
    public partial class TemplateCheckerWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly RevitExternalInvoker _invoker;
        private readonly Document _doc;

        private View? _selectedTemplate;
        private VTM.ViewTemplateStandardFile? _loadedStandard;

        private readonly ObservableCollection<TemplateDiffRow> _diffAll = new();
        private readonly ObservableCollection<TemplateDiffRow> _diffView = new();

        public TemplateCheckerWindow(UIApplication uiApp, Document doc, RevitExternalInvoker invoker)
        {
            InitializeComponent();

            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));

            GridDiff.ItemsSource = _diffView;

            // Safe: called from BimHubWindow.RunCommand lambda which executes
            // on the Revit thread via RevitActionQueueHandler.Execute().
            LoadTemplates();

            TxtFile.Text = Path.Combine(
                ViewTemplateStandardFileIo.DefaultFolder(),
                "ViewTemplateStandard.json");
        }

        // ── Template list ─────────────────────────────────────────────────────
        private void LoadTemplates()
        {
            var templates = new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate)
                .OrderBy(v => v.Name)
                .ToList();

            CmbTemplates.ItemsSource = templates;
            CmbTemplates.DisplayMemberPath = "Name";
            CmbTemplates.SelectedIndex = templates.Count > 0 ? 0 : -1;
        }

        private View? GetSelectedTemplate() => CmbTemplates.SelectedItem as View;

        // ── Browse ────────────────────────────────────────────────────────────
        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Template Standard JSON",
                Filter = "JSON (*.json)|*.json|All files (*.*)|*.*",
                InitialDirectory = ViewTemplateStandardFileIo.DefaultFolder()
            };

            if (dlg.ShowDialog() == true)
                TxtFile.Text = dlg.FileName;
        }

        // ── Save Standard ─────────────────────────────────────────────────────
        // Pattern: SaveFileDialog on WPF thread, Capture on Revit thread,
        // file write back on WPF thread in onCompleted.
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var template = GetSelectedTemplate();
            if (template == null)
            {
                ShowDialog("BA", "Pick a view template first.");
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "Save Template Standard",
                Filter = "JSON (*.json)|*.json",
                InitialDirectory = ViewTemplateStandardFileIo.DefaultFolder(),
                FileName = SanitizeFileName(template.Name) + ".json"
            };

            if (dlg.ShowDialog() != true) return;

            string savePath = dlg.FileName;
            var templateId = template.Id;

            SetBusy(true, "Capturing template...");

            _invoker.Run<VTM.ViewTemplateStandardFile>(
                uiApp =>
                {
                    Document doc = uiApp.ActiveUIDocument?.Document
                        ?? throw new InvalidOperationException("No active document.");
                    View view = (View)(doc.GetElement(templateId)
                        ?? throw new InvalidOperationException("Template element not found."));
                    return ViewTemplateStandardService.Capture(doc, view);
                },
                onCompleted: captured =>
                {
                    SetBusy(false);
                    try
                    {
                        ViewTemplateStandardFileIo.Save(savePath, captured);
                        _loadedStandard = captured;
                        TxtFile.Text = savePath;
                        TxtSummary.Text = $"Saved: {Path.GetFileName(savePath)}";
                        ShowDialog("BA", "Standard saved.");
                    }
                    catch (Exception ex)
                    {
                        ShowDialog("BA", "Save failed (file write):\n" + ex.Message);
                    }
                },
                onError: ex =>
                {
                    SetBusy(false);
                    ShowDialog("BA", "Save failed (capture):\n" + ex.Message);
                });
        }

        // ── Check ─────────────────────────────────────────────────────────────
        private void BtnCheck_Click(object sender, RoutedEventArgs e)
        {
            var template = GetSelectedTemplate();
            if (template == null)
            {
                ShowDialog("BA", "Pick a view template first.");
                return;
            }

            var path = TxtFile.Text?.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                ShowDialog("BA", "Pick an existing standard JSON file.");
                return;
            }

            // JSON load is pure file I/O — WPF thread is fine.
            VTM.ViewTemplateStandardFile standard;
            try
            {
                standard = ViewTemplateStandardFileIo.Load(path);
            }
            catch (Exception ex)
            {
                ShowDialog("BA", "Failed to load standard file:\n" + ex.Message);
                return;
            }

            var templateId = template.Id;

            SetBusy(true, "Comparing...");

            _invoker.Run<List<TemplateDiffRow>>(
                uiApp =>
                {
                    Document doc = uiApp.ActiveUIDocument?.Document
                        ?? throw new InvalidOperationException("No active document.");
                    View view = (View)(doc.GetElement(templateId)
                        ?? throw new InvalidOperationException("Template element not found."));
                    return ViewTemplateStandardService.Compare(doc, view, standard);
                },
                onCompleted: diffs =>
                {
                    SetBusy(false);
                    _loadedStandard = standard;

                    _diffAll.Clear();
                    foreach (var d in diffs) _diffAll.Add(d);
                    RefreshDiffView();

                    int mism = _diffAll.Count(x => x.IsMismatch);
                    TxtSummary.Text =
                        $"Compared '{template.Name}' vs '{Path.GetFileName(path)}'" +
                        $"  —  mismatches: {mism} / total rows: {_diffAll.Count}";
                },
                onError: ex =>
                {
                    SetBusy(false);
                    ShowDialog("BA", "Check failed:\n" + ex.Message);
                });
        }

        // ── Fix Selected ──────────────────────────────────────────────────────
        private void BtnFixSelected_Click(object sender, RoutedEventArgs e)
        {
            var template = GetSelectedTemplate();
            if (template == null)
            {
                ShowDialog("BA", "Pick a view template first.");
                return;
            }

            if (_loadedStandard == null)
            {
                ShowDialog("BA", "Run Check first to load a standard.");
                return;
            }

            var rowsToFix = _diffAll.Where(x => x.IsMismatch && x.ApplyFix).ToList();
            if (rowsToFix.Count == 0)
            {
                ShowDialog("BA", "No rows are marked for fixing.\n\nTick the Fix checkbox on mismatch rows.");
                return;
            }

            var td = new TaskDialog("BA – Fix Selected")
            {
                MainInstruction = "Apply fixes for selected rows?",
                MainContent = $"Rows to fix: {rowsToFix.Count}\n\nThis will overwrite affected settings in the template.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };
            if (td.Show() != TaskDialogResult.Yes) return;

            var standardSnapshot = _loadedStandard;
            var templateId = template.Id;
            var templateName = template.Name;

            SetBusy(true, "Applying fixes...");

            _invoker.Run<ViewTemplateStandardService.FixResult>(
                uiApp =>
                {
                    Document doc = uiApp.ActiveUIDocument?.Document
                        ?? throw new InvalidOperationException("No active document.");
                    View view = (View)(doc.GetElement(templateId)
                        ?? throw new InvalidOperationException("Template element not found."));

                    using var t = new Transaction(doc, "BA | Fix Selected Template Diffs");
                    t.Start();
                    var res = ViewTemplateStandardService.ApplyFixes(doc, view, standardSnapshot, rowsToFix);
                    t.Commit();
                    return res;
                },
                onCompleted: res =>
                {
                    SetBusy(false);
                    ShowDialog("BA",
                        $"Fix applied to '{templateName}'.\n\n" +
                        $"Categories fixed:   {res.FixedCategories}\n" +
                        $"Filters fixed:      {res.FixedFilters}\n" +
                        $"Worksets fixed:     {res.FixedWorksets}\n" +
                        $"Parameters fixed:   {res.FixedParameters}\n" +
                        $"Filter order fixed: {res.FixedFilterOrder}\n\n" +
                        $"Missing targets:    {res.MissingTargets.Count}" +
                        (res.MissingTargets.Count > 0
                            ? "\n\n" + string.Join("\n", res.MissingTargets.Take(10))
                            : string.Empty));

                    // Re-run compare to refresh the diff grid.
                    BtnCheck_Click(this, new RoutedEventArgs());
                },
                onError: ex =>
                {
                    SetBusy(false);
                    ShowDialog("BA", "Fix failed:\n" + ex.Message);
                });
        }

        // ── Apply All ─────────────────────────────────────────────────────────
        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            var template = GetSelectedTemplate();
            if (template == null)
            {
                ShowDialog("BA", "Pick a view template first.");
                return;
            }

            if (_loadedStandard == null)
            {
                var path = TxtFile.Text?.Trim();
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    ShowDialog("BA", "Load a standard JSON file first (Browse / Check).");
                    return;
                }

                try { _loadedStandard = ViewTemplateStandardFileIo.Load(path); }
                catch (Exception ex)
                {
                    ShowDialog("BA", "Failed to load standard file:\n" + ex.Message);
                    return;
                }
            }

            var td = new TaskDialog("BA – Apply Standard")
            {
                MainInstruction = "Apply saved standard to the selected view template?",
                MainContent = "This overwrites all category and filter overrides in the template.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };
            if (td.Show() != TaskDialogResult.Yes) return;

            var standardSnapshot = _loadedStandard;
            var templateId = template.Id;
            var templateName = template.Name;

            SetBusy(true, "Applying standard...");

            _invoker.Run<ViewTemplateStandardService.ApplyResult>(
                uiApp =>
                {
                    Document doc = uiApp.ActiveUIDocument?.Document
                        ?? throw new InvalidOperationException("No active document.");
                    View view = (View)(doc.GetElement(templateId)
                        ?? throw new InvalidOperationException("Template element not found."));

                    using var t = new Transaction(doc, "BA | Apply Template Standard");
                    t.Start();
                    var res = ViewTemplateStandardService.Apply(doc, view, standardSnapshot);
                    t.Commit();
                    return res;
                },
                onCompleted: res =>
                {
                    SetBusy(false);
                    ShowDialog("BA",
                        $"Applied to '{templateName}'.\n\n" +
                        $"Categories applied:  {res.AppliedCategoryCount}\n" +
                        $"Filters applied:     {res.AppliedFilterCount}\n" +
                        $"Worksets applied:    {res.AppliedWorksets}\n\n" +
                        $"Missing categories:  {res.MissingCategories.Count}\n" +
                        $"Missing filters:     {res.MissingFilters.Count}\n" +
                        $"Skipped categories:  {res.SkippedCategories.Count}\n" +
                        $"Skipped filters:     {res.SkippedFilters.Count}");

                    BtnCheck_Click(this, new RoutedEventArgs());
                },
                onError: ex =>
                {
                    SetBusy(false);
                    ShowDialog("BA", "Apply failed:\n" + ex.Message);
                });
        }

        // ── UI helpers ────────────────────────────────────────────────────────
        private void ChkOnlyMismatches_Changed(object sender, RoutedEventArgs e)
            => RefreshDiffView();

        private void RefreshDiffView()
        {
            _diffView.Clear();
            bool only = ChkOnlyMismatches.IsChecked == true;
            IEnumerable<TemplateDiffRow> src = only
                ? _diffAll.Where(x => x.IsMismatch)
                : (IEnumerable<TemplateDiffRow>)_diffAll;
            foreach (var d in src) _diffView.Add(d);
        }

        private void SetBusy(bool busy, string? label = null)
        {
            BtnSave.IsEnabled = !busy;
            BtnCheck.IsEnabled = !busy;
            BtnFixSelected.IsEnabled = !busy;
            BtnApply.IsEnabled = !busy;
            BtnBrowse.IsEnabled = !busy;
            if (label != null) TxtSummary.Text = label;
        }

        private static void ShowDialog(string title, string message)
            => TaskDialog.Show(title, message);

        private static string SanitizeFileName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s.Trim();
        }
    }
}
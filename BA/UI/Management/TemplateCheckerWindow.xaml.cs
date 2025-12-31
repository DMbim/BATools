using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Standards;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using VTM = BA.Core.Standards;
using System.Windows;

namespace BA.UI.Management
{
    public partial class TemplateCheckerWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly Document _doc;

        private View? _selectedTemplate;
        private VTM.ViewTemplateStandardFile? _loadedStandard;

        private readonly ObservableCollection<TemplateDiffRow> _diffAll = new();
        private readonly ObservableCollection<TemplateDiffRow> _diffView = new();

        public TemplateCheckerWindow(UIApplication uiApp, Document doc)
        {
            InitializeComponent();

            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));

            GridDiff.ItemsSource = _diffView;

            LoadTemplates();
            TxtFile.Text = Path.Combine(ViewTemplateStandardFileIo.DefaultFolder(), "ViewTemplateStandard.json");
        }

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

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var template = GetSelectedTemplate();
            if (template == null)
            {
                TaskDialog.Show("BA", "Pick a view template first.");
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

            try
            {
                var file = ViewTemplateStandardService.Capture(_doc, template);
                ViewTemplateStandardFileIo.Save(dlg.FileName, file);

                TxtSummary.Text = $"Saved standard: {Path.GetFileName(dlg.FileName)}";
                TxtFile.Text = dlg.FileName;
                _loadedStandard = file;

                TaskDialog.Show("BA", "Standard saved.");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BA", "Save failed:\n" + ex.Message);
            }
        }

        private void BtnCheck_Click(object sender, RoutedEventArgs e)
        {
            var template = GetSelectedTemplate();
            if (template == null)
            {
                TaskDialog.Show("BA", "Pick a view template first.");
                return;
            }

            var path = TxtFile.Text?.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                TaskDialog.Show("BA", "Pick an existing standard JSON file.");
                return;
            }

            try
            {
                _loadedStandard = ViewTemplateStandardFileIo.Load(path);

                var diffs = ViewTemplateStandardService.Compare(_doc, template, _loadedStandard);

                _diffAll.Clear();
                foreach (var d in diffs) _diffAll.Add(d);

                RefreshDiffView();

                var mism = _diffAll.Count(x => x.IsMismatch);
                TxtSummary.Text = $"Compared '{template.Name}' vs '{Path.GetFileName(path)}' → mismatches: {mism} / rows: {_diffAll.Count}";
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BA", "Check failed:\n" + ex.Message);
            }
        }

        private void BtnFixSelected_Click(object sender, RoutedEventArgs e)
        {
            var template = GetSelectedTemplate();
            if (template == null)
            {
                TaskDialog.Show("BA", "Pick a view template first.");
                return;
            }

            var path = TxtFile.Text?.Trim();
            if (_loadedStandard == null)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    TaskDialog.Show("BA", "Load a standard JSON file first (Browse/Check).");
                    return;
                }
                _loadedStandard = ViewTemplateStandardFileIo.Load(path);
            }

            var rowsToFix = _diffAll.Where(x => x.IsMismatch && x.ApplyFix).ToList();
            if (rowsToFix.Count == 0)
            {
                TaskDialog.Show("BA", "No rows are marked for fixing.\n\nTick the 'Fix' checkbox on mismatched rows.");
                return;
            }

            var td = new TaskDialog("BA – Fix Selected")
            {
                MainInstruction = "Apply fixes for selected rows?",
                MainContent = $"This will overwrite the affected settings in the template.\n\nRows: {rowsToFix.Count}",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };
            if (td.Show() != TaskDialogResult.Yes) return;

            try
            {
                using var t = new Transaction(_doc, "BA | Fix Selected Template Diffs");
                t.Start();

                var res = ViewTemplateStandardService.ApplyFixes(_doc, template, _loadedStandard, rowsToFix);

                t.Commit();

                TaskDialog.Show("BA",
                    $"Fix applied.\n\n" +
                    $"Categories fixed: {res.FixedCategories}\n" +
                    $"Filters fixed: {res.FixedFilters}\n" +
                    $"Worksets fixed: {res.FixedWorksets}\n" +
                    $"Parameters fixed: {res.FixedParameters}\n" +
                    $"Filter order fixed: {res.FixedFilterOrder}\n\n" +
                    $"Missing targets: {res.MissingTargets.Count}");

                // re-check to refresh UI
                BtnCheck_Click(sender, e);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BA", "Fix failed:\n" + ex.Message);
            }
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            var template = GetSelectedTemplate();
            if (template == null)
            {
                TaskDialog.Show("BA", "Pick a view template first.");
                return;
            }

            var path = TxtFile.Text?.Trim();
            if (_loadedStandard == null)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    TaskDialog.Show("BA", "Load a standard JSON file first (Browse/Check).");
                    return;
                }

                _loadedStandard = ViewTemplateStandardFileIo.Load(path);
            }

            var td = new TaskDialog("BA – Apply Standard")
            {
                MainInstruction = "Apply saved standard to the selected view template?",
                MainContent = "This will overwrite category and filter overrides in the template.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };

            if (td.Show() != TaskDialogResult.Yes) return;

            try
            {
                using var t = new Transaction(_doc, "BA | Apply Template Standard");
                t.Start();

                var res = ViewTemplateStandardService.Apply(_doc, template, _loadedStandard);

                t.Commit();

                TaskDialog.Show("BA",
                    $"Applied.\n\n" +
                    $"Categories: {res.AppliedCategoryCount}\n" +
                    $"Filters: {res.AppliedFilterCount}\n\n" +
                    $"Missing Categories: {res.MissingCategories.Count}\n" +
                    $"Missing Filters: {res.MissingFilters.Count}\n" +
                    $"Skipped Categories: {res.SkippedCategories.Count}\n" +
                    $"Skipped Filters: {res.SkippedFilters.Count}");

                BtnCheck_Click(sender, e);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BA", "Apply failed:\n" + ex.Message);
            }
        }

        private void ChkOnlyMismatches_Changed(object sender, RoutedEventArgs e) => RefreshDiffView();

        private void RefreshDiffView()
        {
            _diffView.Clear();

            bool only = ChkOnlyMismatches.IsChecked == true;
            var src = only ? _diffAll.Where(x => x.IsMismatch) : _diffAll;

            foreach (var d in src)
                _diffView.Add(d);
        }

        private static string SanitizeFileName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s.Trim();
        }
    }
}

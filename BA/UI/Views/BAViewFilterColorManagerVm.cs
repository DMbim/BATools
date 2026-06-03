using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.ViewFilters;
using BA.UI.ExternalEvents;
using BA.UI.Helpers;
using BA.UI.Mvvm;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace BA.UI.Views
{
    public sealed class BAViewFilterColorManagerVm : BA.UI.Mvvm.ObservableObject, IDisposable
    {
        private readonly UIApplication _uiApp;
        private readonly RevitExternalInvoker _revit;
        private readonly Window _window;

        public ObservableCollection<ViewTemplateItem> ViewTemplates { get; } = new();
        public ObservableCollection<FilterRowItem> Filters { get; } = new();
        public ObservableCollection<PaletteColorItem> Palette { get; } = new();
        public ObservableCollection<AssignRowItem> AssignRows { get; } = new();

        private ViewTemplateItem _selectedViewTemplate;
        public ViewTemplateItem SelectedViewTemplate
        {
            get => _selectedViewTemplate;
            set { if (SetProperty(ref _selectedViewTemplate, value)) RaiseCanExecChanged(); }
        }

        private FilterRowItem _selectedFilter;
        public FilterRowItem SelectedFilter
        {
            get => _selectedFilter;
            set { if (SetProperty(ref _selectedFilter, value)) RaiseCanExecChanged(); }
        }

        private AssignRowItem _selectedAssignRow;
        public AssignRowItem SelectedAssignRow
        {
            get => _selectedAssignRow;
            set => SetProperty(ref _selectedAssignRow, value);
        }

        private string _statusText = "Ready";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public BA.UI.Mvvm.RelayCommand LoadTemplatesCommand { get; }
        public BA.UI.Mvvm.RelayCommand LoadFiltersCommand { get; }
        public BA.UI.Mvvm.RelayCommand PreviewFilterCommand { get; }
        public BA.UI.Mvvm.RelayCommand ApplyToTemplateCommand { get; }
        public BA.UI.Mvvm.RelayCommand AutoAssignCommand { get; }
        public BA.UI.Mvvm.RelayCommand EditPaletteColorCommand { get; }
        public BA.UI.Mvvm.RelayCommand PickCutColorCommand { get; }
        public BA.UI.Mvvm.RelayCommand PickProjColorCommand { get; }
        public BA.UI.Mvvm.RelayCommand ImportPaletteCommand { get; }
        public BA.UI.Mvvm.RelayCommand ExportPaletteCommand { get; }
        public BA.UI.Mvvm.RelayCommand CloseCommand { get; }
        public BA.UI.Mvvm.RelayCommand HelpCommand { get; }

        public BAViewFilterColorManagerVm(UIApplication uiApp, RevitExternalInvoker revit, Window window)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _revit = revit ?? throw new ArgumentNullException(nameof(revit));
            _window = window ?? throw new ArgumentNullException(nameof(window));

            SeedDefaultPalette();

            LoadTemplatesCommand = new BA.UI.Mvvm.RelayCommand(_ => LoadTemplates());
            LoadFiltersCommand = new BA.UI.Mvvm.RelayCommand(_ => LoadFilters(), _ => SelectedViewTemplate != null);
            PreviewFilterCommand = new BA.UI.Mvvm.RelayCommand(_ => PreviewSelectedFilter(), _ => SelectedFilter != null);
            ApplyToTemplateCommand = new BA.UI.Mvvm.RelayCommand(_ => ApplyOverridesToTemplate(), _ => SelectedViewTemplate != null && AssignRows.Count > 0);
            AutoAssignCommand = new BA.UI.Mvvm.RelayCommand(_ => AutoAssignPalette(), _ => AssignRows.Count > 0 && Palette.Count > 0);

            EditPaletteColorCommand = new BA.UI.Mvvm.RelayCommand(p => EditPaletteColor(p as PaletteColorItem), p => p is PaletteColorItem);
            PickCutColorCommand = new BA.UI.Mvvm.RelayCommand(p => PickCutColor(p as AssignRowItem), p => p is AssignRowItem);
            PickProjColorCommand = new BA.UI.Mvvm.RelayCommand(p => PickProjColor(p as AssignRowItem), p => p is AssignRowItem);

            ImportPaletteCommand = new BA.UI.Mvvm.RelayCommand(_ => ImportPaletteJson());
            ExportPaletteCommand = new BA.UI.Mvvm.RelayCommand(_ => ExportPaletteJson(), _ => Palette.Count > 0);

            CloseCommand = new BA.UI.Mvvm.RelayCommand(_ => _window.Close());
            HelpCommand = new BA.UI.Mvvm.RelayCommand(_ => ShowHelp());
        }

        public void EnsureTemplatesLoaded()
        {
            if (ViewTemplates.Count == 0)
                LoadTemplatesCommand.Execute(null);
        }

        public void Dispose() { }

        private void RaiseCanExecChanged()
        {
            LoadFiltersCommand.RaiseCanExecuteChanged();
            PreviewFilterCommand.RaiseCanExecuteChanged();
            ApplyToTemplateCommand.RaiseCanExecuteChanged();
            AutoAssignCommand.RaiseCanExecuteChanged();
            ExportPaletteCommand.RaiseCanExecuteChanged();
        }

        private void SeedDefaultPalette()
        {
            Palette.Clear();
            Palette.Add(new PaletteColorItem("01", Color.FromRgb(20, 20, 20)));
            Palette.Add(new PaletteColorItem("02", Color.FromRgb(70, 70, 70)));
            Palette.Add(new PaletteColorItem("03", Color.FromRgb(120, 120, 120)));
            Palette.Add(new PaletteColorItem("04", Color.FromRgb(160, 160, 160)));
            Palette.Add(new PaletteColorItem("05", Color.FromRgb(200, 200, 200)));
            Palette.Add(new PaletteColorItem("06", Color.FromRgb(235, 235, 235)));
        }

        private void LoadTemplates()
        {
            StatusText = "Loading view templates...";

            _revit.Run(
                app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    return ViewFilterColorManagerService.GetViewTemplates(doc);
                },
                templates =>
                {
                    ViewTemplates.Clear();
                    foreach (var t in templates)
                        ViewTemplates.Add(new ViewTemplateItem(t.Id, t.Name));

                    SelectedViewTemplate = ViewTemplates.FirstOrDefault();
                    StatusText = $"Loaded {ViewTemplates.Count} templates.";
                    RaiseCanExecChanged();
                },
                ex => StatusText = "Template load failed: " + ex.Message
            );
        }

        private void LoadFilters()
        {
            if (SelectedViewTemplate == null) return;

            StatusText = "Loading filters from template...";
            var templateId = SelectedViewTemplate.Id;

            _revit.Run(
                app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    return ViewFilterColorManagerService.GetTemplateFilters(doc, templateId);
                },
                filters =>
                {
                    Filters.Clear();
                    AssignRows.Clear();

                    foreach (var f in filters)
                    {
                        Filters.Add(new FilterRowItem(f.FilterId, f.Name, f.CategorySummary, f.IsVisible));

                        var cut = (f.CutR.HasValue && f.CutG.HasValue && f.CutB.HasValue)
                            ? Color.FromRgb(f.CutR.Value, f.CutG.Value, f.CutB.Value)
                            : (Color?)null;

                        var proj = (f.ProjR.HasValue && f.ProjG.HasValue && f.ProjB.HasValue)
                            ? Color.FromRgb(f.ProjR.Value, f.ProjG.Value, f.ProjB.Value)
                            : (Color?)null;

                        AssignRows.Add(new AssignRowItem(f.FilterId, f.Name)
                        {
                            CutBrush = cut.HasValue ? new SolidColorBrush(cut.Value) : new SolidColorBrush(Colors.Transparent),
                            ProjectionBrush = proj.HasValue ? new SolidColorBrush(proj.Value) : new SolidColorBrush(Colors.Transparent)
                        });
                    }

                    SelectedFilter = Filters.FirstOrDefault();
                    SelectedAssignRow = AssignRows.FirstOrDefault();

                    StatusText = $"Loaded {Filters.Count} filters.";
                    RaiseCanExecChanged();
                },
                ex => StatusText = "Filter load failed: " + ex.Message
            );
        }

        private void PreviewSelectedFilter()
        {
            if (SelectedFilter == null) return;

            TaskDialog.Show("BA | Filter Preview",
                $"Filter: {SelectedFilter.FilterName}\n" +
                $"Categories: {SelectedFilter.CategorySummary}\n" +
                $"Visible: {SelectedFilter.VisibleText}");
        }

        private void AutoAssignPalette()
        {
            if (Palette.Count == 0 || AssignRows.Count == 0) return;

            for (int i = 0; i < AssignRows.Count; i++)
            {
                var c = Palette[i % Palette.Count].Color;
                AssignRows[i].ProjectionBrush = new SolidColorBrush(c);
                AssignRows[i].CutBrush = new SolidColorBrush(c);
            }

            StatusText = $"Auto-assigned palette to {AssignRows.Count} filters (preview only).";
        }

        private void ApplyOverridesToTemplate()
        {
            if (SelectedViewTemplate == null) return;

            StatusText = "Applying overrides to template...";
            var templateId = SelectedViewTemplate.Id;

            var assigns = BuildAssignmentsSnapshot();

            _revit.Run(
                app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    if (doc == null) return 0;

                    using (var t = new Transaction(doc, "BA | Apply Filter Line Colors"))
                    {
                        t.Start();
                        ViewFilterColorManagerService.ApplyLineOverrides(doc, templateId, assigns);
                        t.Commit();
                    }

                    return assigns.Count;
                },
                changed =>
                {
                    StatusText = $"Applied overrides to {changed} filters.";
                    LoadFilters();
                },
                ex => StatusText = "Apply failed: " + ex.Message
            );
        }

        private List<FilterColorAssignment> BuildAssignmentsSnapshot()
        {
            var list = new List<FilterColorAssignment>();

            foreach (var row in AssignRows)
            {
                byte? cr = null, cg = null, cb = null;
                byte? pr = null, pg = null, pb = null;

                if (row.CutBrush is SolidColorBrush cut && cut.Color.A > 0)
                { cr = cut.Color.R; cg = cut.Color.G; cb = cut.Color.B; }

                if (row.ProjectionBrush is SolidColorBrush proj && proj.Color.A > 0)
                { pr = proj.Color.R; pg = proj.Color.G; pb = proj.Color.B; }

                list.Add(new FilterColorAssignment(row.FilterId, cr, cg, cb, pr, pg, pb));
            }

            return list;
        }

        private void EditPaletteColor(PaletteColorItem item)
        {
            if (item == null) return;

            if (ColorPicker.TryPickColor(item.Color, out var picked))
            {
                item.Color = picked;
                StatusText = $"Palette color {item.Name} updated.";
            }
        }

        private void PickCutColor(AssignRowItem row)
        {
            if (row == null) return;

            var current = (row.CutBrush as SolidColorBrush)?.Color ?? Colors.White;
            if (ColorPicker.TryPickColor(current, out var picked))
            {
                row.CutBrush = new SolidColorBrush(picked);
                StatusText = $"Cut color set: {row.FilterName}";
            }
        }

        private void PickProjColor(AssignRowItem row)
        {
            if (row == null) return;

            var current = (row.ProjectionBrush as SolidColorBrush)?.Color ?? Colors.White;
            if (ColorPicker.TryPickColor(current, out var picked))
            {
                row.ProjectionBrush = new SolidColorBrush(picked);
                StatusText = $"Projection color set: {row.FilterName}";
            }
        }

        private void ImportPaletteJson()
        {
            var dlg = new OpenFileDialog { Title = "Import Palette (JSON)", Filter = "JSON (*.json)|*.json" };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var json = File.ReadAllText(dlg.FileName);
                var dto = JsonSerializer.Deserialize<PaletteDto>(json);

                if (dto == null || dto.Colors == null || dto.Colors.Count == 0)
                    throw new InvalidDataException("Palette file contains no colors.");

                Palette.Clear();
                int i = 1;
                foreach (var c in dto.Colors)
                {
                    Palette.Add(new PaletteColorItem(i.ToString("00"), Color.FromRgb(c.R, c.G, c.B)));
                    i++;
                }

                StatusText = $"Imported palette: {Palette.Count} colors.";
                RaiseCanExecChanged();
            }
            catch (Exception ex)
            {
                StatusText = "Import failed: " + ex.Message;
            }
        }

        private void ExportPaletteJson()
        {
            var dlg = new SaveFileDialog { Title = "Export Palette (JSON)", Filter = "JSON (*.json)|*.json", FileName = "BA_Palette.json" };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var dto = new PaletteDto
                {
                    Colors = Palette.Select(p => new ColorDto { R = p.Color.R, G = p.Color.G, B = p.Color.B }).ToList()
                };

                var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dlg.FileName, json);

                StatusText = $"Exported palette: {Palette.Count} colors.";
            }
            catch (Exception ex)
            {
                StatusText = "Export failed: " + ex.Message;
            }
        }

        private void ShowHelp()
        {
            System.Windows.MessageBox.Show(
                "Workflow:\n" +
                "1) View Template → Load Filters\n" +
                "2) Palette → edit swatches → Auto-Assign (optional)\n" +
                "3) Assign Colors → click Cut / Projection\n" +
                "4) Apply → writes overrides into the selected template\n\n" +
                "Notes:\n• Sets Cut + Projection LINE colors.\n• Palette JSON import/export supported.",
                "BA | Help", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private sealed class PaletteDto { public List<ColorDto> Colors { get; set; } = new(); }
        private sealed class ColorDto { public byte R { get; set; } public byte G { get; set; } public byte B { get; set; } }
    }

    // UI models (keep ElementId everywhere)
    public sealed class ViewTemplateItem
    {
        public ElementId Id { get; }
        public string Name { get; }
        public ViewTemplateItem(ElementId id, string name) { Id = id; Name = name; }
        public override string ToString() => Name;
    }

    public sealed class FilterRowItem : BA.UI.Mvvm.ObservableObject
    {
        public ElementId FilterId { get; }
        public string FilterName { get; }
        public string CategorySummary { get; }

        private bool _visible;
        public bool Visible
        {
            get => _visible;
            set { if (SetProperty(ref _visible, value)) OnPropertyChanged(nameof(VisibleText)); }
        }

        public string VisibleText => Visible ? "Yes" : "No";

        public FilterRowItem(ElementId filterId, string filterName, string categorySummary, bool visible)
        {
            FilterId = filterId;
            FilterName = filterName;
            CategorySummary = categorySummary;
            _visible = visible;
        }
    }

    public sealed class PaletteColorItem : BA.UI.Mvvm.ObservableObject
    {
        public string Name { get; }

        private Color _color;
        public Color Color
        {
            get => _color;
            set { if (SetProperty(ref _color, value)) OnPropertyChanged(nameof(Brush)); }
        }

        public SolidColorBrush Brush => new SolidColorBrush(Color);

        public PaletteColorItem(string name, Color color)
        {
            Name = name;
            _color = color;
        }
    }

    public sealed class AssignRowItem : BA.UI.Mvvm.ObservableObject
    {
        public ElementId FilterId { get; }
        public string FilterName { get; }

        private Brush _cutBrush = new SolidColorBrush(Colors.Transparent);
        public Brush CutBrush { get => _cutBrush; set => SetProperty(ref _cutBrush, value); }

        private Brush _projectionBrush = new SolidColorBrush(Colors.Transparent);
        public Brush ProjectionBrush { get => _projectionBrush; set => SetProperty(ref _projectionBrush, value); }

        public AssignRowItem(ElementId filterId, string filterName)
        {
            FilterId = filterId;
            FilterName = filterName;
        }
    }
}

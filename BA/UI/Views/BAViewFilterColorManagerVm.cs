// File: BA.UI/Views/BAViewFilterColorManagerVm.cs
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

        private bool _suppressCascade;

        public ObservableCollection<ViewTemplateItem> ViewTemplates { get; } = new();
        public ObservableCollection<FilterRowItem> Filters { get; } = new();
        public ObservableCollection<PaletteColorItem> Palette { get; } = new();
        public ObservableCollection<AssignRowItem> AssignRows { get; } = new();

        public ObservableCollection<CategoryInfo> Categories { get; } = new();
        public ObservableCollection<ParameterInfo> Parameters { get; } = new();
        public ObservableCollection<ColorBucketItem> Buckets { get; } = new();
        public BA.UI.Mvvm.RelayCommand ApplyToAllInViewCommand { get; } // <- NEW

        private ProcessMethod _currentMethod = ProcessMethod.ValueBucket;

        private CategoryInfo _selectedCategory;
        public CategoryInfo SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (!SetProperty(ref _selectedCategory, value)) return;
                if (_suppressCascade) return;

                Parameters.Clear();
                SelectedParameter = null;
                Buckets.Clear();
                RaiseParamColorCanExecChanged();

                if (value != null)
                    LoadParametersForCategory(value.Id);
            }
        }

        private ParameterInfo _selectedParameter;
        public ParameterInfo SelectedParameter
        {
            get => _selectedParameter;
            set
            {
                if (!SetProperty(ref _selectedParameter, value)) return;
                if (_suppressCascade) return;

                Buckets.Clear();
                RaiseParamColorCanExecChanged();

                if (value != null)
                    GenerateBuckets(_currentMethod);
            }
        }

        private bool _isValueMethod = true;
        public bool IsValueMethod
        {
            get => _isValueMethod;
            set
            {
                if (!value) return;
                if (_isValueMethod) return;

                if (SelectedParameter == null)
                {
                    OnPropertyChanged(nameof(IsValueMethod));
                    OnPropertyChanged(nameof(IsRangeMethod));
                    return;
                }

                if (!ConfirmMethodSwitch())
                {
                    OnPropertyChanged(nameof(IsValueMethod));
                    OnPropertyChanged(nameof(IsRangeMethod));
                    return;
                }

                _isValueMethod = true;
                _isRangeMethod = false;
                OnPropertyChanged(nameof(IsValueMethod));
                OnPropertyChanged(nameof(IsRangeMethod));
                GenerateBuckets(ProcessMethod.ValueBucket);
            }
        }

        private bool _isRangeMethod;
        public bool IsRangeMethod
        {
            get => _isRangeMethod;
            set
            {
                if (!value) return;
                if (_isRangeMethod) return;

                if (SelectedParameter == null)
                {
                    OnPropertyChanged(nameof(IsValueMethod));
                    OnPropertyChanged(nameof(IsRangeMethod));
                    return;
                }

                if (SelectedParameter.StorageType != StorageType.Double && SelectedParameter.StorageType != StorageType.Integer)
                {
                    StatusText = "Range bucketing only supports numeric (Double or Integer) parameters.";
                    OnPropertyChanged(nameof(IsValueMethod));
                    OnPropertyChanged(nameof(IsRangeMethod));
                    return;
                }

                if (!ConfirmMethodSwitch())
                {
                    OnPropertyChanged(nameof(IsValueMethod));
                    OnPropertyChanged(nameof(IsRangeMethod));
                    return;
                }

                _isRangeMethod = true;
                _isValueMethod = false;
                OnPropertyChanged(nameof(IsValueMethod));
                OnPropertyChanged(nameof(IsRangeMethod));
                GenerateBuckets(ProcessMethod.RangeBucket);
            }
        }

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

        public BA.UI.Mvvm.RelayCommand LoadCategoriesCommand { get; }
        public BA.UI.Mvvm.RelayCommand RandomColorsCommand { get; }
        public BA.UI.Mvvm.RelayCommand GradientCommand { get; }
        public BA.UI.Mvvm.RelayCommand AddBucketCommand { get; }
        public BA.UI.Mvvm.RelayCommand RemoveBucketCommand { get; }
        public BA.UI.Mvvm.RelayCommand EditBucketColorCommand { get; }
        public BA.UI.Mvvm.RelayCommand SaveSchemeCommand { get; }
        public BA.UI.Mvvm.RelayCommand LoadSchemeCommand { get; }
        public BA.UI.Mvvm.RelayCommand CreateViewFiltersCommand { get; }
        public BA.UI.Mvvm.RelayCommand CreateLegendCommand { get; }
        public BA.UI.Mvvm.RelayCommand ApplyToSelectionCommand { get; } // <- NEW

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

            LoadCategoriesCommand = new BA.UI.Mvvm.RelayCommand(_ => LoadCategories());
            RandomColorsCommand = new BA.UI.Mvvm.RelayCommand(_ => RandomizeBucketColors(), _ => Buckets.Count > 0);
            GradientCommand = new BA.UI.Mvvm.RelayCommand(_ => ApplyGradient(), _ => Buckets.Count >= 2);
            AddBucketCommand = new BA.UI.Mvvm.RelayCommand(_ => AddManualBucket());
            RemoveBucketCommand = new BA.UI.Mvvm.RelayCommand(p => RemoveBucket(p as ColorBucketItem), p => p is ColorBucketItem);
            EditBucketColorCommand = new BA.UI.Mvvm.RelayCommand(p => EditBucketColor(p as ColorBucketItem), p => p is ColorBucketItem);
            SaveSchemeCommand = new BA.UI.Mvvm.RelayCommand(_ => SaveScheme(), _ => Buckets.Count > 0);
            LoadSchemeCommand = new BA.UI.Mvvm.RelayCommand(_ => LoadScheme());
            CreateViewFiltersCommand = new BA.UI.Mvvm.RelayCommand(_ => CreateViewFilters(), _ => SelectedViewTemplate != null && Buckets.Count > 0);
            CreateLegendCommand = new BA.UI.Mvvm.RelayCommand(_ => CreateLegendFromRule(), _ => Buckets.Count > 0);
            ApplyToSelectionCommand = new BA.UI.Mvvm.RelayCommand(_ => ApplyOverridesToSelection(), _ => Buckets.Count > 0); // <- NEW
            ApplyToAllInViewCommand = new BA.UI.Mvvm.RelayCommand(_ => ApplyOverridesToAllInView(), _ => Buckets.Count > 0); // <- NEW
        }

        public void EnsureTemplatesLoaded()
        {
            if (ViewTemplates.Count == 0)
                LoadTemplatesCommand.Execute(null);
        }

        public void EnsureParameterCategoriesLoaded()
        {
            if (Categories.Count == 0)
                LoadCategoriesCommand.Execute(null);
        }

        public void Dispose() { }

        private void RaiseCanExecChanged()
        {
            LoadFiltersCommand.RaiseCanExecuteChanged();
            PreviewFilterCommand.RaiseCanExecuteChanged();
            ApplyToTemplateCommand.RaiseCanExecuteChanged();
            AutoAssignCommand.RaiseCanExecuteChanged();
            ExportPaletteCommand.RaiseCanExecuteChanged();
            CreateViewFiltersCommand?.RaiseCanExecuteChanged();
        }

        private void RaiseParamColorCanExecChanged()
        {
            RandomColorsCommand.RaiseCanExecuteChanged();
            GradientCommand.RaiseCanExecuteChanged();
            SaveSchemeCommand.RaiseCanExecuteChanged();
            CreateViewFiltersCommand.RaiseCanExecuteChanged();
            CreateLegendCommand.RaiseCanExecuteChanged();
            ApplyToSelectionCommand.RaiseCanExecuteChanged(); // <- NEW
            ApplyToAllInViewCommand.RaiseCanExecuteChanged(); // <- NEW
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

                    using (var t = new Transaction(doc, "BA | Apply Filter Colors"))
                    {
                        t.Start();
                        ViewFilterColorManagerService.ApplyFilterOverrides(doc, templateId, assigns);
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
                "1) View Template > Load Filters\n" +
                "2) Palette > edit swatches > Auto-Assign (optional)\n" +
                "3) Assign Colors > click Cut / Projection\n" +
                "4) Apply > writes line color and matching solid fill into the selected template\n\n" +
                "Parameter Colors tab:\n" +
                "1) Pick a Category, then a Parameter\n" +
                "2) Pick Value or Range, buckets auto generate from the model\n" +
                "3) Edit colors, labels, and range breakpoints as needed\n" +
                "4) Create View Filters and/or Create Legend, or Apply to Selection to color\n" +
                "   only the currently selected elements directly, without creating a filter\n\n" +
                "Notes:\n. Sets Cut + Projection LINE colors and matching solid FILL colors.\n. Palette JSON import/export supported.",
                "BA | Help", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LoadCategories()
        {
            StatusText = "Loading categories...";

            _revit.Run(
                app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    return ParameterEnumerationService.GetFilterableCategories(doc);
                },
                cats =>
                {
                    Categories.Clear();
                    foreach (var c in cats)
                        Categories.Add(c);
                    StatusText = $"Loaded {Categories.Count} categories.";
                },
                ex => StatusText = "Category load failed: " + ex.Message
            );
        }

        private void LoadParametersForCategory(ElementId categoryId)
        {
            StatusText = "Loading parameters...";

            _revit.Run(
                app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    return ParameterEnumerationService.GetFilterableParameters(doc, categoryId);
                },
                pars =>
                {
                    Parameters.Clear();
                    foreach (var p in pars)
                        Parameters.Add(p);

                    StatusText = pars.Length > 0
                        ? $"Loaded {pars.Length} parameter(s)."
                        : "No filterable parameters found. This usually means no elements of this category exist in the model yet.";
                },
                ex => StatusText = "Parameter load failed: " + ex.Message
            );
        }

        private bool ConfirmMethodSwitch()
        {
            if (Buckets.Count == 0) return true;

            var result = System.Windows.MessageBox.Show(
                "Switching the process method will clear the current bucket list. Continue?",
                "BA | Switch Process Method",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            return result == MessageBoxResult.Yes;
        }

        private void GenerateBuckets(ProcessMethod method)
        {
            if (SelectedCategory == null || SelectedParameter == null)
            {
                StatusText = "Select a category and parameter first.";
                return;
            }

            if (method == ProcessMethod.RangeBucket &&
                SelectedParameter.StorageType != StorageType.Double &&
                SelectedParameter.StorageType != StorageType.Integer)
            {
                StatusText = "Range bucketing only supports numeric (Double or Integer) parameters.";
                return;
            }

            var categoryId = SelectedCategory.Id;
            var paramInfo = SelectedParameter;

            StatusText = "Scanning model for parameter values...";

            if (method == ProcessMethod.ValueBucket)
            {
                _revit.Run(
                    app =>
                    {
                        var doc = app.ActiveUIDocument?.Document;
                        return ParameterEnumerationService.DiscoverDistinctValues(doc, categoryId, paramInfo);
                    },
                    distinct =>
                    {
                        if (distinct.Count == 0)
                        {
                            StatusText = "No elements with a value for this parameter were found.";
                            return;
                        }

                        var coreBuckets = ParameterEnumerationService.BuildValueBuckets(distinct);
                        Buckets.Clear();
                        foreach (var b in coreBuckets)
                            Buckets.Add(ColorBucketItem.FromCore(b));

                        _currentMethod = ProcessMethod.ValueBucket;
                        StatusText = $"Generated {Buckets.Count} value bucket(s).";
                        RaiseParamColorCanExecChanged();
                    },
                    ex => StatusText = "Value discovery failed: " + ex.Message
                );
            }
            else
            {
                _revit.Run(
                    app =>
                    {
                        var doc = app.ActiveUIDocument?.Document;
                        return ParameterEnumerationService.DiscoverRangeExtent(doc, categoryId, paramInfo);
                    },
                    extent =>
                    {
                        var coreBuckets = ParameterEnumerationService.BuildDefaultRangeBuckets(extent.Min, extent.Max);
                        Buckets.Clear();
                        foreach (var b in coreBuckets)
                            Buckets.Add(ColorBucketItem.FromCore(b));

                        _currentMethod = ProcessMethod.RangeBucket;
                        StatusText = $"Generated {Buckets.Count} range bucket(s), observed {extent.Min:0.##} to {extent.Max:0.##}.";
                        RaiseParamColorCanExecChanged();
                    },
                    ex => StatusText = "Range discovery failed: " + ex.Message
                );
            }
        }

        private void RandomizeBucketColors()
        {
            if (Buckets.Count == 0) return;

            var colors = ParameterEnumerationService.AssignDistinctColors(Buckets.Count);
            for (int i = 0; i < Buckets.Count; i++)
            {
                Buckets[i].R = colors[i].r;
                Buckets[i].G = colors[i].g;
                Buckets[i].B = colors[i].b;
            }

            StatusText = "Random colors reassigned.";
        }

        private void ApplyGradient()
        {
            if (Buckets.Count < 2) return;

            var start = Buckets[0];
            var end = Buckets[Buckets.Count - 1];
            int n = Buckets.Count;

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / (n - 1);
                byte r = (byte)Math.Round(start.R + (end.R - start.R) * t);
                byte g = (byte)Math.Round(start.G + (end.G - start.G) * t);
                byte b = (byte)Math.Round(start.B + (end.B - start.B) * t);

                Buckets[i].R = r;
                Buckets[i].G = g;
                Buckets[i].B = b;
            }

            StatusText = "Gradient applied across buckets.";
        }

        private void AddManualBucket()
        {
            var item = new ColorBucketItem();

            if (_currentMethod == ProcessMethod.RangeBucket)
            {
                item.RangeMin = 0;
                item.RangeMax = 0;
                item.Label = "0 to 0";
            }
            else
            {
                item.Label = "New Value";
                item.Value = "New Value";
            }

            Buckets.Add(item);
            RaiseParamColorCanExecChanged();
        }

        private void RemoveBucket(ColorBucketItem item)
        {
            if (item == null) return;
            Buckets.Remove(item);
            RaiseParamColorCanExecChanged();
        }

        private void EditBucketColor(ColorBucketItem item)
        {
            if (item == null) return;

            var current = Color.FromRgb(item.R, item.G, item.B);
            if (ColorPicker.TryPickColor(current, out var picked))
            {
                item.R = picked.R;
                item.G = picked.G;
                item.B = picked.B;
                StatusText = $"Bucket color updated: {item.Label}";
            }
        }

        private ParameterColorRule BuildRuleSnapshot()
        {
            return new ParameterColorRule
            {
                CategoryId = SelectedCategory.Id,
                CategoryName = SelectedCategory.Name,
                ParameterId = SelectedParameter.Id,
                ParameterName = SelectedParameter.Name,
                StorageType = SelectedParameter.StorageType,
                IsInstance = SelectedParameter.IsInstance,
                Method = _currentMethod,
                Buckets = Buckets.Select(b => b.ToCore()).ToList()
            };
        }

        private void CreateViewFilters()
        {
            if (SelectedViewTemplate == null)
            {
                StatusText = "Select a view template first.";
                return;
            }

            if (SelectedCategory == null || SelectedParameter == null || Buckets.Count == 0)
            {
                StatusText = "Configure a category, parameter, and at least one bucket first.";
                return;
            }

            var rule = BuildRuleSnapshot();
            var templateId = SelectedViewTemplate.Id;

            StatusText = "Generating parameter filters...";

            _revit.Run(
                app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    if (doc == null) return 0;

                    using (var t = new Transaction(doc, "BA | Generate Parameter Filters"))
                    {
                        t.Start();
                        int count;
                        try
                        {
                            count = ParameterFilterGenerationService.GenerateAndApply(doc, templateId, rule);
                            t.Commit();
                        }
                        catch
                        {
                            t.RollBack();
                            throw;
                        }
                        return count;
                    }
                },
                count =>
                {
                    StatusText = $"Created or updated {count} filter(s).";
                    LoadFilters();
                },
                ex => StatusText = "Filter generation failed: " + ex.Message
            );
        }

        private void CreateLegendFromRule()
        {
            if (SelectedCategory == null || SelectedParameter == null || Buckets.Count == 0)
            {
                StatusText = "Configure a category, parameter, and at least one bucket first.";
                return;
            }

            var rule = BuildRuleSnapshot();

            StatusText = "Creating legend...";

            _revit.Run(
                app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    if (doc == null) return ElementId.InvalidElementId;

                    using (var t = new Transaction(doc, "BA | Create Legend"))
                    {
                        t.Start();
                        ElementId id;
                        try
                        {
                            id = LegendGenerationService.CreateLegend(doc, rule);
                            t.Commit();
                        }
                        catch
                        {
                            t.RollBack();
                            throw;
                        }
                        return id;
                    }
                },
                id => StatusText = id != ElementId.InvalidElementId ? "Legend created." : "Legend creation failed.",
                ex => StatusText = "Legend creation failed: " + ex.Message
            );
        }

        // Applies the current rule directly to whatever is selected in the
        // active view, no ParameterFilterElement involved. Reads live
        // selection through UIDocument.Selection, which only exists on the
        // Revit API thread, so this has to go through _revit.Run the same
        // as every other Revit touching operation in this ViewModel. // <- NEW
        private void ApplyOverridesToSelection()
        {
            if (SelectedCategory == null || SelectedParameter == null || Buckets.Count == 0)
            {
                StatusText = "Configure a category, parameter, and at least one bucket first.";
                return;
            }

            var rule = BuildRuleSnapshot();

            StatusText = "Applying colors to selection...";

            _revit.Run(
                app =>
                {
                    var uiDoc = app.ActiveUIDocument;
                    var doc = uiDoc?.Document;

                    if (doc == null || uiDoc == null)
                        return (Applied: 0, SkippedCategory: 0, SkippedNoMatch: 0, HadSelection: false, ViewValid: true);

                    var view = doc.ActiveView;

                    if (view == null || !view.AreGraphicsOverridesAllowed())
                        return (Applied: 0, SkippedCategory: 0, SkippedNoMatch: 0, HadSelection: false, ViewValid: false);

                    var selectedIds = uiDoc.Selection.GetElementIds();

                    if (selectedIds == null || selectedIds.Count == 0)
                        return (Applied: 0, SkippedCategory: 0, SkippedNoMatch: 0, HadSelection: false, ViewValid: true);

                    (int applied, int skippedCategory, int skippedNoMatch) result;

                    using (var t = new Transaction(doc, "BA | Apply Colors to Selection"))
                    {
                        t.Start();
                        try
                        {
                            result = ViewFilterColorManagerService.ApplySelectionOverrides(doc, view, selectedIds, rule);
                            t.Commit();
                        }
                        catch
                        {
                            t.RollBack();
                            throw;
                        }
                    }

                    return (Applied: result.applied, SkippedCategory: result.skippedCategory, SkippedNoMatch: result.skippedNoMatch, HadSelection: true, ViewValid: true);
                },
                outcome =>
                {
                    if (!outcome.ViewValid)
                    {
                        StatusText = "The active view does not support element graphic overrides.";
                        return;
                    }

                    if (!outcome.HadSelection)
                    {
                        StatusText = "Nothing selected in the active view.";
                        return;
                    }

                    StatusText = $"Applied colors to {outcome.Applied} element(s). Skipped {outcome.SkippedCategory} (different category), {outcome.SkippedNoMatch} (no matching value).";
                },
                ex => StatusText = "Apply to selection failed: " + ex.Message
            );
        }
        // Applies the current rule to every element of the configured category
        // visible in the active view, not just the current selection. Uses
        // FilteredElementCollector(doc, view.Id), which correctly respects view
        // specific visibility, crop region, and view range, this is not the same
        // as a document wide collector. No ParameterFilterElement is created,
        // this is a direct one time paint, identical override logic to
        // ApplyOverridesToSelection, just a different source of element ids. // <- NEW
        private void ApplyOverridesToAllInView()
        {
            if (SelectedCategory == null || SelectedParameter == null || Buckets.Count == 0)
            {
                StatusText = "Configure a category, parameter, and at least one bucket first.";
                return;
            }

            var rule = BuildRuleSnapshot();

            StatusText = "Applying colors to all matching elements in view...";

            _revit.Run(
                app =>
                {
                    var uiDoc = app.ActiveUIDocument;
                    var doc = uiDoc?.Document;

                    if (doc == null)
                        return (Applied: 0, SkippedCategory: 0, SkippedNoMatch: 0, TotalFound: 0, ViewValid: true);

                    var view = doc.ActiveView;

                    if (view == null || !view.AreGraphicsOverridesAllowed())
                        return (Applied: 0, SkippedCategory: 0, SkippedNoMatch: 0, TotalFound: 0, ViewValid: false);

                    var candidateIds = new FilteredElementCollector(doc, view.Id)
                        .OfCategoryId(rule.CategoryId)
                        .WhereElementIsNotElementType()
                        .ToElementIds();

                    if (candidateIds == null || candidateIds.Count == 0)
                        return (Applied: 0, SkippedCategory: 0, SkippedNoMatch: 0, TotalFound: 0, ViewValid: true);

                    (int applied, int skippedCategory, int skippedNoMatch) result;

                    using (var t = new Transaction(doc, "BA | Apply Colors to All in View"))
                    {
                        t.Start();
                        try
                        {
                            result = ViewFilterColorManagerService.ApplySelectionOverrides(doc, view, candidateIds, rule);
                            t.Commit();
                        }
                        catch
                        {
                            t.RollBack();
                            throw;
                        }
                    }

                    return (Applied: result.applied, SkippedCategory: result.skippedCategory, SkippedNoMatch: result.skippedNoMatch, TotalFound: candidateIds.Count, ViewValid: true);
                },
                outcome =>
                {
                    if (!outcome.ViewValid)
                    {
                        StatusText = "The active view does not support element graphic overrides.";
                        return;
                    }

                    if (outcome.TotalFound == 0)
                    {
                        StatusText = "No elements of this category are visible in the active view.";
                        return;
                    }

                    StatusText = $"Applied colors to {outcome.Applied} of {outcome.TotalFound} element(s) in view. Skipped {outcome.SkippedNoMatch} (no matching value).";
                },
                ex => StatusText = "Apply to all in view failed: " + ex.Message
            );
        }
        private sealed class SchemeDto
        {
            public string CategoryName { get; set; } = string.Empty;
            public string ParameterName { get; set; } = string.Empty;
            public string StorageType { get; set; } = string.Empty;
            public bool IsInstance { get; set; }
            public string Method { get; set; } = string.Empty;
            public List<BucketDto> Buckets { get; set; } = new();
        }

        private sealed class BucketDto
        {
            public string Label { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public double? RangeMin { get; set; }
            public double? RangeMax { get; set; }
            public byte R { get; set; }
            public byte G { get; set; }
            public byte B { get; set; }
        }

        private void SaveScheme()
        {
            if (SelectedCategory == null || SelectedParameter == null || Buckets.Count == 0)
            {
                StatusText = "Nothing to save yet.";
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "Save Color Scheme",
                Filter = "BA Color Scheme (*.bacs)|*.bacs",
                FileName = "BA_ColorScheme.bacs"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var dto = new SchemeDto
                {
                    CategoryName = SelectedCategory.Name,
                    ParameterName = SelectedParameter.Name,
                    StorageType = SelectedParameter.StorageType.ToString(),
                    IsInstance = SelectedParameter.IsInstance,
                    Method = _currentMethod.ToString(),
                    Buckets = Buckets.Select(b => new BucketDto
                    {
                        Label = b.Label,
                        Value = b.Value,
                        RangeMin = b.RangeMin,
                        RangeMax = b.RangeMax,
                        R = b.R,
                        G = b.G,
                        B = b.B
                    }).ToList()
                };

                var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dlg.FileName, json);
                StatusText = "Color scheme saved.";
            }
            catch (Exception ex)
            {
                StatusText = "Save failed: " + ex.Message;
            }
        }

        private void LoadScheme()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Load Color Scheme",
                Filter = "BA Color Scheme (*.bacs)|*.bacs"
            };
            if (dlg.ShowDialog() != true) return;

            SchemeDto dto;
            try
            {
                var json = File.ReadAllText(dlg.FileName);
                dto = JsonSerializer.Deserialize<SchemeDto>(json);
                if (dto == null) throw new InvalidDataException("Scheme file is empty or invalid.");
            }
            catch (Exception ex)
            {
                StatusText = "Load failed: " + ex.Message;
                return;
            }

            StatusText = "Resolving category and parameter against this document...";

            _revit.Run(
                app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    var categories = ParameterEnumerationService.GetFilterableCategories(doc);
                    var cat = categories.FirstOrDefault(c => c.Name.Equals(dto.CategoryName, StringComparison.OrdinalIgnoreCase));

                    if (cat == null)
                        return (Category: (CategoryInfo)null, Parameters: Array.Empty<ParameterInfo>());

                    var pars = ParameterEnumerationService.GetFilterableParameters(doc, cat.Id);
                    return (Category: cat, Parameters: pars);
                },
                result =>
                {
                    if (result.Category == null)
                    {
                        StatusText = $"Load failed: category '{dto.CategoryName}' was not found or is not filterable in this document.";
                        return;
                    }

                    var param = result.Parameters.FirstOrDefault(p => p.Name.Equals(dto.ParameterName, StringComparison.OrdinalIgnoreCase));
                    if (param == null)
                    {
                        StatusText = $"Load failed: parameter '{dto.ParameterName}' was not found for category '{dto.CategoryName}' in this document.";
                        return;
                    }

                    _suppressCascade = true;

                    if (!Categories.Any(c => c.Id == result.Category.Id))
                        Categories.Add(result.Category);
                    SelectedCategory = Categories.First(c => c.Id == result.Category.Id);

                    Parameters.Clear();
                    foreach (var p in result.Parameters)
                        Parameters.Add(p);
                    SelectedParameter = param;

                    _suppressCascade = false;

                    _currentMethod = dto.Method == ProcessMethod.RangeBucket.ToString()
                        ? ProcessMethod.RangeBucket
                        : ProcessMethod.ValueBucket;

                    _isValueMethod = _currentMethod == ProcessMethod.ValueBucket;
                    _isRangeMethod = _currentMethod == ProcessMethod.RangeBucket;
                    OnPropertyChanged(nameof(IsValueMethod));
                    OnPropertyChanged(nameof(IsRangeMethod));

                    Buckets.Clear();
                    foreach (var b in dto.Buckets)
                    {
                        Buckets.Add(new ColorBucketItem
                        {
                            Label = b.Label,
                            Value = b.Value,
                            RangeMin = b.RangeMin,
                            RangeMax = b.RangeMax,
                            R = b.R,
                            G = b.G,
                            B = b.B
                        });
                    }

                    RaiseParamColorCanExecChanged();
                    StatusText = $"Loaded scheme, {Buckets.Count} bucket(s).";
                },
                ex => StatusText = "Load failed: " + ex.Message
            );
        }

        private sealed class PaletteDto { public List<ColorDto> Colors { get; set; } = new(); }
        private sealed class ColorDto { public byte R { get; set; } public byte G { get; set; } public byte B { get; set; } }
    }

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

    public sealed class ColorBucketItem : BA.UI.Mvvm.ObservableObject
    {
        private string _label = string.Empty;
        public string Label { get => _label; set => SetProperty(ref _label, value); }

        private string _value = string.Empty;
        public string Value { get => _value; set => SetProperty(ref _value, value); }

        private double? _rangeMin;
        public double? RangeMin { get => _rangeMin; set => SetProperty(ref _rangeMin, value); }

        private double? _rangeMax;
        public double? RangeMax { get => _rangeMax; set => SetProperty(ref _rangeMax, value); }

        private byte _r;
        public byte R { get => _r; set { if (SetProperty(ref _r, value)) OnPropertyChanged(nameof(Swatch)); } }

        private byte _g;
        public byte G { get => _g; set { if (SetProperty(ref _g, value)) OnPropertyChanged(nameof(Swatch)); } }

        private byte _b;
        public byte B { get => _b; set { if (SetProperty(ref _b, value)) OnPropertyChanged(nameof(Swatch)); } }

        public Brush Swatch => new SolidColorBrush(Color.FromRgb(R, G, B));

        public static ColorBucketItem FromCore(ColorBucket core)
        {
            return new ColorBucketItem
            {
                Label = core.Label,
                Value = core.Value,
                RangeMin = core.RangeMin,
                RangeMax = core.RangeMax,
                R = core.R,
                G = core.G,
                B = core.B
            };
        }

        public ColorBucket ToCore()
        {
            return new ColorBucket
            {
                Label = Label,
                Value = Value,
                RangeMin = RangeMin,
                RangeMax = RangeMax,
                R = R,
                G = G,
                B = B
            };
        }
    }
}
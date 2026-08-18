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
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using View = Autodesk.Revit.DB.View;

namespace BA.UI.Views
{
    public sealed class BAViewFilterColorManagerVm : BA.UI.Mvvm.ObservableObject, IDisposable
    {
        private readonly UIApplication _uiApp;
        private readonly RevitExternalInvoker _revit;
        private readonly Window _window;

        private bool _suppressCascade;

        private PaletteWindow _paletteWindow;

        private static readonly FillPatternInfo SolidPatternEntry = new FillPatternInfo(ElementId.InvalidElementId, "Solid (default)");

        public ObservableCollection<TemplateFilterRowItem> TemplateFilters { get; } = new();
        public ICollectionView NativeFilterRows { get; }
        public ICollectionView BaManagedFilterRows { get; }

        public ObservableCollection<ViewTemplateItem> ViewTemplates { get; } = new();
        public ObservableCollection<PaletteColorItem> Palette { get; } = new();

        public ObservableCollection<CategoryInfo> Categories { get; } = new();
        public ObservableCollection<ParameterInfo> Parameters { get; } = new();
        public ObservableCollection<ColorBucketItem> Buckets { get; } = new();
        public ObservableCollection<FillPatternInfo> Patterns { get; } = new();

        public ObservableCollection<SchemeSummary> SavedSchemes { get; } = new();

        // New. Named, reusable "combined filter" groups, assembled from checked
        // rows in either pane. A group is a list of filter NAMES, not ElementIds,
        // same reasoning as SchemeDto: it has to resolve against whatever
        // document/template it is shown or hidden on later. Show/Hide toggles
        // template.SetFilterVisibility for every member filter that exists on
        // the currently selected template, it does not add filters that are not
        // already there, that's what "Apply Scheme to Template" is for. // <- NEW
        public ObservableCollection<FilterGroupSummary> FilterGroups { get; } = new();
        private bool _suppressSelectionSync;
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
        private bool _scopeBucketsToActiveView = true;
        public bool ScopeBucketsToActiveView
        {
            get => _scopeBucketsToActiveView;
            set => SetProperty(ref _scopeBucketsToActiveView, value);
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

        private TemplateFilterRowItem _selectedTemplateFilterRow;
        public TemplateFilterRowItem SelectedTemplateFilterRow
        {
            get => _selectedTemplateFilterRow;
            set { if (SetProperty(ref _selectedTemplateFilterRow, value)) RaiseCanExecChanged(); }
        }

        // Each DataGrid gets its own SelectedItem now, since NativeFilterRows and
        // BaManagedFilterRows are different filtered ICollectionViews over the same
        // underlying TemplateFilters collection. Binding both grids' SelectedItem to
        // one shared property caused the second grid to coerce that property back to
        // null the instant it received a value not present in its own filtered view,
        // silently clearing whatever was just selected in the other grid. These two
        // properties are mutually exclusive, selecting in one clears the other, and
        // both feed SelectedTemplateFilterRow, which is what PreviewFilterCommand and
        // anything else that needs "the current selection" actually reads. // <- NEW
        private TemplateFilterRowItem _selectedNativeFilterRow;
        public TemplateFilterRowItem SelectedNativeFilterRow
        {
            get => _selectedNativeFilterRow;
            set
            {
                if (!SetProperty(ref _selectedNativeFilterRow, value)) return;

                if (value != null)
                {
                    _suppressSelectionSync = true;
                    SelectedBaManagedFilterRow = null;
                    _suppressSelectionSync = false;
                    SelectedTemplateFilterRow = value;
                }
                else if (!_suppressSelectionSync)
                {
                    SelectedTemplateFilterRow = SelectedBaManagedFilterRow;
                }
            }
        }

        private TemplateFilterRowItem _selectedBaManagedFilterRow;
        public TemplateFilterRowItem SelectedBaManagedFilterRow
        {
            get => _selectedBaManagedFilterRow;
            set
            {
                if (!SetProperty(ref _selectedBaManagedFilterRow, value)) return;

                if (value != null)
                {
                    _suppressSelectionSync = true;
                    SelectedNativeFilterRow = null;
                    _suppressSelectionSync = false;
                    SelectedTemplateFilterRow = value;
                }
                else if (!_suppressSelectionSync)
                {
                    SelectedTemplateFilterRow = SelectedNativeFilterRow;
                }
            }
        }

        private SchemeSummary _selectedSavedScheme;
        public SchemeSummary SelectedSavedScheme
        {
            get => _selectedSavedScheme;
            set
            {
                if (!SetProperty(ref _selectedSavedScheme, value)) return;
                LoadSchemeCommand?.RaiseCanExecuteChanged();
                AddSchemeToTemplateCommand?.RaiseCanExecuteChanged();
            }
        }

        private string _newSchemeName = string.Empty;
        public string NewSchemeName
        {
            get => _newSchemeName;
            set => SetProperty(ref _newSchemeName, value);
        }

        // New.
        private FilterGroupSummary _selectedFilterGroup;
        public FilterGroupSummary SelectedFilterGroup
        {
            get => _selectedFilterGroup;
            set
            {
                if (!SetProperty(ref _selectedFilterGroup, value)) return;
                ShowGroupOnTemplateCommand?.RaiseCanExecuteChanged();
                HideGroupOnTemplateCommand?.RaiseCanExecuteChanged();
                CreateLegendFromGroupCommand?.RaiseCanExecuteChanged();
            }
        }

        private string _newGroupName = string.Empty;
        public string NewGroupName
        {
            get => _newGroupName;
            set => SetProperty(ref _newGroupName, value);
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
        public BA.UI.Mvvm.RelayCommand OpenPaletteCommand { get; }

        public BA.UI.Mvvm.RelayCommand LoadCategoriesCommand { get; }
        public BA.UI.Mvvm.RelayCommand LoadPatternsCommand { get; }
        public BA.UI.Mvvm.RelayCommand RandomColorsCommand { get; }
        public BA.UI.Mvvm.RelayCommand GradientCommand { get; }
        public BA.UI.Mvvm.RelayCommand AddBucketCommand { get; }
        public BA.UI.Mvvm.RelayCommand RemoveBucketCommand { get; }
        public BA.UI.Mvvm.RelayCommand EditBucketColorCommand { get; }
        public BA.UI.Mvvm.RelayCommand SaveSchemeCommand { get; }
        public BA.UI.Mvvm.RelayCommand LoadSchemeCommand { get; }
        public BA.UI.Mvvm.RelayCommand CreateViewFiltersCommand { get; }
        public BA.UI.Mvvm.RelayCommand CreateLegendCommand { get; }
        public BA.UI.Mvvm.RelayCommand ApplyToSelectionCommand { get; }
        public BA.UI.Mvvm.RelayCommand ApplyToAllInViewCommand { get; }

        public BA.UI.Mvvm.RelayCommand AddSchemeToTemplateCommand { get; }
        public BA.UI.Mvvm.RelayCommand CreateLegendFromSelectedCommand { get; }

        public BA.UI.Mvvm.RelayCommand SaveGroupFromSelectedCommand { get; }
        public BA.UI.Mvvm.RelayCommand ShowGroupOnTemplateCommand { get; }
        public BA.UI.Mvvm.RelayCommand HideGroupOnTemplateCommand { get; }
        public BA.UI.Mvvm.RelayCommand CreateLegendFromGroupCommand { get; }
        public BA.UI.Mvvm.RelayCommand ToggleLegendCheckCommand { get; }

        public BAViewFilterColorManagerVm(UIApplication uiApp, RevitExternalInvoker revit, Window window)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _revit = revit ?? throw new ArgumentNullException(nameof(revit));
            _window = window ?? throw new ArgumentNullException(nameof(window));

            SeedDefaultPalette();

            NativeFilterRows = new ListCollectionView(TemplateFilters) { Filter = o => !((TemplateFilterRowItem)o).IsBaManaged };
            BaManagedFilterRows = new ListCollectionView(TemplateFilters) { Filter = o => ((TemplateFilterRowItem)o).IsBaManaged };

            LoadTemplatesCommand = new BA.UI.Mvvm.RelayCommand(_ => LoadTemplates());
            LoadFiltersCommand = new BA.UI.Mvvm.RelayCommand(_ => LoadFilters(), _ => SelectedViewTemplate != null);
            PreviewFilterCommand = new BA.UI.Mvvm.RelayCommand(_ => PreviewSelectedFilter(), _ => SelectedTemplateFilterRow != null);
            ApplyToTemplateCommand = new BA.UI.Mvvm.RelayCommand(_ => ApplyOverridesToTemplate(), _ => SelectedViewTemplate != null && TemplateFilters.Count > 0);
            AutoAssignCommand = new BA.UI.Mvvm.RelayCommand(_ => AutoAssignPalette(), _ => TemplateFilters.Count > 0 && Palette.Count > 0);

            EditPaletteColorCommand = new BA.UI.Mvvm.RelayCommand(p => EditPaletteColor(p as PaletteColorItem), p => p is PaletteColorItem);
            PickCutColorCommand = new BA.UI.Mvvm.RelayCommand(p => PickCutColor(p as TemplateFilterRowItem), p => p is TemplateFilterRowItem);
            PickProjColorCommand = new BA.UI.Mvvm.RelayCommand(p => PickProjColor(p as TemplateFilterRowItem), p => p is TemplateFilterRowItem);

            ImportPaletteCommand = new BA.UI.Mvvm.RelayCommand(_ => ImportPaletteJson());
            ExportPaletteCommand = new BA.UI.Mvvm.RelayCommand(_ => ExportPaletteJson(), _ => Palette.Count > 0);

            CloseCommand = new BA.UI.Mvvm.RelayCommand(_ => _window.Close());
            HelpCommand = new BA.UI.Mvvm.RelayCommand(_ => ShowHelp());
            OpenPaletteCommand = new BA.UI.Mvvm.RelayCommand(_ => OpenPalette());

            LoadCategoriesCommand = new BA.UI.Mvvm.RelayCommand(_ => LoadCategories());
            LoadPatternsCommand = new BA.UI.Mvvm.RelayCommand(_ => LoadPatterns());
            RandomColorsCommand = new BA.UI.Mvvm.RelayCommand(_ => RandomizeBucketColors(), _ => Buckets.Count > 0);
            GradientCommand = new BA.UI.Mvvm.RelayCommand(_ => ApplyGradient(), _ => Buckets.Count >= 2);
            AddBucketCommand = new BA.UI.Mvvm.RelayCommand(_ => AddManualBucket());
            RemoveBucketCommand = new BA.UI.Mvvm.RelayCommand(p => RemoveBucket(p as ColorBucketItem), p => p is ColorBucketItem);
            EditBucketColorCommand = new BA.UI.Mvvm.RelayCommand(p => EditBucketColor(p as ColorBucketItem), p => p is ColorBucketItem);
            SaveSchemeCommand = new BA.UI.Mvvm.RelayCommand(_ => SaveScheme(), _ => Buckets.Count > 0);
            LoadSchemeCommand = new BA.UI.Mvvm.RelayCommand(_ => LoadSelectedScheme(), _ => SelectedSavedScheme != null);
            CreateViewFiltersCommand = new BA.UI.Mvvm.RelayCommand(_ => CreateViewFilters(), _ => SelectedViewTemplate != null && Buckets.Count > 0);
            CreateLegendCommand = new BA.UI.Mvvm.RelayCommand(_ => CreateLegendFromRule(), _ => Buckets.Count > 0);
            ApplyToSelectionCommand = new BA.UI.Mvvm.RelayCommand(_ => ApplyOverridesToSelection(), _ => Buckets.Count > 0);
            ApplyToAllInViewCommand = new BA.UI.Mvvm.RelayCommand(_ => ApplyOverridesToAllInView(), _ => Buckets.Count > 0);

            AddSchemeToTemplateCommand = new BA.UI.Mvvm.RelayCommand(_ => AddSchemeToTemplate(), _ => SelectedViewTemplate != null && SelectedSavedScheme != null);
            CreateLegendFromSelectedCommand = new BA.UI.Mvvm.RelayCommand(_ => CreateLegendFromSelected(), _ => TemplateFilters.Any(r => r.IsCheckedForLegend));

            SaveGroupFromSelectedCommand = new BA.UI.Mvvm.RelayCommand(_ => SaveGroupFromSelected(), _ => TemplateFilters.Any(r => r.IsCheckedForLegend));
            ShowGroupOnTemplateCommand = new BA.UI.Mvvm.RelayCommand(_ => ShowGroupOnTemplate(), _ => SelectedViewTemplate != null && SelectedFilterGroup != null);
            HideGroupOnTemplateCommand = new BA.UI.Mvvm.RelayCommand(_ => HideGroupOnTemplate(), _ => SelectedViewTemplate != null && SelectedFilterGroup != null);
            CreateLegendFromGroupCommand = new BA.UI.Mvvm.RelayCommand(_ => CreateLegendFromGroup(), _ => SelectedViewTemplate != null && SelectedFilterGroup != null);
            ToggleLegendCheckCommand = new BA.UI.Mvvm.RelayCommand(p => ToggleLegendCheck(p as TemplateFilterRowItem), p => p is TemplateFilterRowItem);
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

        public void EnsureFillPatternsLoaded()
        {
            if (Patterns.Count == 0)
                LoadPatternsCommand.Execute(null);
        }

        public void EnsureSchemesLoaded()
        {
            if (SavedSchemes.Count == 0)
                LoadSavedSchemes();
        }

        public void EnsureFilterGroupsLoaded()
        {
            if (FilterGroups.Count == 0)
                LoadFilterGroups();
        }

        public void Dispose()
        {
            if (_paletteWindow != null)
            {
                _paletteWindow.Close();
                _paletteWindow = null;
            }
        }

        private void RaiseCanExecChanged()
        {
            LoadFiltersCommand.RaiseCanExecuteChanged();
            PreviewFilterCommand.RaiseCanExecuteChanged();
            ApplyToTemplateCommand.RaiseCanExecuteChanged();
            AutoAssignCommand.RaiseCanExecuteChanged();
            ExportPaletteCommand.RaiseCanExecuteChanged();
            CreateViewFiltersCommand?.RaiseCanExecuteChanged();
            AddSchemeToTemplateCommand?.RaiseCanExecuteChanged();
            ShowGroupOnTemplateCommand?.RaiseCanExecuteChanged();
            HideGroupOnTemplateCommand?.RaiseCanExecuteChanged();
            CreateLegendFromGroupCommand?.RaiseCanExecuteChanged();
            CreateLegendFromSelectedCommand?.RaiseCanExecuteChanged(); // <- NEW
            SaveGroupFromSelectedCommand?.RaiseCanExecuteChanged();    // <- NEW
        }

        private void RaiseParamColorCanExecChanged()
        {
            RandomColorsCommand.RaiseCanExecuteChanged();
            GradientCommand.RaiseCanExecuteChanged();
            SaveSchemeCommand.RaiseCanExecuteChanged();
            CreateViewFiltersCommand.RaiseCanExecuteChanged();
            CreateLegendCommand.RaiseCanExecuteChanged();
            ApplyToSelectionCommand.RaiseCanExecuteChanged();
            ApplyToAllInViewCommand.RaiseCanExecuteChanged();
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
        private void ToggleLegendCheck(TemplateFilterRowItem row)
        {
            if (row == null) return;

            // Corrected. VfLegendCheckTemplate uses a Button, not a CheckBox, so
            // there is no TwoWay IsChecked binding anywhere flipping this property
            // automatically, the button's Click only ever reaches this command. The
            // previous comment here described CheckBox behavior that was never
            // actually wired up for this Button based implementation. // <- CHANGED
            row.IsCheckedForLegend = !row.IsCheckedForLegend;
            RaiseLegendSelectionCanExecChanged();
        }

        private void OnLegendCheckChanged(object sender, EventArgs e)
        {
            RaiseLegendSelectionCanExecChanged();
        }

        private void RaiseLegendSelectionCanExecChanged()
        {
            CreateLegendFromSelectedCommand?.RaiseCanExecuteChanged();
            SaveGroupFromSelectedCommand?.RaiseCanExecuteChanged();
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
                    TemplateFilters.Clear();

                    foreach (var f in filters)
                    {
                        bool isBaManaged = f.Name.StartsWith("BA_", StringComparison.OrdinalIgnoreCase);

                        var cut = (f.CutR.HasValue && f.CutG.HasValue && f.CutB.HasValue)
                            ? Color.FromRgb(f.CutR.Value, f.CutG.Value, f.CutB.Value)
                            : (Color?)null;

                        var proj = (f.ProjR.HasValue && f.ProjG.HasValue && f.ProjB.HasValue)
                            ? Color.FromRgb(f.ProjR.Value, f.ProjG.Value, f.ProjB.Value)
                            : (Color?)null;

                        var row = new TemplateFilterRowItem(f.FilterId, f.Name, f.CategorySummary, f.IsVisible, isBaManaged)
                        {
                            CutBrush = cut.HasValue ? new SolidColorBrush(cut.Value) : new SolidColorBrush(Colors.Transparent),
                            ProjectionBrush = proj.HasValue ? new SolidColorBrush(proj.Value) : new SolidColorBrush(Colors.Transparent)
                        };

                        row.LegendCheckChanged += OnLegendCheckChanged;
                        TemplateFilters.Add(row);
                    }

                    // No longer auto-selecting the first loaded filter. With two grids now
                    // owning independent selection state, picking a single "first" item and
                    // trying to route it into the correct one of two properties adds
                    // complexity for no real benefit, the user clicks what they want. // <- CHANGED
                    SelectedNativeFilterRow = null;
                    SelectedBaManagedFilterRow = null;
                    SelectedTemplateFilterRow = null;

                    int baCount = TemplateFilters.Count(r => r.IsBaManaged);
                    StatusText = $"Loaded {TemplateFilters.Count} filter(s), {baCount} BA managed, {TemplateFilters.Count - baCount} native.";
                    RaiseCanExecChanged();
                },
                ex => StatusText = "Filter load failed: " + ex.Message
            );
        }

        private void PreviewSelectedFilter()
        {
            if (SelectedTemplateFilterRow == null) return;

            var row = SelectedTemplateFilterRow;
            TaskDialog.Show("BA | Filter Preview",
                $"Filter: {row.FilterName}\n" +
                $"Categories: {row.CategorySummary}\n" +
                $"Visible: {row.VisibleText}\n" +
                $"Managed by: {(row.IsBaManaged ? "BA Tools" : "Revit (native)")}");
        }

        private void AutoAssignPalette()
        {
            if (Palette.Count == 0 || TemplateFilters.Count == 0) return;

            for (int i = 0; i < TemplateFilters.Count; i++)
            {
                var c = Palette[i % Palette.Count].Color;
                TemplateFilters[i].ProjectionBrush = new SolidColorBrush(c);
                TemplateFilters[i].CutBrush = new SolidColorBrush(c);
            }

            StatusText = $"Auto-assigned palette to {TemplateFilters.Count} filters (preview only).";
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

            foreach (var row in TemplateFilters)
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

        private void PickCutColor(TemplateFilterRowItem row)
        {
            if (row == null) return;

            var current = (row.CutBrush as SolidColorBrush)?.Color ?? Colors.White;
            if (ColorPicker.TryPickColor(current, out var picked))
            {
                row.CutBrush = new SolidColorBrush(picked);
                StatusText = $"Cut color set: {row.FilterName}";
            }
        }

        private void PickProjColor(TemplateFilterRowItem row)
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
                "View Template tab:\n" +
                "1) Pick a template, Load Filters\n" +
                "2) Left pane = filters native to Revit, right pane = filters created by BA Tools\n" +
                "3) Click a Cut/Projection swatch to recolor a filter, Apply writes it to the template\n" +
                "4) Pick a saved scheme and click 'Apply Scheme to Template' to generate BA filters from it\n" +
                "5) Check filters in either pane, name a group, and 'Save Group From Selected' to make a reusable combined filter\n" +
                "6) Pick a saved group and Show/Hide it on the current template, or 'Create Legend From Group'\n" +
                "7) Palette button opens the swatch palette for Auto-Assign\n\n" +
                "Parameter Colors tab:\n" +
                "1) Pick a Category, then a Parameter\n" +
                "2) Pick Value or Range, buckets auto generate from the model\n" +
                "3) Edit colors, patterns, labels, and range breakpoints as needed\n" +
                "4) Name it and Save Scheme to add it to the library, or Create View Filters / Create Legend / Apply to Selection / Apply to All in View directly\n\n" +
                "Notes:\n. Sets Cut + Projection LINE colors and matching FILL colors and patterns.\n. Palette JSON import/export supported from the Palette window.\n. A 'group' is a saved list of filter names, Show/Hide toggles visibility of whichever of those filters exist on the current template.",
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

        private void LoadPatterns()
        {
            StatusText = "Loading fill patterns...";

            _revit.Run(
                app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    return ParameterEnumerationService.GetAvailableFillPatterns(doc);
                },
                pats =>
                {
                    Patterns.Clear();
                    Patterns.Add(SolidPatternEntry);
                    foreach (var p in pats)
                        Patterns.Add(p);

                    StatusText = $"Loaded {Patterns.Count - 1} fill pattern(s).";
                },
                ex => StatusText = "Fill pattern load failed: " + ex.Message
            );
        }

        private void LoadSavedSchemes()
        {
            StatusText = "Loading saved color schemes...";

            _revit.Run(
                app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    if (doc == null) return Array.Empty<SchemeSummary>();
                    return ColorSchemeLibraryService.ListSchemes(doc).ToArray();
                },
                schemes =>
                {
                    SavedSchemes.Clear();
                    foreach (var s in schemes)
                        SavedSchemes.Add(s);

                    SelectedSavedScheme = SavedSchemes.FirstOrDefault();
                    StatusText = $"Loaded {SavedSchemes.Count} saved scheme(s).";
                },
                ex => StatusText = "Loading saved schemes failed: " + ex.Message
            );
        }

        // New. Read-only enumeration, still routed through _revit.Run for the same
        // reason LoadSavedSchemes is: FilterGroupLibraryService's folder resolution
        // goes through ProjectSetService.GetProjectSetName, which touches the Revit
        // API and needs the Revit thread. // <- NEW
        private void LoadFilterGroups()
        {
            StatusText = "Loading saved filter groups...";

            _revit.Run(
                app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    if (doc == null) return Array.Empty<FilterGroupSummary>();
                    return FilterGroupLibraryService.ListGroups(doc).ToArray();
                },
                groups =>
                {
                    FilterGroups.Clear();
                    foreach (var g in groups)
                        FilterGroups.Add(g);

                    SelectedFilterGroup = FilterGroups.FirstOrDefault();
                    StatusText = $"Loaded {FilterGroups.Count} filter group(s).";
                },
                ex => StatusText = "Loading filter groups failed: " + ex.Message
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
            bool scopeToView = ScopeBucketsToActiveView; // <- NEW

            StatusText = "Scanning model for parameter values...";

            if (method == ProcessMethod.ValueBucket)
            {
                _revit.Run(
                    app =>
                    {
                        var doc = app.ActiveUIDocument?.Document;
                        var viewId = scopeToView ? doc?.ActiveView?.Id : null; // <- NEW
                        return ParameterEnumerationService.DiscoverDistinctValues(doc, categoryId, paramInfo, viewId);
                    },
                    distinct =>
                    {
                        if (distinct.Count == 0)
                        {
                            StatusText = scopeToView
                                ? "No elements with a value for this parameter were found in the active view."
                                : "No elements with a value for this parameter were found.";
                            return;
                        }

                        var coreBuckets = ParameterEnumerationService.BuildValueBuckets(distinct);
                        Buckets.Clear();
                        foreach (var b in coreBuckets)
                        {
                            var item = ColorBucketItem.FromCore(b);
                            item.SelectedPattern = SolidPatternEntry;
                            Buckets.Add(item);
                        }

                        _currentMethod = ProcessMethod.ValueBucket;
                        StatusText = $"Generated {Buckets.Count} value bucket(s){(scopeToView ? " from the active view" : "")}.";
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
                        var viewId = scopeToView ? doc?.ActiveView?.Id : null; // <- NEW
                        return ParameterEnumerationService.DiscoverRangeExtent(doc, categoryId, paramInfo, viewId);
                    },
                    extent =>
                    {
                        var coreBuckets = ParameterEnumerationService.BuildDefaultRangeBuckets(extent.Min, extent.Max);
                        Buckets.Clear();
                        foreach (var b in coreBuckets)
                        {
                            var item = ColorBucketItem.FromCore(b);
                            item.SelectedPattern = SolidPatternEntry;
                            Buckets.Add(item);
                        }

                        _currentMethod = ProcessMethod.RangeBucket;
                        StatusText = $"Generated {Buckets.Count} range bucket(s){(scopeToView ? " from the active view" : "")}, observed {extent.Min:0.##} to {extent.Max:0.##}.";
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
            var item = new ColorBucketItem
            {
                SelectedPattern = SolidPatternEntry
            };

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

        private void AddSchemeToTemplate()
        {
            if (SelectedViewTemplate == null)
            {
                StatusText = "Select a view template first.";
                return;
            }

            if (SelectedSavedScheme == null)
            {
                StatusText = "Select a saved color scheme first.";
                return;
            }

            var templateId = SelectedViewTemplate.Id;
            var fileName = SelectedSavedScheme.FileName;
            var schemeName = SelectedSavedScheme.SchemeName;

            StatusText = $"Applying scheme '{schemeName}' to template...";

            _revit.Run(
                app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    if (doc == null) return (Count: 0, Error: "No active document.");

                    SchemeDto dto;
                    try { dto = ColorSchemeLibraryService.LoadScheme(doc, fileName); }
                    catch (Exception ex) { return (Count: 0, Error: ex.Message); }

                    var categories = ParameterEnumerationService.GetFilterableCategories(doc);
                    var cat = categories.FirstOrDefault(c => c.Name.Equals(dto.CategoryName, StringComparison.OrdinalIgnoreCase));
                    if (cat == null)
                        return (Count: 0, Error: $"Category '{dto.CategoryName}' was not found or is not filterable in this document.");

                    var pars = ParameterEnumerationService.GetFilterableParameters(doc, cat.Id);
                    var param = pars.FirstOrDefault(p => p.Name.Equals(dto.ParameterName, StringComparison.OrdinalIgnoreCase));
                    if (param == null)
                        return (Count: 0, Error: $"Parameter '{dto.ParameterName}' was not found for category '{dto.CategoryName}' in this document.");

                    var patterns = ParameterEnumerationService.GetAvailableFillPatterns(doc);

                    var method = dto.Method == ProcessMethod.RangeBucket.ToString()
                        ? ProcessMethod.RangeBucket
                        : ProcessMethod.ValueBucket;

                    var rule = new ParameterColorRule
                    {
                        CategoryId = cat.Id,
                        CategoryName = cat.Name,
                        ParameterId = param.Id,
                        ParameterName = param.Name,
                        StorageType = param.StorageType,
                        IsInstance = param.IsInstance,
                        Method = method,
                        Buckets = dto.Buckets.Select(b =>
                        {
                            var pattern = patterns.FirstOrDefault(p => p.Name.Equals(b.PatternName, StringComparison.OrdinalIgnoreCase));
                            return new ColorBucket
                            {
                                Label = b.Label,
                                Value = b.Value,
                                RangeMin = b.RangeMin,
                                RangeMax = b.RangeMax,
                                R = b.R,
                                G = b.G,
                                B = b.B,
                                FillPatternId = pattern?.Id ?? ElementId.InvalidElementId
                            };
                        }).ToList()
                    };

                    using (var t = new Transaction(doc, "BA | Apply Saved Scheme To Template"))
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
                        return (Count: count, Error: (string)null);
                    }
                },
                result =>
                {
                    if (!string.IsNullOrEmpty(result.Error))
                    {
                        StatusText = $"Apply scheme failed: {result.Error}";
                        return;
                    }

                    StatusText = $"Applied scheme '{schemeName}', created/updated {result.Count} filter(s).";
                    LoadFilters();
                },
                ex => StatusText = "Apply scheme failed: " + ex.Message
            );
        }

        private void CreateLegendFromSelected()
        {
            var checkedRows = TemplateFilters.Where(r => r.IsCheckedForLegend).ToList();

            if (checkedRows.Count == 0)
            {
                StatusText = "Check at least one filter in either pane to include on the legend.";
                return;
            }

            if (SelectedViewTemplate == null)
            {
                StatusText = "Select a view template first.";
                return;
            }

            var templateId = SelectedViewTemplate.Id;
            var filterIds = checkedRows.Select(r => r.FilterId).ToList();
            var title = SelectedViewTemplate.Name;

            StatusText = "Creating legend from selected filters...";

            _revit.Run(
                app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    if (doc == null)
                        return (LegendId: ElementId.InvalidElementId, EntryCount: 0, SkippedCount: filterIds.Count, Error: "No active document.");

                    var template = doc.GetElement(templateId) as View;
                    if (template == null || !template.IsTemplate)
                        return (LegendId: ElementId.InvalidElementId, EntryCount: 0, SkippedCount: filterIds.Count, Error: "Selected element is not a valid view template.");

                    var entries = filterIds
                        .Select(id => ViewFilterColorManagerService.BuildLegendEntryFromFilter(doc, template, id))
                        .Where(e => e != null)
                        .ToList();

                    int skippedCount = filterIds.Count - entries.Count;
                    if (entries.Count == 0)
                        return (LegendId: ElementId.InvalidElementId, EntryCount: 0, SkippedCount: skippedCount,
                            Error: "None of the selected filters could be converted into legend entries.");

                    using (var t = new Transaction(doc, "BA | Create Legend From Selected Filters"))
                    {
                        t.Start();
                        ElementId legendId;
                        try
                        {
                            legendId = LegendGenerationService.CreateLegendFromEntries(doc, title, entries);
                            t.Commit();
                        }
                        catch
                        {
                            t.RollBack();
                            throw;
                        }

                        return (LegendId: legendId, EntryCount: entries.Count, SkippedCount: skippedCount, Error: (string)null);
                    }
                },
                result =>
                {
                    if (!string.IsNullOrEmpty(result.Error))
                    {
                        StatusText = "Legend creation failed: " + result.Error;
                        return;
                    }

                    StatusText = result.SkippedCount == 0
                        ? $"Legend created from {result.EntryCount} selected filter(s)."
                        : $"Legend created from {result.EntryCount} selected filter(s); {result.SkippedCount} unsupported filter(s) were skipped.";
                },
                ex => StatusText = "Legend creation failed: " + ex.Message
            );
        }

        // New. Saves whatever is currently checked, across both panes, as a named
        // group. Filter names are what's stored, not ElementIds, matching how the
        // group has to resolve against templates it hasn't been applied to yet. // <- NEW
        private void SaveGroupFromSelected()
        {
            var checkedRows = TemplateFilters.Where(r => r.IsCheckedForLegend).ToList();

            if (checkedRows.Count == 0)
            {
                StatusText = "Check at least one filter in either pane before saving a group.";
                return;
            }

            string groupName = string.IsNullOrWhiteSpace(NewGroupName)
                ? $"Group_{DateTime.Now:yyyyMMdd_HHmmss}"
                : NewGroupName.Trim();

            var dto = new FilterGroupDto
            {
                GroupName = groupName,
                FilterNames = checkedRows.Select(r => r.FilterName).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };

            StatusText = $"Saving group '{groupName}'...";

            _revit.Run(
                app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    if (doc == null) throw new InvalidOperationException("No active document.");
                    return FilterGroupLibraryService.SaveGroup(doc, dto);
                },
                fileName =>
                {
                    StatusText = $"Group '{groupName}' saved with {dto.FilterNames.Count} filter(s).";
                    NewGroupName = string.Empty;
                    LoadFilterGroups();
                },
                ex => StatusText = "Save group failed: " + ex.Message
            );
        }

        // New. Sets Visible = true (via SetFilterVisibility) for every member filter
        // of the selected group that is actually present on the current template.
        // Members not present are skipped, not added, silently, that's a deliberate
        // choice: a group is a visibility toggle for what's already there, not
        // another way to materialize filters (that's Apply Scheme to Template). // <- NEW
        private void ShowGroupOnTemplate() => SetGroupVisibilityOnTemplate(true);

        private void HideGroupOnTemplate() => SetGroupVisibilityOnTemplate(false);

        private void SetGroupVisibilityOnTemplate(bool visible)
        {
            if (SelectedViewTemplate == null)
            {
                StatusText = "Select a view template first.";
                return;
            }

            if (SelectedFilterGroup == null)
            {
                StatusText = "Select a saved filter group first.";
                return;
            }

            var templateId = SelectedViewTemplate.Id;
            var fileName = SelectedFilterGroup.FileName;
            var groupName = SelectedFilterGroup.GroupName;

            StatusText = $"{(visible ? "Showing" : "Hiding")} group '{groupName}' on template...";

            _revit.Run(
                app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    if (doc == null) return (Matched: 0, Missing: 0, Error: "No active document.");

                    FilterGroupDto dto;
                    try { dto = FilterGroupLibraryService.LoadGroup(doc, fileName); }
                    catch (Exception ex) { return (Matched: 0, Missing: 0, Error: ex.Message); }

                    var template = doc.GetElement(templateId) as View;
                    if (template == null || !template.IsTemplate)
                        return (Matched: 0, Missing: 0, Error: "Selected element is not a valid view template.");

                    var templateFilterIds = template.GetFilters() ?? new List<ElementId>();

                    var nameToId = templateFilterIds
                        .Select(id => (Id: id, Elem: doc.GetElement(id)))
                        .Where(x => x.Elem != null && !string.IsNullOrWhiteSpace(x.Elem.Name))
                        .GroupBy(x => x.Elem.Name, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

                    int matched = 0;
                    int missing = 0;

                    using (var t = new Transaction(doc, visible ? "BA | Show Filter Group" : "BA | Hide Filter Group"))
                    {
                        t.Start();
                        try
                        {
                            foreach (var name in dto.FilterNames)
                            {
                                if (nameToId.TryGetValue(name, out var id))
                                {
                                    template.SetFilterVisibility(id, visible);
                                    matched++;
                                }
                                else
                                {
                                    missing++;
                                }
                            }
                            t.Commit();
                        }
                        catch
                        {
                            t.RollBack();
                            throw;
                        }
                    }

                    return (Matched: matched, Missing: missing, Error: (string)null);
                },
                result =>
                {
                    if (!string.IsNullOrEmpty(result.Error))
                    {
                        StatusText = $"{(visible ? "Show" : "Hide")} group failed: {result.Error}";
                        return;
                    }

                    StatusText = result.Missing == 0
                        ? $"Group '{groupName}' {(visible ? "shown" : "hidden")}, {result.Matched} filter(s) affected."
                        : $"Group '{groupName}' {(visible ? "shown" : "hidden")}, {result.Matched} filter(s) affected, {result.Missing} not present on this template.";

                    LoadFilters();
                },
                ex => StatusText = $"{(visible ? "Show" : "Hide")} group failed: " + ex.Message
            );
        }

        // New. Same entry-building path as CreateLegendFromSelected, except the
        // filter list comes from a saved group's names resolved against the
        // current template instead of checked rows in the grid. // <- NEW
        private void CreateLegendFromGroup()
        {
            if (SelectedViewTemplate == null)
            {
                StatusText = "Select a view template first.";
                return;
            }

            if (SelectedFilterGroup == null)
            {
                StatusText = "Select a saved filter group first.";
                return;
            }

            var templateId = SelectedViewTemplate.Id;
            var fileName = SelectedFilterGroup.FileName;
            var groupName = SelectedFilterGroup.GroupName;

            StatusText = $"Creating legend from group '{groupName}'...";

            _revit.Run(
                app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    if (doc == null)
                        return (LegendId: ElementId.InvalidElementId, EntryCount: 0, Missing: 0, Unsupported: 0, Error: "No active document.");

                    FilterGroupDto dto;
                    try
                    {
                        dto = FilterGroupLibraryService.LoadGroup(doc, fileName);
                    }
                    catch (Exception ex)
                    {
                        return (LegendId: ElementId.InvalidElementId, EntryCount: 0, Missing: 0, Unsupported: 0, Error: ex.Message);
                    }

                    var template = doc.GetElement(templateId) as View;
                    if (template == null || !template.IsTemplate)
                        return (LegendId: ElementId.InvalidElementId, EntryCount: 0, Missing: 0, Unsupported: 0,
                            Error: "Selected element is not a valid view template.");

                    var templateFilterIds = template.GetFilters() ?? new List<ElementId>();

                    var nameToId = templateFilterIds
                        .Select(id => (Id: id, Elem: doc.GetElement(id)))
                        .Where(x => x.Elem != null && !string.IsNullOrWhiteSpace(x.Elem.Name))
                        .GroupBy(x => x.Elem.Name, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

                    var entries = new List<LegendEntry>();
                    int missing = 0;
                    int unsupported = 0;

                    foreach (var name in dto.FilterNames)
                    {
                        if (!nameToId.TryGetValue(name, out var id))
                        {
                            missing++;
                            continue;
                        }

                        var entry = ViewFilterColorManagerService.BuildLegendEntryFromFilter(doc, template, id);
                        if (entry != null)
                            entries.Add(entry);
                        else
                            unsupported++;
                    }

                    if (entries.Count == 0)
                        return (LegendId: ElementId.InvalidElementId, EntryCount: 0, Missing: missing, Unsupported: unsupported,
                            Error: "None of this group's filters could be converted into legend entries for the selected template.");

                    using (var t = new Transaction(doc, "BA | Create Legend From Group"))
                    {
                        t.Start();
                        ElementId legendId;
                        try
                        {
                            legendId = LegendGenerationService.CreateLegendFromEntries(doc, groupName, entries);
                            t.Commit();
                        }
                        catch
                        {
                            t.RollBack();
                            throw;
                        }

                        return (LegendId: legendId, EntryCount: entries.Count, Missing: missing, Unsupported: unsupported, Error: (string)null);
                    }
                },
                result =>
                {
                    if (!string.IsNullOrEmpty(result.Error))
                    {
                        StatusText = $"Legend from group failed: {result.Error}";
                        return;
                    }

                    var details = new List<string>();
                    if (result.Missing > 0)
                        details.Add($"{result.Missing} not present");
                    if (result.Unsupported > 0)
                        details.Add($"{result.Unsupported} unsupported");

                    StatusText = details.Count == 0
                        ? $"Legend created from group '{groupName}' with {result.EntryCount} entr{(result.EntryCount == 1 ? "y" : "ies")}."
                        : $"Legend created from group '{groupName}' with {result.EntryCount} entr{(result.EntryCount == 1 ? "y" : "ies")}; {string.Join(", ", details)} filter(s) skipped.";
                },
                ex => StatusText = "Legend from group failed: " + ex.Message
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

        private void SaveScheme()
        {
            if (SelectedCategory == null || SelectedParameter == null || Buckets.Count == 0)
            {
                StatusText = "Nothing to save yet.";
                return;
            }

            string schemeName = string.IsNullOrWhiteSpace(NewSchemeName)
                ? $"{SelectedCategory.Name}_{SelectedParameter.Name}"
                : NewSchemeName.Trim();

            var dto = new SchemeDto
            {
                SchemeName = schemeName,
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
                    B = b.B,
                    PatternName = b.SelectedPattern?.Name ?? SolidPatternEntry.Name
                }).ToList()
            };

            StatusText = $"Saving scheme '{schemeName}'...";

            _revit.Run(
                app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    if (doc == null) throw new InvalidOperationException("No active document.");
                    return ColorSchemeLibraryService.SaveScheme(doc, dto);
                },
                fileName =>
                {
                    StatusText = $"Scheme '{schemeName}' saved.";
                    NewSchemeName = string.Empty;
                    LoadSavedSchemes();
                },
                ex => StatusText = "Save failed: " + ex.Message
            );
        }

        private void LoadSelectedScheme()
        {
            if (SelectedSavedScheme == null)
            {
                StatusText = "Select a saved scheme first.";
                return;
            }

            var fileName = SelectedSavedScheme.FileName;

            StatusText = $"Loading scheme '{SelectedSavedScheme.SchemeName}'...";

            _revit.Run(
                app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    if (doc == null)
                        return (Dto: (SchemeDto)null, Category: (CategoryInfo)null, Parameters: Array.Empty<ParameterInfo>(), Error: "No active document.");

                    SchemeDto dto;
                    try { dto = ColorSchemeLibraryService.LoadScheme(doc, fileName); }
                    catch (Exception ex)
                    {
                        return (Dto: (SchemeDto)null, Category: (CategoryInfo)null, Parameters: Array.Empty<ParameterInfo>(), Error: ex.Message);
                    }

                    var categories = ParameterEnumerationService.GetFilterableCategories(doc);
                    var cat = categories.FirstOrDefault(c => c.Name.Equals(dto.CategoryName, StringComparison.OrdinalIgnoreCase));
                    if (cat == null)
                        return (Dto: dto, Category: (CategoryInfo)null, Parameters: Array.Empty<ParameterInfo>(),
                            Error: $"Category '{dto.CategoryName}' was not found or is not filterable in this document.");

                    var pars = ParameterEnumerationService.GetFilterableParameters(doc, cat.Id);
                    return (Dto: dto, Category: cat, Parameters: pars, Error: (string)null);
                },
                result =>
                {
                    if (!string.IsNullOrEmpty(result.Error))
                    {
                        StatusText = "Load failed: " + result.Error;
                        return;
                    }

                    var dto = result.Dto;

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
                        var resolvedPattern = Patterns.FirstOrDefault(p =>
                            p.Name.Equals(b.PatternName, StringComparison.OrdinalIgnoreCase)) ?? SolidPatternEntry;

                        Buckets.Add(new ColorBucketItem
                        {
                            Label = b.Label,
                            Value = b.Value,
                            RangeMin = b.RangeMin,
                            RangeMax = b.RangeMax,
                            R = b.R,
                            G = b.G,
                            B = b.B,
                            SelectedPattern = resolvedPattern
                        });
                    }

                    NewSchemeName = dto.SchemeName;

                    RaiseParamColorCanExecChanged();
                    StatusText = $"Loaded scheme '{dto.SchemeName}', {Buckets.Count} bucket(s).";
                },
                ex => StatusText = "Load failed: " + ex.Message
            );
        }

        private void OpenPalette()
        {
            if (_paletteWindow != null && _paletteWindow.IsLoaded)
            {
                _paletteWindow.Activate();
                return;
            }

            _paletteWindow = new PaletteWindow(this) { Owner = _window };
            _paletteWindow.Closed += (_, __) => _paletteWindow = null;
            _paletteWindow.Show();
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

    public sealed class TemplateFilterRowItem : BA.UI.Mvvm.ObservableObject
    {
        public ElementId FilterId { get; }
        public string FilterName { get; }
        public string CategorySummary { get; }
        public bool IsBaManaged { get; }

        private bool _visible;
        public bool Visible
        {
            get => _visible;
            set { if (SetProperty(ref _visible, value)) OnPropertyChanged(nameof(VisibleText)); }
        }

        public string VisibleText => Visible ? "Yes" : "No";

        public event EventHandler LegendCheckChanged;

        private bool _isCheckedForLegend;
        public bool IsCheckedForLegend
        {
            get => _isCheckedForLegend;
            set
            {
                if (SetProperty(ref _isCheckedForLegend, value))
                    LegendCheckChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private Brush _cutBrush = new SolidColorBrush(Colors.Transparent);
        public Brush CutBrush { get => _cutBrush; set => SetProperty(ref _cutBrush, value); }

        private Brush _projectionBrush = new SolidColorBrush(Colors.Transparent);
        public Brush ProjectionBrush { get => _projectionBrush; set => SetProperty(ref _projectionBrush, value); }

        public TemplateFilterRowItem(ElementId filterId, string filterName, string categorySummary, bool visible, bool isBaManaged)
        {
            FilterId = filterId;
            FilterName = filterName;
            CategorySummary = categorySummary;
            _visible = visible;
            IsBaManaged = isBaManaged;
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

        private FillPatternInfo _selectedPattern;
        public FillPatternInfo SelectedPattern { get => _selectedPattern; set => SetProperty(ref _selectedPattern, value); }

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
                B = B,
                FillPatternId = (SelectedPattern != null && SelectedPattern.Id != ElementId.InvalidElementId)
                    ? SelectedPattern.Id
                    : ElementId.InvalidElementId
            };
        }
    }
}
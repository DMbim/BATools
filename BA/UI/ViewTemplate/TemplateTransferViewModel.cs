using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.ViewTemplates;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace BA.UI.ViewTemplates
{
    public sealed class TemplateTransferViewModel : INotifyPropertyChanged
    {
        private readonly UIApplication _uiApp;
        private readonly Window _ownerWindow;
        private readonly ExternalEvent _applyExternalEvent;
        private readonly ApplyTemplatePropertiesHandler _applyHandler;

        private readonly List<CheckableViewTemplateItem> _allSourceTemplates = new();
        private readonly List<CheckableTemplatePropertyItem> _allProperties = new();
        private readonly List<CheckableViewTemplateItem> _allTargets = new();

        private CheckableViewTemplateItem? _selectedSourceTemplate;
        private string _sourceSearchText = string.Empty;
        private string _propertySearchText = string.Empty;
        private string _targetSearchText = string.Empty;
        private string _statusText = "Ready.";
        private bool _isBusy;
        private string _busyText = "Applying changes";
        private readonly ExternalEvent _applyCategoryGraphicsExternalEvent;
        private readonly ApplyCategoryGraphicsHandler _applyCategoryGraphicsHandler;

        private readonly List<CategoryTransferItem> _allGraphicCategories = new();

        private string _categorySearchText = string.Empty;
        private bool _copyCategoryVisibility = true;
        private bool _copyCategoryProjectionOverrides = true;
        private bool _copyCategoryCutOverrides = true;
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly ExternalEvent _applyViewFiltersExternalEvent;
        private readonly ApplyViewFiltersHandler _applyViewFiltersHandler;

        private readonly List<ViewFilterTransferItem> _allSourceFilters = new();

        private string _filterSearchText = string.Empty;
        private bool _copyFilterEnabledState = true;
        private bool _copyFilterVisibility = true;
        private bool _copyFilterOverrides = true;
        private bool _preserveFilterOrder = false;
        private bool _copyFilterTransparency = false;
        private bool _isAdvancedFilterOptionsVisible = false;

        // <- NEW: collapse state for the three optional panels, all hidden by default
        private bool _isPropertiesPanelVisible = false;
        private bool _isAdvancedGraphicsPanelVisible = false;
        private bool _isFilterPanelVisible = false;

        public ObservableCollection<CheckableViewTemplateItem> SourceTemplates { get; } = new();
        public ObservableCollection<CheckableViewTemplateItem> FilteredSourceTemplates { get; } = new();
        public ObservableCollection<CheckableTemplatePropertyItem> FilteredProperties { get; } = new();
        public ObservableCollection<CheckableViewTemplateItem> FilteredTargets { get; } = new();
        public ObservableCollection<CategoryTransferItem> FilteredGraphicCategories { get; } = new();

        public ObservableCollection<ViewFilterTransferItem> FilteredSourceFilters { get; } = new();

        // <- CHANGED: was SourceViewTypeOptions (single select dropdown model), now a checkable list
        public ObservableCollection<CheckableViewTypeItem> SourceViewTypeFilters { get; } = new();

        public BA.UI.Mvvm.RelayCommand ApplyViewFiltersCommand { get; }
        public BA.UI.Mvvm.RelayCommand SelectAllFiltersCommand { get; }
        public BA.UI.Mvvm.RelayCommand ClearAllFiltersCommand { get; }
        public BA.UI.Mvvm.RelayCommand InvertFiltersCommand { get; }
        public BA.UI.Mvvm.RelayCommand ToggleAdvancedFilterOptionsCommand { get; }

        // <- NEW: top bar panel toggles
        public BA.UI.Mvvm.RelayCommand TogglePropertiesPanelCommand { get; }
        public BA.UI.Mvvm.RelayCommand ToggleAdvancedGraphicsPanelCommand { get; }
        public BA.UI.Mvvm.RelayCommand ToggleFilterPanelCommand { get; }

        public BA.UI.Mvvm.RelayCommand ApplyCategoryGraphicsCommand { get; }
        public BA.UI.Mvvm.RelayCommand SelectAllGraphicCategoriesCommand { get; }
        public BA.UI.Mvvm.RelayCommand ClearAllGraphicCategoriesCommand { get; }
        public BA.UI.Mvvm.RelayCommand InvertGraphicCategoriesCommand { get; }
        public BA.UI.Mvvm.RelayCommand ApplyCommand { get; }
        public BA.UI.Mvvm.RelayCommand SelectAllPropertiesCommand { get; }
        public BA.UI.Mvvm.RelayCommand ClearAllPropertiesCommand { get; }
        public BA.UI.Mvvm.RelayCommand InvertPropertiesCommand { get; }
        public BA.UI.Mvvm.RelayCommand SelectAllTargetsCommand { get; }
        public BA.UI.Mvvm.RelayCommand ClearAllTargetsCommand { get; }
        public BA.UI.Mvvm.RelayCommand InvertTargetsCommand { get; }

        public TemplateTransferViewModel(UIApplication uiApp, Window ownerWindow)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _ownerWindow = ownerWindow ?? throw new ArgumentNullException(nameof(ownerWindow));

            _applyHandler = new ApplyTemplatePropertiesHandler();
            _applyExternalEvent = ExternalEvent.Create(_applyHandler);
            _applyViewFiltersHandler = new ApplyViewFiltersHandler();
            _applyViewFiltersExternalEvent = ExternalEvent.Create(_applyViewFiltersHandler);


            ApplyViewFiltersCommand = new BA.UI.Mvvm.RelayCommand(_ => ApplyViewFilters(), _ => CanApplyViewFilters());
            SelectAllFiltersCommand = new BA.UI.Mvvm.RelayCommand(_ => SelectAllFilters());
            ClearAllFiltersCommand = new BA.UI.Mvvm.RelayCommand(_ => ClearAllFilters());
            InvertFiltersCommand = new BA.UI.Mvvm.RelayCommand(_ => InvertFilters());
            ToggleAdvancedFilterOptionsCommand = new BA.UI.Mvvm.RelayCommand(_ => IsAdvancedFilterOptionsVisible = !IsAdvancedFilterOptionsVisible);
            TogglePropertiesPanelCommand = new BA.UI.Mvvm.RelayCommand(_ => IsPropertiesPanelVisible = !IsPropertiesPanelVisible); // <- NEW
            ToggleAdvancedGraphicsPanelCommand = new BA.UI.Mvvm.RelayCommand(_ => IsAdvancedGraphicsPanelVisible = !IsAdvancedGraphicsPanelVisible); // <- NEW
            ToggleFilterPanelCommand = new BA.UI.Mvvm.RelayCommand(_ => IsFilterPanelVisible = !IsFilterPanelVisible); // <- NEW
            ApplyCommand = new BA.UI.Mvvm.RelayCommand(_ => Apply(), _ => CanApply());
            SelectAllPropertiesCommand = new BA.UI.Mvvm.RelayCommand(_ => SelectAllProperties());
            ClearAllPropertiesCommand = new BA.UI.Mvvm.RelayCommand(_ => ClearAllProperties());
            InvertPropertiesCommand = new BA.UI.Mvvm.RelayCommand(_ => InvertProperties());
            SelectAllTargetsCommand = new BA.UI.Mvvm.RelayCommand(_ => SelectAllTargets());
            ClearAllTargetsCommand = new BA.UI.Mvvm.RelayCommand(_ => ClearAllTargets());
            InvertTargetsCommand = new BA.UI.Mvvm.RelayCommand(_ => InvertTargets());
            ApplyCategoryGraphicsCommand = new BA.UI.Mvvm.RelayCommand(_ => ApplyCategoryGraphics(), _ => CanApplyCategoryGraphics());
            SelectAllGraphicCategoriesCommand = new BA.UI.Mvvm.RelayCommand(_ => SelectAllGraphicCategories());
            ClearAllGraphicCategoriesCommand = new BA.UI.Mvvm.RelayCommand(_ => ClearAllGraphicCategories());
            InvertGraphicCategoriesCommand = new BA.UI.Mvvm.RelayCommand(_ => InvertGraphicCategories());


            LoadTemplates();
        }

        public CheckableViewTemplateItem? SelectedSourceTemplate
        {
            get => _selectedSourceTemplate;
            set
            {
                if (_selectedSourceTemplate == value) return;

                _selectedSourceTemplate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedSourceViewType));

                LoadFiltersForSelectedSource();
                LoadPropertiesForSelectedSource();
                LoadGraphicCategoriesForSelectedSource();
                RebuildTargets();
                UpdateCountsAndStatus();
                ApplyCommand.RaiseCanExecuteChanged();
            }
        }

        // <- NEW: collapse state for the Properties panel, toggled from the top bar
        public bool IsPropertiesPanelVisible
        {
            get => _isPropertiesPanelVisible;
            set
            {
                if (_isPropertiesPanelVisible == value) return;
                _isPropertiesPanelVisible = value;
                OnPropertyChanged();
            }
        }

        // <- NEW: collapse state for the Advanced Graphics panel, toggled from the top bar
        public bool IsAdvancedGraphicsPanelVisible
        {
            get => _isAdvancedGraphicsPanelVisible;
            set
            {
                if (_isAdvancedGraphicsPanelVisible == value) return;
                _isAdvancedGraphicsPanelVisible = value;
                OnPropertyChanged();
            }
        }

        // <- NEW: collapse state for the Filter transfer panel, toggled from the top bar
        public bool IsFilterPanelVisible
        {
            get => _isFilterPanelVisible;
            set
            {
                if (_isFilterPanelVisible == value) return;
                _isFilterPanelVisible = value;
                OnPropertyChanged();
            }
        }

        public bool IsAdvancedFilterOptionsVisible
        {
            get => _isAdvancedFilterOptionsVisible;
            set
            {
                if (_isAdvancedFilterOptionsVisible == value) return;
                _isAdvancedFilterOptionsVisible = value;
                OnPropertyChanged();
            }
        }

        public bool CopyFilterTransparency
        {
            get => _copyFilterTransparency;
            set
            {
                if (_copyFilterTransparency == value) return;
                _copyFilterTransparency = value;
                OnPropertyChanged();
                ApplyViewFiltersCommand.RaiseCanExecuteChanged();
            }
        }

        public string CategorySearchText
        {
            get => _categorySearchText;
            set
            {
                if (_categorySearchText == value) return;
                _categorySearchText = value ?? string.Empty;
                OnPropertyChanged();
                RebuildFilteredGraphicCategories();
            }
        }
        private void LoadFiltersForSelectedSource()
        {
            _allSourceFilters.Clear();
            FilteredSourceFilters.Clear();

            if (SelectedSourceTemplate == null)
            {
                OnPropertyChanged(nameof(SelectedFilterCount));
                ApplyViewFiltersCommand.RaiseCanExecuteChanged();
                return;
            }

            Document doc = _uiApp.ActiveUIDocument?.Document
                ?? throw new InvalidOperationException("No active Revit document.");

            List<ViewFilterTransferItem> filters =
                ViewTemplateFilterTransferService.GetAppliedFilters(doc, SelectedSourceTemplate.Id);

            foreach (ViewFilterTransferItem item in filters)
            {
                item.PropertyChanged += Item_PropertyChanged;
                _allSourceFilters.Add(item);
            }

            RebuildFilteredSourceFilters();
            OnPropertyChanged(nameof(SelectedFilterCount));
            ApplyViewFiltersCommand.RaiseCanExecuteChanged();
        }
        private void RebuildFilteredSourceFilters()
        {
            FilteredSourceFilters.Clear();

            IEnumerable<ViewFilterTransferItem> items = _allSourceFilters;

            string text = (_filterSearchText ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                items = items.Where(x =>
                    x.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    x.FilterTypeName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            foreach (ViewFilterTransferItem item in items)
                FilteredSourceFilters.Add(item);

            OnPropertyChanged(nameof(SelectedFilterCount));
        }
        private void SelectAllFilters()
        {
            foreach (ViewFilterTransferItem item in FilteredSourceFilters)
                item.IsSelected = true;

            OnPropertyChanged(nameof(SelectedFilterCount));
            ApplyViewFiltersCommand.RaiseCanExecuteChanged();
        }

        private void ClearAllFilters()
        {
            foreach (ViewFilterTransferItem item in FilteredSourceFilters)
                item.IsSelected = false;

            OnPropertyChanged(nameof(SelectedFilterCount));
            ApplyViewFiltersCommand.RaiseCanExecuteChanged();
        }

        private void InvertFilters()
        {
            foreach (ViewFilterTransferItem item in FilteredSourceFilters)
                item.IsSelected = !item.IsSelected;

            OnPropertyChanged(nameof(SelectedFilterCount));
            ApplyViewFiltersCommand.RaiseCanExecuteChanged();
        }
        private bool CanApplyViewFilters()
        {
            if (IsBusy) return false;
            if (SelectedSourceTemplate == null) return false;
            if (!_allTargets.Any(x => x.IsSelected)) return false;
            if (!_allSourceFilters.Any(x => x.IsSelected)) return false;
            if (!CopyFilterEnabledState && !CopyFilterVisibility && !CopyFilterOverrides && !CopyFilterTransparency) return false;
            return true;
        }
        private void LoadGraphicCategoriesForSelectedSource()
        {
            _allGraphicCategories.Clear();
            FilteredGraphicCategories.Clear();

            if (SelectedSourceTemplate == null)
            {
                OnPropertyChanged(nameof(SelectedGraphicCategoryCount));
                return;
            }

            Document doc = _uiApp.ActiveUIDocument?.Document
                ?? throw new InvalidOperationException("No active Revit document.");

            List<CategoryTransferItem> cats =
                ViewTemplateGraphicsTransferService.GetTransferableModelCategories(doc, SelectedSourceTemplate.Id);

            foreach (CategoryTransferItem cat in cats)
            {
                cat.PropertyChanged += Item_PropertyChanged;
                _allGraphicCategories.Add(cat);
            }

            RebuildFilteredGraphicCategories();
            OnPropertyChanged(nameof(SelectedGraphicCategoryCount));
            ApplyCategoryGraphicsCommand.RaiseCanExecuteChanged();
        }
        public bool CopyCategoryVisibility
        {
            get => _copyCategoryVisibility;
            set
            {
                if (_copyCategoryVisibility == value) return;
                _copyCategoryVisibility = value;
                OnPropertyChanged();
                ApplyCategoryGraphicsCommand.RaiseCanExecuteChanged();
            }
        }
        private void ApplyViewFilters()
        {
            if (SelectedSourceTemplate == null)
            {
                MessageBox.Show(_ownerWindow,
                    "Please select a source template.",
                    "View Filter Transfer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            List<ElementId> selectedTargetIds = _allTargets
                .Where(x => x.IsSelected)
                .Select(x => x.Id)
                .ToList();

            List<ElementId> selectedFilterIds = _allSourceFilters
                .Where(x => x.IsSelected)
                .Select(x => x.FilterId)
                .ToList();

            if (selectedTargetIds.Count == 0)
            {
                MessageBox.Show(_ownerWindow,
                    "Please select at least one target template.",
                    "View Filter Transfer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (selectedFilterIds.Count == 0)
            {
                MessageBox.Show(_ownerWindow,
                    "Please select at least one filter.",
                    "View Filter Transfer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!CopyFilterEnabledState && !CopyFilterVisibility && !CopyFilterOverrides && !CopyFilterTransparency)
            {
                MessageBox.Show(_ownerWindow,
                    "Enable at least one transfer option: Enabled State, Visibility, Overrides, or Transparency.",
                    "View Filter Transfer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            IsBusy = true;
            BusyText = "Applying view filters";

            var request = new ApplyViewFiltersRequest
            {
                SourceTemplateId = SelectedSourceTemplate.Id,
                CopyEnabledState = CopyFilterEnabledState,
                CopyVisibility = CopyFilterVisibility,
                CopyOverrides = CopyFilterOverrides,
                CopyTransparency = CopyFilterTransparency,
                PreserveOrder = PreserveFilterOrder,
                OnSuccess = result =>
                {
                    _ownerWindow.Dispatcher.Invoke(() =>
                    {
                        IsBusy = false;

                        string summary =
                            $"Source: {result.SourceTemplateName}\n" +
                            $"Requested filters: {result.RequestedFilters}\n" +
                            $"Updated targets: {result.UpdatedTargets}\n" +
                            $"Skipped targets: {result.SkippedTargets}\n\n" +
                            string.Join("\n", result.Messages.Take(40));

                        if (result.Messages.Count > 40)
                            summary += $"\n\n... and {result.Messages.Count - 40} more messages.";

                        StatusText = $"View filters applied to {result.UpdatedTargets} target template(s).";

                        MessageBox.Show(_ownerWindow,
                            summary,
                            "View Filter Transfer Result",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    });
                },
                OnError = ex =>
                {
                    _ownerWindow.Dispatcher.Invoke(() =>
                    {
                        IsBusy = false;
                        MessageBox.Show(_ownerWindow,
                            ex.ToString(),
                            "View Filter Transfer Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    });
                }
            };

            request.TargetTemplateIds.AddRange(selectedTargetIds);
            request.SelectedFilterIds.AddRange(selectedFilterIds);

            _applyViewFiltersHandler.Raise(request);
            _applyViewFiltersExternalEvent.Raise();
        }
        private void RebuildFilteredGraphicCategories()
        {
            FilteredGraphicCategories.Clear();

            IEnumerable<CategoryTransferItem> items = _allGraphicCategories;

            string text = (_categorySearchText ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                items = items.Where(x =>
                    x.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            foreach (CategoryTransferItem item in items.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                FilteredGraphicCategories.Add(item);

            OnPropertyChanged(nameof(SelectedGraphicCategoryCount));
        }
        public bool CopyCategoryProjectionOverrides
        {
            get => _copyCategoryProjectionOverrides;
            set
            {
                if (_copyCategoryProjectionOverrides == value) return;
                _copyCategoryProjectionOverrides = value;
                OnPropertyChanged();
                ApplyCategoryGraphicsCommand.RaiseCanExecuteChanged();
            }
        }
        private void SelectAllGraphicCategories()
        {
            foreach (CategoryTransferItem item in FilteredGraphicCategories)
                item.IsSelected = true;

            OnPropertyChanged(nameof(SelectedGraphicCategoryCount));
            ApplyCategoryGraphicsCommand.RaiseCanExecuteChanged();
        }

        private void ClearAllGraphicCategories()
        {
            foreach (CategoryTransferItem item in FilteredGraphicCategories)
                item.IsSelected = false;

            OnPropertyChanged(nameof(SelectedGraphicCategoryCount));
            ApplyCategoryGraphicsCommand.RaiseCanExecuteChanged();
        }

        private void InvertGraphicCategories()
        {
            foreach (CategoryTransferItem item in FilteredGraphicCategories)
                item.IsSelected = !item.IsSelected;

            OnPropertyChanged(nameof(SelectedGraphicCategoryCount));
            ApplyCategoryGraphicsCommand.RaiseCanExecuteChanged();
        }
        public bool CopyCategoryCutOverrides
        {
            get => _copyCategoryCutOverrides;
            set
            {
                if (_copyCategoryCutOverrides == value) return;
                _copyCategoryCutOverrides = value;
                OnPropertyChanged();
                ApplyCategoryGraphicsCommand.RaiseCanExecuteChanged();
            }
        }
        public string FilterSearchText
        {
            get => _filterSearchText;
            set
            {
                if (_filterSearchText == value) return;
                _filterSearchText = value ?? string.Empty;
                OnPropertyChanged();
                RebuildFilteredSourceFilters();
            }
        }

        public bool CopyFilterEnabledState
        {
            get => _copyFilterEnabledState;
            set
            {
                if (_copyFilterEnabledState == value) return;
                _copyFilterEnabledState = value;
                OnPropertyChanged();
                ApplyViewFiltersCommand.RaiseCanExecuteChanged();
            }
        }

        public bool CopyFilterVisibility
        {
            get => _copyFilterVisibility;
            set
            {
                if (_copyFilterVisibility == value) return;
                _copyFilterVisibility = value;
                OnPropertyChanged();
                ApplyViewFiltersCommand.RaiseCanExecuteChanged();
            }
        }

        public bool CopyFilterOverrides
        {
            get => _copyFilterOverrides;
            set
            {
                if (_copyFilterOverrides == value) return;
                _copyFilterOverrides = value;
                OnPropertyChanged();
                ApplyViewFiltersCommand.RaiseCanExecuteChanged();
            }
        }

        public bool PreserveFilterOrder
        {
            get => _preserveFilterOrder;
            set
            {
                if (_preserveFilterOrder == value) return;
                _preserveFilterOrder = value;
                OnPropertyChanged();
            }
        }

        public int SelectedFilterCount => _allSourceFilters.Count(x => x.IsSelected);
        private void ApplyCategoryGraphics()
        {
            if (SelectedSourceTemplate == null)
            {
                MessageBox.Show(_ownerWindow,
                    "Please select a source template.",
                    "Advanced Graphics Transfer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            List<ElementId> selectedTargetIds = _allTargets
                .Where(x => x.IsSelected)
                .Select(x => x.Id)
                .ToList();

            List<ElementId> selectedCategoryIds = _allGraphicCategories
                .Where(x => x.IsSelected)
                .Select(x => x.CategoryId)
                .ToList();

            if (selectedTargetIds.Count == 0)
            {
                MessageBox.Show(_ownerWindow,
                    "Please select at least one target template.",
                    "Advanced Graphics Transfer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (selectedCategoryIds.Count == 0)
            {
                MessageBox.Show(_ownerWindow,
                    "Please select at least one model category.",
                    "Advanced Graphics Transfer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!CopyCategoryVisibility && !CopyCategoryProjectionOverrides && !CopyCategoryCutOverrides)
            {
                MessageBox.Show(_ownerWindow,
                    "Enable at least one transfer option: Visibility, Projection Overrides, or Cut Overrides.",
                    "Advanced Graphics Transfer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            IsBusy = true;
            BusyText = "Applying advanced category graphics";

            var request = new ApplyCategoryGraphicsRequest
            {
                SourceTemplateId = SelectedSourceTemplate.Id,
                CopyVisibility = CopyCategoryVisibility,
                CopyProjectionOverrides = CopyCategoryProjectionOverrides,
                CopyCutOverrides = CopyCategoryCutOverrides,
                OnSuccess = result =>
                {
                    _ownerWindow.Dispatcher.Invoke(() =>
                    {
                        IsBusy = false;

                        string summary =
                            $"Source: {result.SourceTemplateName}\n" +
                            $"Requested categories: {result.RequestedCategories}\n" +
                            $"Updated targets: {result.UpdatedTargets}\n" +
                            $"Skipped targets: {result.SkippedTargets}\n\n" +
                            string.Join("\n", result.Messages.Take(40));

                        if (result.Messages.Count > 40)
                            summary += $"\n\n... and {result.Messages.Count - 40} more messages.";

                        StatusText = $"Advanced graphics applied to {result.UpdatedTargets} target template(s).";

                        MessageBox.Show(_ownerWindow,
                            summary,
                            "Advanced Graphics Transfer Result",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    });
                },
                OnError = ex =>
                {
                    _ownerWindow.Dispatcher.Invoke(() =>
                    {
                        IsBusy = false;
                        MessageBox.Show(_ownerWindow,
                            ex.ToString(),
                            "Advanced Graphics Transfer Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    });
                }
            };

            request.TargetTemplateIds.AddRange(selectedTargetIds);
            request.SelectedCategoryIds.AddRange(selectedCategoryIds);

            _applyCategoryGraphicsHandler.Raise(request);
            _applyCategoryGraphicsExternalEvent.Raise();
        }
        private bool CanApplyCategoryGraphics()
        {
            if (IsBusy) return false;
            if (SelectedSourceTemplate == null) return false;
            if (!_allTargets.Any(x => x.IsSelected)) return false;
            if (!_allGraphicCategories.Any(x => x.IsSelected)) return false;
            if (!CopyCategoryVisibility && !CopyCategoryProjectionOverrides && !CopyCategoryCutOverrides) return false;
            return true;
        }
        public int SelectedGraphicCategoryCount => _allGraphicCategories.Count(x => x.IsSelected);
        public string SelectedSourceViewType
        {
            get => SelectedSourceTemplate?.ViewTypeName ?? "-";
        }

        public string SourceSearchText
        {
            get => _sourceSearchText;
            set
            {
                if (_sourceSearchText == value) return;
                _sourceSearchText = value ?? string.Empty;
                OnPropertyChanged();
                RebuildFilteredSources();
            }
        }

        public string PropertySearchText
        {
            get => _propertySearchText;
            set
            {
                if (_propertySearchText == value) return;
                _propertySearchText = value ?? string.Empty;
                OnPropertyChanged();
                RebuildFilteredProperties();
            }
        }

        public string TargetSearchText
        {
            get => _targetSearchText;
            set
            {
                if (_targetSearchText == value) return;
                _targetSearchText = value ?? string.Empty;
                OnPropertyChanged();
                RebuildFilteredTargets();
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText == value) return;
                _statusText = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy == value) return;
                _isBusy = value;
                OnPropertyChanged();
                ApplyCommand.RaiseCanExecuteChanged();
            }
        }

        public string BusyText
        {
            get => _busyText;
            set
            {
                if (_busyText == value) return;
                _busyText = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public int SelectedPropertyCount => _allProperties.Count(x => x.IsSelected);
        public int SelectedTargetCount => _allTargets.Count(x => x.IsSelected);

        private void LoadTemplates()
        {
            Document doc = _uiApp.ActiveUIDocument?.Document
                ?? throw new InvalidOperationException("No active Revit document.");

            _allSourceTemplates.Clear();
            _allTargets.Clear();
            SourceTemplates.Clear();
            FilteredSourceTemplates.Clear();
            FilteredTargets.Clear();

            // <- NEW: unsubscribe old filter items before rebuilding the list
            foreach (CheckableViewTypeItem existing in SourceViewTypeFilters)
                existing.PropertyChanged -= ViewTypeFilterItem_PropertyChanged;
            SourceViewTypeFilters.Clear();

            List<ViewTemplateItem> templates = ViewTemplateTransferService.GetAllViewTemplates(doc);

            foreach (ViewTemplateItem t in templates)
            {
                var item = new CheckableViewTemplateItem(t);
                item.PropertyChanged += Item_PropertyChanged;
                _allSourceTemplates.Add(item);
                SourceTemplates.Add(item);
            }

            // <- NEW: one checkbox per distinct ViewType actually present, alphabetical
            foreach (var group in _allSourceTemplates
                .GroupBy(x => x.ViewType)
                .OrderBy(g => g.First().ViewTypeName, StringComparer.OrdinalIgnoreCase))
            {
                var filterItem = new CheckableViewTypeItem(group.Key, group.First().ViewTypeName);
                filterItem.PropertyChanged += ViewTypeFilterItem_PropertyChanged;
                SourceViewTypeFilters.Add(filterItem);
            }

            RebuildFilteredSources();

            SelectedSourceTemplate = _allSourceTemplates.FirstOrDefault();

            StatusText = $"Loaded {templates.Count} view templates.";
        }

        // <- NEW: any view type checkbox flipping re-filters the Source list
        private void ViewTypeFilterItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CheckableViewTypeItem.IsSelected))
            {
                RebuildFilteredSources();
            }
        }

        private void LoadPropertiesForSelectedSource()
        {
            _allProperties.Clear();
            FilteredProperties.Clear();

            if (SelectedSourceTemplate == null)
            {
                UpdateCountsAndStatus();
                return;
            }

            Document doc = _uiApp.ActiveUIDocument?.Document
                ?? throw new InvalidOperationException("No active Revit document.");

            List<TemplatePropertyItem> props =
                ViewTemplateTransferService.GetTemplateProperties(doc, SelectedSourceTemplate.Id);

            foreach (TemplatePropertyItem p in props)
            {
                var item = new CheckableTemplatePropertyItem(p);
                item.PropertyChanged += Item_PropertyChanged;
                _allProperties.Add(item);
            }

            RebuildFilteredProperties();
            UpdateCountsAndStatus();
        }

        private void RebuildTargets()
        {
            _allTargets.Clear();
            FilteredTargets.Clear();

            if (SelectedSourceTemplate == null)
            {
                UpdateCountsAndStatus();
                return;
            }

            foreach (CheckableViewTemplateItem src in _allSourceTemplates)
            {
                if (src.Id == SelectedSourceTemplate.Id)
                    continue;

                if (src.ViewType != SelectedSourceTemplate.ViewType)
                    continue;

                var target = new CheckableViewTemplateItem(src.Id, src.Name, src.ViewType);
                target.PropertyChanged += Item_PropertyChanged;
                _allTargets.Add(target);
            }

            RebuildFilteredTargets();
            UpdateCountsAndStatus();
        }

        private void RebuildFilteredSources()
        {
            FilteredSourceTemplates.Clear();

            IEnumerable<CheckableViewTemplateItem> items = _allSourceTemplates;

            // <- CHANGED: view type filter is now "any checked box" instead of one selected dropdown value
            List<ViewType> checkedTypes = SourceViewTypeFilters
                .Where(x => x.IsSelected)
                .Select(x => x.ViewType)
                .ToList();

            if (checkedTypes.Count > 0)
            {
                items = items.Where(x => checkedTypes.Contains(x.ViewType));
            }

            string text = (_sourceSearchText ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                items = items.Where(x =>
                    x.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    x.ViewTypeName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            foreach (CheckableViewTemplateItem item in items.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                FilteredSourceTemplates.Add(item);
        }

        private void RebuildFilteredProperties()
        {
            FilteredProperties.Clear();

            IEnumerable<CheckableTemplatePropertyItem> items = _allProperties;

            string text = (_propertySearchText ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                items = items.Where(x =>
                    x.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            foreach (CheckableTemplatePropertyItem item in items.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                FilteredProperties.Add(item);

            UpdateCountsAndStatus();
        }

        private void RebuildFilteredTargets()
        {
            FilteredTargets.Clear();

            IEnumerable<CheckableViewTemplateItem> items = _allTargets;

            string text = (_targetSearchText ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                items = items.Where(x =>
                    x.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    x.ViewTypeName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            foreach (CheckableViewTemplateItem item in items.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                FilteredTargets.Add(item);

            UpdateCountsAndStatus();
        }

        private void SelectAllProperties()
        {
            foreach (CheckableTemplatePropertyItem item in FilteredProperties)
                item.IsSelected = true;

            UpdateCountsAndStatus();
        }

        private void ClearAllProperties()
        {
            foreach (CheckableTemplatePropertyItem item in FilteredProperties)
                item.IsSelected = false;

            UpdateCountsAndStatus();
        }

        private void InvertProperties()
        {
            foreach (CheckableTemplatePropertyItem item in FilteredProperties)
                item.IsSelected = !item.IsSelected;

            UpdateCountsAndStatus();
        }

        private void SelectAllTargets()
        {
            foreach (CheckableViewTemplateItem item in FilteredTargets)
                item.IsSelected = true;

            UpdateCountsAndStatus();
        }

        private void ClearAllTargets()
        {
            foreach (CheckableViewTemplateItem item in FilteredTargets)
                item.IsSelected = false;

            UpdateCountsAndStatus();
        }

        private void InvertTargets()
        {
            foreach (CheckableViewTemplateItem item in FilteredTargets)
                item.IsSelected = !item.IsSelected;

            UpdateCountsAndStatus();
        }

        private bool CanApply()
        {
            if (IsBusy) return false;
            if (SelectedSourceTemplate == null) return false;
            if (!_allProperties.Any(x => x.IsSelected)) return false;
            if (!_allTargets.Any(x => x.IsSelected)) return false;
            return true;
        }

        private void Apply()
        {
            if (SelectedSourceTemplate == null)
            {
                MessageBox.Show(_ownerWindow,
                    "Please select a source template.",
                    "Template Transfer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            List<ElementId> selectedPropertyIds = _allProperties
                .Where(x => x.IsSelected)
                .Select(x => x.ParameterId)
                .ToList();

            List<ElementId> selectedTargetIds = _allTargets
                .Where(x => x.IsSelected)
                .Select(x => x.Id)
                .ToList();

            if (selectedPropertyIds.Count == 0)
            {
                MessageBox.Show(_ownerWindow,
                    "Please select at least one property.",
                    "Template Transfer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (selectedTargetIds.Count == 0)
            {
                MessageBox.Show(_ownerWindow,
                    "Please select at least one target template.",
                    "Template Transfer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            IsBusy = true;
            BusyText = "Applying selected template properties";
            StatusText = "Applying changes to target templates...";

            var request = new ApplyTemplatePropertiesRequest
            {
                SourceTemplateId = SelectedSourceTemplate.Id,
                OnSuccess = result =>
                {
                    _ownerWindow.Dispatcher.Invoke(() =>
                    {
                        IsBusy = false;

                        string summary =
                            $"Source: {result.SourceTemplateName}\n" +
                            $"Updated targets: {result.UpdatedTargets}\n" +
                            $"Skipped targets: {result.SkippedTargets}\n\n" +
                            string.Join("\n", result.Messages.Take(40));

                        if (result.Messages.Count > 40)
                            summary += $"\n\n... and {result.Messages.Count - 40} more messages.";

                        StatusText = $"Done. Updated {result.UpdatedTargets} target template(s).";

                        MessageBox.Show(_ownerWindow,
                            summary,
                            "Template Transfer Result",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    });
                },
                OnError = ex =>
                {
                    _ownerWindow.Dispatcher.Invoke(() =>
                    {
                        IsBusy = false;
                        StatusText = "Failed.";
                        MessageBox.Show(_ownerWindow,
                            ex.ToString(),
                            "Template Transfer Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    });
                }
            };

            request.TargetTemplateIds.AddRange(selectedTargetIds);
            request.SelectedParameterIds.AddRange(selectedPropertyIds);

            _applyHandler.Raise(request);
            _applyExternalEvent.Raise();
        }
        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CheckableTemplatePropertyItem.IsSelected) ||
                e.PropertyName == nameof(CheckableViewTemplateItem.IsSelected) ||
                e.PropertyName == nameof(CategoryTransferItem.IsSelected) ||
                e.PropertyName == nameof(ViewFilterTransferItem.IsSelected))
            {
                UpdateCountsAndStatus();
                ApplyCommand.RaiseCanExecuteChanged();
                ApplyCategoryGraphicsCommand.RaiseCanExecuteChanged();
                ApplyViewFiltersCommand.RaiseCanExecuteChanged();

                OnPropertyChanged(nameof(SelectedGraphicCategoryCount));
                OnPropertyChanged(nameof(SelectedFilterCount));
            }
        }

        private void UpdateCountsAndStatus()
        {
            OnPropertyChanged(nameof(SelectedPropertyCount));
            OnPropertyChanged(nameof(SelectedTargetCount));
            OnPropertyChanged(nameof(SelectedGraphicCategoryCount));
            OnPropertyChanged(nameof(SelectedFilterCount));

            if (SelectedSourceTemplate == null)
            {
                StatusText = "Select a source template.";
                return;
            }

            StatusText =
                $"Source: {SelectedSourceTemplate.Name} | " +
                $"Properties selected: {SelectedPropertyCount} | " +
                $"Targets selected: {SelectedTargetCount}";
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
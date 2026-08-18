using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using BA.Core.Export.Infrastructure;
using BA.Core.Export.Models;
using BA.UI.Mvvm;

namespace BA.ViewModels.Export
{
    public class BookletViewModel : BA.UI.Mvvm.ObservableObject
    {
        private readonly List<BookletTypePickerRowViewModel> _allTypes = new List<BookletTypePickerRowViewModel>();

        public ObservableCollection<BookletCategoryOption> AvailableCategories { get; } = new ObservableCollection<BookletCategoryOption>
        {
            new BookletCategoryOption("Windows", BuiltInCategory.OST_Windows),
            new BookletCategoryOption("Doors", BuiltInCategory.OST_Doors),
            new BookletCategoryOption("Casework", BuiltInCategory.OST_Casework),
            new BookletCategoryOption("Furniture", BuiltInCategory.OST_Furniture),
            new BookletCategoryOption("Structural Framing", BuiltInCategory.OST_StructuralFraming),
            new BookletCategoryOption("Specialty Equipment", BuiltInCategory.OST_SpecialityEquipment),
            new BookletCategoryOption("Generic Models", BuiltInCategory.OST_GenericModel)
        };

        private BookletGenerationMode _generationMode = BookletGenerationMode.RealViews;
        public BookletGenerationMode GenerationMode
        {
            get => _generationMode;
            set
            {
                if (SetProperty(ref _generationMode, value))
                {
                    OnPropertyChanged(nameof(IsRealViewMode));
                    OnPropertyChanged(nameof(IsLegendMode));
                    ApplyInstanceRequirementToRows();
                }
            }
        }

        public bool IsRealViewMode
        {
            get => GenerationMode == BookletGenerationMode.RealViews;
            set
            {
                if (value)
                {
                    GenerationMode = BookletGenerationMode.RealViews;
                }
            }
        }

        public bool IsLegendMode
        {
            get => GenerationMode == BookletGenerationMode.LegendComponents;
            set
            {
                if (value)
                {
                    GenerationMode = BookletGenerationMode.LegendComponents;
                }
            }
        }

        public ObservableCollection<LegendViewOption> AvailableLegendViews { get; } = new ObservableCollection<LegendViewOption>();

        private LegendViewOption _selectedLegendView;
        public LegendViewOption SelectedLegendView
        {
            get => _selectedLegendView;
            set => SetProperty(ref _selectedLegendView, value);
        }

        private BookletCategoryOption _selectedCategory;
        public BookletCategoryOption SelectedCategory
        {
            get => _selectedCategory;
            set => SetProperty(ref _selectedCategory, value);
        }

        private BookletGroupingMode _groupingMode = BookletGroupingMode.RevitCategory;
        public BookletGroupingMode GroupingMode
        {
            get => _groupingMode;
            set
            {
                if (SetProperty(ref _groupingMode, value))
                {
                    OnPropertyChanged(nameof(IsCategoryMode));
                    OnPropertyChanged(nameof(IsParameterMode));
                }
            }
        }

        public bool IsCategoryMode
        {
            get => GroupingMode == BookletGroupingMode.RevitCategory;
            set
            {
                if (value)
                {
                    GroupingMode = BookletGroupingMode.RevitCategory;
                }
            }
        }

        public bool IsParameterMode
        {
            get => GroupingMode == BookletGroupingMode.ParameterValue;
            set
            {
                if (value)
                {
                    GroupingMode = BookletGroupingMode.ParameterValue;
                }
            }
        }

        private string _groupingParameterName = string.Empty;
        public string GroupingParameterName
        {
            get => _groupingParameterName;
            set => SetProperty(ref _groupingParameterName, value);
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplyFilter();
                }
            }
        }

        public ObservableCollection<BookletTypePickerRowViewModel> FilteredTypes { get; } = new ObservableCollection<BookletTypePickerRowViewModel>();

        public ObservableCollection<BookletTitleBlockFieldMapping> TitleBlockFieldMappings { get; } = new ObservableCollection<BookletTitleBlockFieldMapping>();

        private string _itemMarkTitleBlockParameterName = string.Empty;
        public string ItemMarkTitleBlockParameterName
        {
            get => _itemMarkTitleBlockParameterName;
            set => SetProperty(ref _itemMarkTitleBlockParameterName, value);
        }

        private string _itemMarkPrefix = "Z ";
        public string ItemMarkPrefix
        {
            get => _itemMarkPrefix;
            set => SetProperty(ref _itemMarkPrefix, value);
        }

        public ObservableCollection<TitleBlockOption> AvailableTitleBlocks { get; } = new ObservableCollection<TitleBlockOption>();

        private TitleBlockOption _selectedTitleBlock;
        public TitleBlockOption SelectedTitleBlock
        {
            get => _selectedTitleBlock;
            set => SetProperty(ref _selectedTitleBlock, value);
        }

        private double _cropMarginMm = 150;
        public double CropMarginMm
        {
            get => _cropMarginMm;
            set => SetProperty(ref _cropMarginMm, value);
        }

        private int _viewScale = 20;
        public int ViewScale
        {
            get => _viewScale;
            set => SetProperty(ref _viewScale, value);
        }

        private ViewDetailLevel _detailLevel = ViewDetailLevel.Fine;
        public ViewDetailLevel DetailLevel
        {
            get => _detailLevel;
            set => SetProperty(ref _detailLevel, value);
        }

        public IEnumerable<ViewDetailLevel> AvailableDetailLevels { get; } =
            Enum.GetValues(typeof(ViewDetailLevel)).Cast<ViewDetailLevel>();

        private string _outputSheetNumberPrefix = "TB-";
        public string OutputSheetNumberPrefix
        {
            get => _outputSheetNumberPrefix;
            set => SetProperty(ref _outputSheetNumberPrefix, value);
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        public BA.UI.Mvvm.RelayCommand LoadTypesCommand { get; }
        public BA.UI.Mvvm.RelayCommand SelectAllCommand { get; }
        public BA.UI.Mvvm.RelayCommand SelectNoneCommand { get; }
        public BA.UI.Mvvm.RelayCommand AddFieldMappingCommand { get; }
        public BA.UI.Mvvm.RelayCommand RemoveFieldMappingCommand { get; }
        public BA.UI.Mvvm.RelayCommand RunCommand { get; }

        public BookletViewModel()
        {
            SelectedCategory = AvailableCategories.First();

            LoadTypesCommand = new BA.UI.Mvvm.RelayCommand(_ => LoadTypes());
            SelectAllCommand = new BA.UI.Mvvm.RelayCommand(_ => SetAllSelected(true));
            SelectNoneCommand = new BA.UI.Mvvm.RelayCommand(_ => SetAllSelected(false));
            AddFieldMappingCommand = new BA.UI.Mvvm.RelayCommand(_ => OpenAddFieldMappingDialog());
            RemoveFieldMappingCommand = new BA.UI.Mvvm.RelayCommand(param => RemoveFieldMapping(param as BookletTitleBlockFieldMapping));
            RunCommand = new BA.UI.Mvvm.RelayCommand(_ => RunGeneration());

            LoadTitleBlocks();
            LoadLegendViews();
        }

        private void LoadLegendViews()
        {
            var request = new BookletUiRequest { Action = BookletUiAction.GetLegendViews };

            BookletUiBridge.Submit(request, response =>
            {
                if (!response.Success)
                {
                    return;
                }

                AvailableLegendViews.Clear();

                for (var i = 0; i < response.LegendViewNames.Count; i++)
                {
                    AvailableLegendViews.Add(new LegendViewOption(response.LegendViewNames[i], response.LegendViewUniqueIds[i]));
                }

                // Deliberately no auto-select here. Auto-selecting
                // whatever came back first previously caused an
                // unrelated, empty Legend view to get silently used as
                // the seed, producing a "no Legend Components in it"
                // failure with no indication the wrong view was ever
                // picked. Forcing an explicit choice, backed by the
                // existing null check in RunGeneration, is safer than
                // guessing.
            });
        }

        private void LoadTitleBlocks()
        {
            var request = new BookletUiRequest { Action = BookletUiAction.GetTitleBlocks };

            BookletUiBridge.Submit(request, response =>
            {
                if (!response.Success)
                {
                    return;
                }

                AvailableTitleBlocks.Clear();

                for (var i = 0; i < response.TitleBlockNames.Count; i++)
                {
                    AvailableTitleBlocks.Add(new TitleBlockOption(response.TitleBlockNames[i], response.TitleBlockUniqueIds[i]));
                }

                if (AvailableTitleBlocks.Count > 0)
                {
                    SelectedTitleBlock = AvailableTitleBlocks[0];
                }
            });
        }

        private void LoadTypes()
        {
            IsBusy = true;
            StatusMessage = "Loading types...";

            var request = GroupingMode == BookletGroupingMode.RevitCategory
                ? new BookletUiRequest { Action = BookletUiAction.GetTypesByCategory, Category = SelectedCategory?.Category ?? BuiltInCategory.OST_Windows }
                : new BookletUiRequest { Action = BookletUiAction.GetTypesByParameter, ParameterName = GroupingParameterName };

            if (GroupingMode == BookletGroupingMode.ParameterValue && string.IsNullOrWhiteSpace(GroupingParameterName))
            {
                IsBusy = false;
                StatusMessage = "Enter a parameter name to group by first.";
                return;
            }

            BookletUiBridge.Submit(request, response =>
            {
                IsBusy = false;

                if (!response.Success)
                {
                    StatusMessage = $"Failed to load types: {response.ErrorMessage}";
                    return;
                }

                StatusMessage = $"Ready. {response.Types.Count} types found.";

                _allTypes.Clear();
                _allTypes.AddRange(response.Types.Select(t => new BookletTypePickerRowViewModel(t)));
                ApplyInstanceRequirementToRows();

                TitleBlockFieldMappings.Clear();
                ApplyFilter();
            });
        }

        private void ApplyInstanceRequirementToRows()
        {
            var required = GenerationMode == BookletGenerationMode.RealViews;

            foreach (var row in _allTypes)
            {
                row.IsInstanceRequired = required;
            }
        }

        private void ApplyFilter()
        {
            FilteredTypes.Clear();

            var matches = string.IsNullOrWhiteSpace(SearchText)
                ? _allTypes
                : _allTypes.Where(t =>
                    t.TypeName.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.FamilyName.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0);

            foreach (var row in matches)
            {
                FilteredTypes.Add(row);
            }
        }

        private void SetAllSelected(bool selected)
        {
            foreach (var row in FilteredTypes.Where(r => r.IsSelectable))
            {
                row.IsSelected = selected;
            }
        }

        private void OpenAddFieldMappingDialog()
        {
            var selectedTypeIds = _allTypes.Where(t => t.IsSelected).Select(t => t.Info.UniqueId).ToList();

            if (selectedTypeIds.Count == 0)
            {
                StatusMessage = "Select at least one type first, the parameter list comes from the selected types.";
                return;
            }

            var request = new BookletUiRequest
            {
                Action = BookletUiAction.DiscoverInfoParameters,
                TypeUniqueIdsForParameterDiscovery = selectedTypeIds
            };

            BookletUiBridge.Submit(request, response =>
            {
                if (!response.Success)
                {
                    StatusMessage = $"Failed to load parameters: {response.ErrorMessage}";
                    return;
                }

                var alreadyAdded = new HashSet<string>(
                    TitleBlockFieldMappings.Where(m => m.SourceField != null).Select(m => m.SourceField.ColumnKey),
                    StringComparer.Ordinal);

                var picker = new BA.Views.Export.ParameterColumnPickerWindow(response.ParameterCandidates, alreadyAdded);

                var activeWindow = System.Windows.Application.Current?.Windows
                    .OfType<System.Windows.Window>()
                    .FirstOrDefault(w => w.IsActive);

                if (activeWindow != null)
                {
                    picker.Owner = activeWindow;
                }

                if (picker.ShowDialog() == true)
                {
                    // Target title block parameter name starts empty, the
                    // user types the actual name for their title block
                    // family directly into the row in the list, no second
                    // picker needed per field.
                    foreach (var descriptor in picker.SelectedColumns)
                    {
                        TitleBlockFieldMappings.Add(new BookletTitleBlockFieldMapping
                        {
                            SourceField = descriptor,
                            TitleBlockParameterName = string.Empty
                        });
                    }
                }
            });
        }

        private void RemoveFieldMapping(BookletTitleBlockFieldMapping mapping)
        {
            if (mapping != null)
            {
                TitleBlockFieldMappings.Remove(mapping);
            }
        }

        private void RunGeneration()
        {
            var selectedTypeIds = _allTypes.Where(t => t.IsSelected).Select(t => t.Info.UniqueId).ToList();

            if (selectedTypeIds.Count == 0)
            {
                StatusMessage = "No types selected.";
                return;
            }

            var titleBlockUniqueId = SelectedTitleBlock?.UniqueId ?? string.Empty;

            if (GenerationMode == BookletGenerationMode.LegendComponents && SelectedLegendView == null)
            {
                StatusMessage = "Pick a seed Legend view first, the one containing your placeholder component(s).";
                return;
            }

            var settings = new BookletSettings
            {
                Mode = GenerationMode,
                SeedLegendViewUniqueId = SelectedLegendView?.UniqueId ?? string.Empty,
                GroupingMode = GroupingMode,
                Category = SelectedCategory?.Category ?? BuiltInCategory.OST_Windows,
                GroupingParameterName = GroupingParameterName,
                SelectedTypeUniqueIds = selectedTypeIds,
                TitleBlockFieldMappings = TitleBlockFieldMappings.ToList(),
                ItemMarkTitleBlockParameterName = ItemMarkTitleBlockParameterName,
                ItemMarkPrefix = ItemMarkPrefix,
                CropMarginMm = CropMarginMm,
                ViewScale = ViewScale,
                DetailLevel = DetailLevel,
                TitleBlockUniqueId = titleBlockUniqueId,
                OutputSheetNumberPrefix = OutputSheetNumberPrefix
            };

            IsBusy = true;
            StatusMessage = $"Generating {selectedTypeIds.Count} booklets...";

            var request = new BookletUiRequest
            {
                Action = BookletUiAction.RunBooklets,
                SettingsForRun = settings
            };

            BookletUiBridge.Submit(request, response =>
            {
                IsBusy = false;

                if (!response.Success)
                {
                    StatusMessage = $"Generation failed: {response.ErrorMessage}";
                    return;
                }

                var succeeded = response.RunOutcomes.Count(o => o.Success);
                var failed = response.RunOutcomes.Count(o => !o.Skipped && !o.Success);
                var skipped = response.RunOutcomes.Count(o => o.Skipped);

                var detail = new System.Text.StringBuilder();
                detail.AppendLine($"{succeeded} generated, {failed} failed, {skipped} skipped.");

                foreach (var outcome in response.RunOutcomes)
                {
                    if (outcome.Skipped)
                    {
                        detail.AppendLine($"  Skipped {outcome.TypeName}: {outcome.SkippedReason}");
                    }
                    else if (!outcome.Success)
                    {
                        detail.AppendLine($"  Failed {outcome.TypeName}: {outcome.ErrorMessage}");
                    }
                    else
                    {
                        detail.AppendLine($"  {outcome.TypeName} -> sheet {outcome.SheetNumber}");
                    }
                }

                StatusMessage = detail.ToString().TrimEnd();
            });
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using BA.Core.Export.Infrastructure;
using BA.Core.Export.Models;
using BA.UI.Mvvm;
using Microsoft.Win32;

namespace BA.ViewModels.Export
{
    public class FamilyExportViewModel : BA.UI.Mvvm.ObservableObject
    {
        private readonly List<FamilyPickerRowViewModel> _allFamilies = new List<FamilyPickerRowViewModel>();

        public ObservableCollection<FamilyPickerRowViewModel> FilteredFamilies { get; } = new ObservableCollection<FamilyPickerRowViewModel>();

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

        private string _outputFolder = string.Empty;
        public string OutputFolder
        {
            get => _outputFolder;
            set => SetProperty(ref _outputFolder, value);
        }

        private bool _groupByCategory;
        public bool GroupByCategory
        {
            get => _groupByCategory;
            set => SetProperty(ref _groupByCategory, value);
        }

        private bool _skipExistingFiles;
        public bool SkipExistingFiles
        {
            get => _skipExistingFiles;
            set => SetProperty(ref _skipExistingFiles, value);
        }

        private bool _exportPreviewImage;
        public bool ExportPreviewImage
        {
            get => _exportPreviewImage;
            set => SetProperty(ref _exportPreviewImage, value);
        }

        private ExportFormat _imageFormat = ExportFormat.Png;
        public ExportFormat ImageFormat
        {
            get => _imageFormat;
            set => SetProperty(ref _imageFormat, value);
        }

        public IEnumerable<ExportFormat> AvailableImageFormats { get; } = new[] { ExportFormat.Jpeg, ExportFormat.Png };

        private string _preferredViewNamesText = "{3D}";

        /// <summary>
        /// Comma separated, in priority order, e.g. "{3D}, Front". Kept as
        /// a plain text field rather than a per-family view picker, since
        /// this is a batch tool and different families rarely share an
        /// exact view list to pick from uniformly.
        /// </summary>
        public string PreferredViewNamesText
        {
            get => _preferredViewNamesText;
            set => SetProperty(ref _preferredViewNamesText, value);
        }

        // Image settings, flat, same shape as the sheet export job's image settings.
        private ImageResolution _imageResolution = ImageResolution.DPI_300;
        public ImageResolution ImageResolution
        {
            get => _imageResolution;
            set => SetProperty(ref _imageResolution, value);
        }

        private ZoomFitType _imageZoomType = ZoomFitType.FitToPage;
        public ZoomFitType ImageZoomType
        {
            get => _imageZoomType;
            set
            {
                if (SetProperty(ref _imageZoomType, value))
                {
                    OnPropertyChanged(nameof(ImagePixelSizeEnabled));
                    OnPropertyChanged(nameof(ImageZoomPercentageEnabled));
                }
            }
        }

        public bool ImagePixelSizeEnabled => ImageZoomType == ZoomFitType.FitToPage;
        public bool ImageZoomPercentageEnabled => ImageZoomType == ZoomFitType.Zoom;

        private int _imagePixelSize = 800;
        public int ImagePixelSize
        {
            get => _imagePixelSize;
            set => SetProperty(ref _imagePixelSize, value);
        }

        private FitDirectionType _imageFitDirection = FitDirectionType.Horizontal;
        public FitDirectionType ImageFitDirection
        {
            get => _imageFitDirection;
            set => SetProperty(ref _imageFitDirection, value);
        }

        private int _imageZoomPercentage = 100;
        public int ImageZoomPercentage
        {
            get => _imageZoomPercentage;
            set => SetProperty(ref _imageZoomPercentage, value);
        }

        public IEnumerable<ImageResolution> AvailableImageResolutions { get; } =
            Enum.GetValues(typeof(ImageResolution)).Cast<ImageResolution>();

        public IEnumerable<ZoomFitType> AvailableImageZoomTypes { get; } =
            Enum.GetValues(typeof(ZoomFitType)).Cast<ZoomFitType>();

        public IEnumerable<FitDirectionType> AvailableImageFitDirections { get; } =
            Enum.GetValues(typeof(FitDirectionType)).Cast<FitDirectionType>();

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

        public BA.Core.Mvvm.RelayCommand BrowseOutputFolderCommand { get; }
        public BA.Core.Mvvm.RelayCommand SelectAllCommand { get; }
        public BA.Core.Mvvm.RelayCommand SelectNoneCommand { get; }
        public BA.Core.Mvvm.RelayCommand RunExportCommand { get; }

        public FamilyExportViewModel()
        {
            BrowseOutputFolderCommand = new BA.Core.Mvvm.RelayCommand(_ => BrowseOutputFolder());
            SelectAllCommand = new BA.Core.Mvvm.RelayCommand(_ => SetAllSelected(true));
            SelectNoneCommand = new BA.Core.Mvvm.RelayCommand(_ => SetAllSelected(false));
            RunExportCommand = new BA.Core.Mvvm.RelayCommand(_ => RunExport());

            LoadFamilies();
        }

        private void LoadFamilies()
        {
            IsBusy = true;
            StatusMessage = "Loading families...";

            var request = new FamilyExportUiRequest { Action = FamilyExportUiAction.GetFamilies };

            FamilyExportUiBridge.Submit(request, response =>
            {
                IsBusy = false;

                if (!response.Success)
                {
                    StatusMessage = $"Failed to load families: {response.ErrorMessage}";
                    return;
                }

                StatusMessage = $"Ready. {response.Families.Count} families found.";

                _allFamilies.Clear();
                _allFamilies.AddRange(response.Families.Select(f => new FamilyPickerRowViewModel(f)));

                ApplyFilter();
            });
        }

        private void ApplyFilter()
        {
            FilteredFamilies.Clear();

            var matches = string.IsNullOrWhiteSpace(SearchText)
                ? _allFamilies
                : _allFamilies.Where(f =>
                    f.Name.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    f.CategoryName.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0);

            foreach (var row in matches)
            {
                FilteredFamilies.Add(row);
            }
        }

        private void SetAllSelected(bool selected)
        {
            // Only affects the currently filtered/visible rows, and only
            // ones that are actually exportable, selecting a system family
            // would just get skipped later anyway but there is no reason
            // to check a box that can never do anything.
            foreach (var row in FilteredFamilies.Where(r => r.IsEditable))
            {
                row.IsSelected = selected;
            }
        }

        private void BrowseOutputFolder()
        {
            var dialog = new OpenFolderDialog { Title = "Select Output Folder" };

            if (!string.IsNullOrWhiteSpace(OutputFolder) && Directory.Exists(OutputFolder))
            {
                dialog.InitialDirectory = OutputFolder;
            }

            if (dialog.ShowDialog() == true)
            {
                OutputFolder = dialog.FolderName;
            }
        }

        private void RunExport()
        {
            var selected = _allFamilies.Where(f => f.IsSelected).Select(f => f.Info.UniqueId).ToList();

            if (selected.Count == 0)
            {
                StatusMessage = "No families selected.";
                return;
            }

            if (string.IsNullOrWhiteSpace(OutputFolder))
            {
                StatusMessage = "Choose an output folder first.";
                return;
            }

            var preferredNames = (PreferredViewNamesText ?? string.Empty)
                .Split(',')
                .Select(n => n.Trim())
                .Where(n => n.Length > 0)
                .ToList();

            if (ExportPreviewImage && preferredNames.Count == 0)
            {
                StatusMessage = "Enter at least one preferred view name, or turn off preview images.";
                return;
            }

            var settings = new FamilyExportSettings
            {
                SelectedFamilyUniqueIds = selected,
                OutputFolder = OutputFolder,
                GroupByCategory = GroupByCategory,
                SkipExistingFiles = SkipExistingFiles,
                ExportPreviewImage = ExportPreviewImage,
                ImageFormat = ImageFormat,
                PreferredImageViewNames = preferredNames,
                ImageSettings = new ImageSettings
                {
                    Resolution = ImageResolution,
                    ZoomType = ImageZoomType,
                    PixelSize = ImagePixelSize,
                    FitDirection = ImageFitDirection,
                    ZoomPercentage = ImageZoomPercentage
                }
            };

            IsBusy = true;
            StatusMessage = $"Exporting {selected.Count} families...";

            var request = new FamilyExportUiRequest
            {
                Action = FamilyExportUiAction.RunFamilyExport,
                SettingsForRun = settings
            };

            FamilyExportUiBridge.Submit(request, response =>
            {
                IsBusy = false;

                if (!response.Success || response.RunResult == null)
                {
                    StatusMessage = $"Export failed: {response.ErrorMessage}";
                    return;
                }

                var result = response.RunResult;
                var detail = new System.Text.StringBuilder();
                detail.AppendLine($"{result.SuccessCount} exported, {result.FailureCount} failed, {result.SkippedCount} skipped.");

                foreach (var outcome in result.Outcomes)
                {
                    if (outcome.Skipped)
                    {
                        detail.AppendLine($"  Skipped {outcome.FamilyName}: {outcome.SkippedReason}");
                    }
                    else if (!outcome.RfaSuccess)
                    {
                        detail.AppendLine($"  Failed {outcome.FamilyName}: {outcome.RfaErrorMessage}");
                    }
                    else if (outcome.ImageAttempted && !outcome.ImageSuccess)
                    {
                        detail.AppendLine($"  {outcome.FamilyName}: RFA exported, image skipped ({outcome.ImageErrorMessage})");
                    }
                }

                StatusMessage = detail.ToString().TrimEnd();
            });
        }
    }
}

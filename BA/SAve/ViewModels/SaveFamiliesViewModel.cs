using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Families.Handlers;
using BA.Families.Models;
using BA.Families.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BA;

namespace BA.Families.ViewModels
{
    public class SaveFamiliesViewModel : ObservableObject
    {
        // ─── Dependencies ─────────────────────────────────────────────────────────

        private readonly Document _doc;
        private readonly ExternalEvent _externalEvent;
        private readonly SaveFamiliesHandler _handler;
        private readonly FamilySaveOptionsRepository _repo;
        private readonly SaveFamiliesOptions _persisted;

        private List<FamilyExportItem> _allFamilies = new();

        // ─── Observable backing fields ────────────────────────────────────────────

        private ObservableCollection<FamilyExportItem> _displayedFamilies = new();
        private string _filterText = string.Empty;
        private string _outputFolder = string.Empty;
        private OverwriteMode _selectedOverwriteMode = OverwriteMode.Skip;
        private bool _organizeByCategory;
        private string _thumbnailViewName = "{3D}";
        private bool _compactFile;
        private bool _isSaving;
        private int _processedCount;
        private int _totalSelectedCount;
        private string _statusSummary = string.Empty;

        // ─── Observable properties ────────────────────────────────────────────────

        public ObservableCollection<FamilyExportItem> DisplayedFamilies
        {
            get => _displayedFamilies;
            private set => SetProperty(ref _displayedFamilies, value);
        }

        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetProperty(ref _filterText, value))
                    ApplyFilter();
            }
        }

        public string OutputFolder
        {
            get => _outputFolder;
            set => SetProperty(ref _outputFolder, value);
        }

        public OverwriteMode SelectedOverwriteMode
        {
            get => _selectedOverwriteMode;
            set => SetProperty(ref _selectedOverwriteMode, value);
        }

        public bool OrganizeByCategory
        {
            get => _organizeByCategory;
            set => SetProperty(ref _organizeByCategory, value);
        }

        public string ThumbnailViewName
        {
            get => _thumbnailViewName;
            set => SetProperty(ref _thumbnailViewName, value);
        }

        public bool CompactFile
        {
            get => _compactFile;
            set => SetProperty(ref _compactFile, value);
        }

        public bool IsSaving
        {
            get => _isSaving;
            private set
            {
                if (SetProperty(ref _isSaving, value))
                    ((CommunityToolkit.Mvvm.Input.RelayCommand)SaveCommand).NotifyCanExecuteChanged();
            }
        }

        public int ProcessedCount
        {
            get => _processedCount;
            private set => SetProperty(ref _processedCount, value);
        }

        public int TotalSelectedCount
        {
            get => _totalSelectedCount;
            private set => SetProperty(ref _totalSelectedCount, value);
        }

        public string StatusSummary
        {
            get => _statusSummary;
            private set => SetProperty(ref _statusSummary, value);
        }

        public int SelectedCount => _allFamilies.Count(f => f.IsSelected);
        public int TotalListCount => _allFamilies.Count;

        public IEnumerable<OverwriteMode> OverwriteModes { get; }
            = Enum.GetValues<OverwriteMode>();

        // ─── Commands ─────────────────────────────────────────────────────────────

        public ICommand BrowseOutputFolderCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand SaveCommand { get; }

        // ─── Constructor ──────────────────────────────────────────────────────────

        public SaveFamiliesViewModel(
            Document doc,
            ExternalEvent externalEvent,
            SaveFamiliesHandler handler)
        {
            _doc = doc;
            _externalEvent = externalEvent;
            _handler = handler;
            _repo = new FamilySaveOptionsRepository();
            _persisted = _repo.Load();

            BrowseOutputFolderCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(BrowseOutputFolder);
            SelectAllCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(SelectAll);
            SelectNoneCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(SelectNone);
            SaveCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(Save, () => !IsSaving);

            RestoreOptions();
            LoadFamilies();
        }

        // ─── Initialisation ───────────────────────────────────────────────────────

        private void RestoreOptions()
        {
            OutputFolder = _persisted.OutputFolder;
            SelectedOverwriteMode = _persisted.OverwriteMode;
            OrganizeByCategory = _persisted.OrganizeByCategory;
            ThumbnailViewName = string.IsNullOrWhiteSpace(_persisted.ThumbnailViewName)
                ? "{3D}"
                : _persisted.ThumbnailViewName;
            CompactFile = _persisted.CompactFile;
        }

        private void LoadFamilies()
        {
            _allFamilies = new FilteredElementCollector(_doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.IsEditable)
                .OrderBy(f => _doc.GetElement(f.FamilyCategoryId)?.Name ?? "Unknown")
                .ThenBy(f => f.Name)
                .Select(f =>
                {
                    string cat = _doc.GetElement(f.FamilyCategoryId)?.Name ?? "Unknown";
                    return new FamilyExportItem
                    {
                        Name = f.Name,
                        CategoryName = cat,
                        FamilyId = f.Id,
                        IsSelected = _persisted.LastSelectedFamilyNames.Contains(f.Name)
                    };
                })
                .ToList();

            ApplyFilter();
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(TotalListCount));
        }

        // ─── Filter ───────────────────────────────────────────────────────────────

        private void ApplyFilter()
        {
            IEnumerable<FamilyExportItem> result = string.IsNullOrWhiteSpace(FilterText)
                ? _allFamilies
                : _allFamilies.Where(f =>
                    f.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                    f.CategoryName.Contains(FilterText, StringComparison.OrdinalIgnoreCase));

            DisplayedFamilies = new ObservableCollection<FamilyExportItem>(result);
        }

        // ─── Command implementations ──────────────────────────────────────────────

        private void BrowseOutputFolder()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select output folder for exported family files",
                InitialDirectory = Directory.Exists(OutputFolder)
                                     ? OutputFolder
                                     : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dlg.ShowDialog() == true)
                OutputFolder = dlg.FolderName;
        }

        private void SelectAll()
        {
            foreach (var item in DisplayedFamilies)
                item.IsSelected = true;
            OnPropertyChanged(nameof(SelectedCount));
        }

        private void SelectNone()
        {
            foreach (var item in DisplayedFamilies)
                item.IsSelected = false;
            OnPropertyChanged(nameof(SelectedCount));
        }

        private void Save()
        {
            if (!ValidatePreConditions())
                return;

            List<FamilyExportItem> selected = _allFamilies.Where(f => f.IsSelected).ToList();

            foreach (var item in _allFamilies)
            {
                item.Status = FamilySaveStatus.Pending;
                item.StatusMessage = null;
            }

            TotalSelectedCount = selected.Count;
            ProcessedCount = 0;
            StatusSummary = string.Empty;
            IsSaving = true;

            PersistOptions(selected);

            _handler.ItemsToSave = _allFamilies.AsReadOnly();
            _handler.Options = new SaveFamiliesOptions
            {
                OutputFolder = OutputFolder,
                OverwriteMode = SelectedOverwriteMode,
                OrganizeByCategory = OrganizeByCategory,
                ThumbnailViewName = ThumbnailViewName,
                CompactFile = CompactFile
            };

            _handler.OnItemCompleted = item =>
            {
                if (item.Status is FamilySaveStatus.Saved
                               or FamilySaveStatus.Skipped
                               or FamilySaveStatus.Error)
                {
                    ProcessedCount++;
                    OnPropertyChanged(nameof(SelectedCount));
                }
            };

            _handler.OnAllCompleted = () =>
            {
                IsSaving = false;

                int saved = _allFamilies.Count(f => f.Status == FamilySaveStatus.Saved);
                int skipped = _allFamilies.Count(f => f.Status == FamilySaveStatus.Skipped);
                int errors = _allFamilies.Count(f => f.Status == FamilySaveStatus.Error);

                StatusSummary = $"Done — {saved} saved, {skipped} skipped, {errors} errors.";
            };

            _externalEvent.Raise();
        }

        // ─── Validation ───────────────────────────────────────────────────────────

        private bool ValidatePreConditions()
        {
            if (string.IsNullOrWhiteSpace(OutputFolder))
            {
                MessageBox.Show("Please select an output folder.",
                    "BATools — Save Families", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!Directory.Exists(OutputFolder))
            {
                var res = MessageBox.Show(
                    $"Output folder does not exist:\n{OutputFolder}\n\nCreate it now?",
                    "BATools — Save Families",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (res != MessageBoxResult.Yes)
                    return false;

                Directory.CreateDirectory(OutputFolder);
            }

            if (!_allFamilies.Any(f => f.IsSelected))
            {
                MessageBox.Show("No families are selected.",
                    "BATools — Save Families", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        // ─── Persistence ──────────────────────────────────────────────────────────

        private void PersistOptions(List<FamilyExportItem> selected)
        {
            _persisted.OutputFolder = OutputFolder;
            _persisted.OverwriteMode = SelectedOverwriteMode;
            _persisted.OrganizeByCategory = OrganizeByCategory;
            _persisted.ThumbnailViewName = ThumbnailViewName;
            _persisted.CompactFile = CompactFile;
            _persisted.LastSelectedFamilyNames = selected.Select(i => i.Name).ToList();
            _repo.Save(_persisted);
        }
    }
}
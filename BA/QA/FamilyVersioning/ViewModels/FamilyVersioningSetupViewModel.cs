using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using BA.QA.FamilyVersioning.Data;
using BA.QA.FamilyVersioning.Models;
using BA.QA.FamilyVersioning;

namespace BA.QA.FamilyVersioning.ViewModels
{
    /// <summary>
    /// Wraps a Building row for display/edit in the Setup window's list. Separate from
    /// the Models.Building POCO so the grid can bind to mutable observable properties
    /// for in-place editing (name, path) without those edits hitting the database until
    /// explicitly saved, the POCO itself is treated as an immutable snapshot elsewhere
    /// in the module.
    /// </summary>
    public partial class BuildingRowViewModel : ObservableObject
    {
        public int BuildingId { get; private set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDirty))]
        private string _buildingName;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDirty))]
        private string _centralModelPath;

        [ObservableProperty]
        private bool _enabled;

        /// <summary>
        /// The BuildingName as last persisted (or, for a new unsaved row, the initial
        /// empty value). Exposed as a real property, not reached into via reflection,
        /// so SaveCommand in the parent ViewModel can detect a pending rename and call
        /// BuildingRepository.UpdateBuildingName explicitly rather than guessing.
        /// </summary>
        public string OriginalName { get; private set; }

        public string OriginalPath { get; private set; }

        public bool IsDirty => BuildingName != OriginalName || CentralModelPath != OriginalPath;

        public bool IsNew { get; private set; }

        public BuildingRowViewModel(Building building)
        {
            BuildingId = building.BuildingId;
            _buildingName = building.BuildingName;
            _centralModelPath = building.CentralModelPath;
            _enabled = building.Enabled;
            OriginalName = building.BuildingName;
            OriginalPath = building.CentralModelPath;
            IsNew = false;
        }

        /// <summary>
        /// Constructs a not-yet-persisted row for the "Add Building" flow. BuildingId
        /// is 0 until SaveCommand successfully inserts it and the ViewModel is replaced
        /// with a persisted BuildingRowViewModel wrapping the real row.
        /// </summary>
        public BuildingRowViewModel()
        {
            BuildingId = 0;
            _buildingName = string.Empty;
            _centralModelPath = string.Empty;
            _enabled = true;
            OriginalName = string.Empty;
            OriginalPath = string.Empty;
            IsNew = true;
        }

        /// <summary>
        /// Called by the parent ViewModel after a successful save to reset the
        /// dirty-tracking baseline. When called after inserting a brand-new row,
        /// assignedBuildingId must be supplied (the real database-generated id);
        /// when called after updating an already-persisted row, omit it.
        /// </summary>
        public void MarkPersisted(int? assignedBuildingId = null)
        {
            if (assignedBuildingId.HasValue)
            {
                BuildingId = assignedBuildingId.Value;
            }

            OriginalName = BuildingName;
            OriginalPath = CentralModelPath;
            IsNew = false;
        }
    }

    /// <summary>
    /// Represents a tracked category row in the Setup window's Categories tab.
    /// Wraps TrackedCategory for direct DataGrid binding with observable Enabled toggle.
    /// </summary>
    public partial class CategoryRowViewModel : ObservableObject
    {
        public int TrackedCategoryId { get; }
        public int BuiltInCategoryId { get; }
        public string CategoryLabel { get; }

        [ObservableProperty]
        private bool _enabled;
        private readonly TrackedCategory category;

        public CategoryRowViewModel(TrackedCategory category)
        {
            TrackedCategoryId = category.TrackedCategoryId;
            BuiltInCategoryId = category.BuiltInCategoryId;
            CategoryLabel = category.CategoryLabel;
            _enabled = category.Enabled;
            this.category = category;
        }
    }

    /// <summary>
    /// Represents a Revit category available for tracking, shown in the "add category"
    /// picker list. BuiltInCategoryId is the integer value of BuiltInCategory enum.
    /// Label is resolved from the live Revit document at command launch time so it is
    /// locale-correct regardless of Revit installation language.
    /// </summary>
    public sealed class AvailableCategoryItem
    {
        public int BuiltInCategoryId { get; }
        public string Label { get; }

        public AvailableCategoryItem(int builtInCategoryId, string label)
        {
            BuiltInCategoryId = builtInCategoryId;
            Label = label ?? throw new ArgumentNullException(nameof(label));
        }
    }

    /// <summary>
    /// ViewModel for the Family Versioning Setup window. Manages the Buildings table:
    /// add, edit name/path, browse for central model file, enable/disable toggle.
    /// This is the minimum UI required before any other part of the Family Versioning
    /// module can function, FamilyBuildingState rows are foreign-keyed to BuildingId,
    /// so at least one Building row with a correct CentralModelPath must exist before
    /// the DocumentChanged hook can resolve "which building am I" on session start.
    /// </summary>
    public partial class FamilyVersioningSetupViewModel : ObservableObject
    {
        private readonly BuildingRepository _buildingRepository;
        private readonly TrackedCategoryRepository _categoryRepository;
        private readonly string _modelPath;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveBuildingCommand))]
        [NotifyCanExecuteChangedFor(nameof(DeleteBuildingCommand))]
        private BuildingRowViewModel? _selectedBuilding;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AddSelectedCategoryCommand))]
        private AvailableCategoryItem? _selectedAvailableCategory;
        
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RemoveCategoryCommand))]
        [NotifyCanExecuteChangedFor(nameof(ToggleCategoryEnabledCommand))]
        private CategoryRowViewModel? _selectedTrackedCategory;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
        private string? _statusMessage;

        [ObservableProperty]
        private string _catalogDatabasePath;

        public ObservableCollection<BuildingRowViewModel> Buildings { get; }
        public ObservableCollection<CategoryRowViewModel> TrackedCategories { get; }
        public ObservableCollection<AvailableCategoryItem> AvailableCategories { get; }

        public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

        public event Action? CloseRequested;

        public FamilyVersioningSetupViewModel(
            BuildingRepository buildingRepository,
            TrackedCategoryRepository categoryRepository,
            string catalogDatabasePath,
            string modelPath,
            IEnumerable<AvailableCategoryItem> availableCategories)
        {
            _buildingRepository = buildingRepository ?? throw new ArgumentNullException(nameof(buildingRepository));
            _categoryRepository = categoryRepository ?? throw new ArgumentNullException(nameof(categoryRepository));

            if (string.IsNullOrWhiteSpace(catalogDatabasePath))
                throw new ArgumentException("Catalog database path must not be empty.", nameof(catalogDatabasePath));

            if (string.IsNullOrWhiteSpace(modelPath))
                throw new ArgumentException("Model path must not be empty.", nameof(modelPath));

            _catalogDatabasePath = catalogDatabasePath;
            _modelPath = modelPath;

            var existingBuildings = _buildingRepository.GetAllBuildings();
            Buildings = new ObservableCollection<BuildingRowViewModel>(
                existingBuildings.Select(b => new BuildingRowViewModel(b)));

            var existingCategories = _categoryRepository.GetAll();
            var trackedIds = new HashSet<int>(existingCategories.Select(c => c.BuiltInCategoryId));
            TrackedCategories = new ObservableCollection<CategoryRowViewModel>(
                existingCategories.Select(c => new CategoryRowViewModel(c)));

            // Available categories are those not already tracked.
            AvailableCategories = new ObservableCollection<AvailableCategoryItem>(
                (availableCategories ?? Enumerable.Empty<AvailableCategoryItem>())
                .Where(a => !trackedIds.Contains(a.BuiltInCategoryId))
                .OrderBy(a => a.Label));
        }

        [RelayCommand]
        private void AddBuilding()
        {
            var newRow = new BuildingRowViewModel();
            Buildings.Add(newRow);
            SelectedBuilding = newRow;
            StatusMessage = "New building added below. Set a name and central model path, then click Save.";
        }

        [RelayCommand]
        private void BrowseCentralModelPath()
        {
            if (SelectedBuilding == null)
            {
                StatusMessage = "Select or add a building row first.";
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Select Central Model RVT",
                Filter = "Revit Project Files (*.rvt)|*.rvt",
                CheckFileExists = true
            };

            // If there's already a path set, start the browse dialog in that directory
            // so the user isn't dropped back at a default location every time they
            // need to update a path that moved on the network.
            if (!string.IsNullOrWhiteSpace(SelectedBuilding.CentralModelPath))
            {
                var existingDirectory = Path.GetDirectoryName(SelectedBuilding.CentralModelPath);
                if (!string.IsNullOrEmpty(existingDirectory) && Directory.Exists(existingDirectory))
                {
                    dialog.InitialDirectory = existingDirectory;
                }
            }

            if (dialog.ShowDialog() == true)
            {
                SelectedBuilding.CentralModelPath = dialog.FileName;
            }
        }

        private bool CanSaveBuilding() =>
            SelectedBuilding != null &&
            !string.IsNullOrWhiteSpace(SelectedBuilding.BuildingName) &&
            !string.IsNullOrWhiteSpace(SelectedBuilding.CentralModelPath);

        [RelayCommand(CanExecute = nameof(CanSaveBuilding))]
        private void SaveBuilding()
        {
            if (SelectedBuilding == null)
            {
                return;
            }

            // Warn, but do not block, if the path doesn't currently resolve. The central
            // could legitimately be temporarily offline (server maintenance, the file
            // not yet created if this is being set up pre-modeling as discussed). A hard
            // block here would prevent exactly the pre-modeling setup scenario this
            // Setup window exists for.
            if (!File.Exists(SelectedBuilding.CentralModelPath))
            {
                StatusMessage = $"Warning: '{SelectedBuilding.CentralModelPath}' does not currently exist or " +
                    "is not reachable. Saved anyway, this is expected if the central hasn't been created yet " +
                    "or the server is temporarily unavailable. Verify the path before relying on it.";
            }
            else
            {
                StatusMessage = null;
            }

            try
            {
                if (SelectedBuilding.IsNew)
                {
                    var created = _buildingRepository.AddBuilding(
                        SelectedBuilding.BuildingName, SelectedBuilding.CentralModelPath);

                    SelectedBuilding.MarkPersisted(created.BuildingId);
                }
                else
                {
                    if (SelectedBuilding.BuildingName != SelectedBuilding.OriginalName)
                    {
                        _buildingRepository.UpdateBuildingName(
                            SelectedBuilding.BuildingId, SelectedBuilding.BuildingName);
                    }

                    if (SelectedBuilding.CentralModelPath != SelectedBuilding.OriginalPath)
                    {
                        _buildingRepository.UpdateCentralModelPath(
                            SelectedBuilding.BuildingId, SelectedBuilding.CentralModelPath);
                    }

                    SelectedBuilding.MarkPersisted();
                }
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = ex.Message;
            }
        }

        [RelayCommand]
        private void ToggleEnabled()
        {
            if (SelectedBuilding == null || SelectedBuilding.IsNew)
            {
                StatusMessage = "Save the building before changing its enabled state.";
                return;
            }

            var newState = !SelectedBuilding.Enabled;

            try
            {
                _buildingRepository.SetEnabled(SelectedBuilding.BuildingId, newState);
                SelectedBuilding.Enabled = newState;
                StatusMessage = newState
                    ? $"'{SelectedBuilding.BuildingName}' is now enabled and will be included in scanning."
                    : $"'{SelectedBuilding.BuildingName}' is now disabled and excluded from scanning. " +
                      "History is preserved.";
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = ex.Message;
            }
        }

        private bool CanDeleteBuilding() => SelectedBuilding != null && SelectedBuilding.IsNew;

        /// <summary>
        /// Removes an unsaved new row from the list. Deliberately does NOT support
        /// deleting a persisted building, deletion would cascade through
        /// FamilyBuildingState, ExceptionTable, PendingRequests, and AuditLog rows for
        /// that building (the schema's ON DELETE CASCADE makes this technically easy),
        /// which destroys real project history. Use SetEnabled(false) to retire a
        /// building from active use instead. If a genuine delete is ever needed
        /// (a building added by mistake with zero real history), that should be a
        /// deliberate, separate, harder-to-reach operation than a button in this list,
        /// not exposed here.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanDeleteBuilding))]
        private void DeleteBuilding()
        {
            if (SelectedBuilding == null || !SelectedBuilding.IsNew)
            {
                return;
            }

            Buildings.Remove(SelectedBuilding);
            SelectedBuilding = null;
        }

        [RelayCommand]
        private void BrowseCatalogDatabasePath()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Select or Create Family Versioning Catalog Database",
                Filter = "SQLite Database (*.db)|*.db",
                DefaultExt = ".db",
                FileName = string.IsNullOrWhiteSpace(CatalogDatabasePath)
                    ? "BA_FamilyVersioning.db"
                    : Path.GetFileName(CatalogDatabasePath),
                OverwritePrompt = false
            };

            if (!string.IsNullOrWhiteSpace(CatalogDatabasePath))
            {
                var existingDirectory = Path.GetDirectoryName(CatalogDatabasePath);
                if (!string.IsNullOrEmpty(existingDirectory) && Directory.Exists(existingDirectory))
                {
                    dialog.InitialDirectory = existingDirectory;
                }
            }

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            CatalogDatabasePath = dialog.FileName;

            try
            {
                var existing = FamilyVersioningSettingsStore.Load(_modelPath) ?? new FamilyVersioningSettings();
                existing.CatalogDatabasePath = CatalogDatabasePath;
                FamilyVersioningSettingsStore.Save(_modelPath, existing);

                StatusMessage = "Catalog path saved. The new path will be used on next session open. " +
                    "Restart the Setup command to connect building rows against the new catalog.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Catalog path selected but could not be saved to settings: {ex.Message}";
            }
        }

        private bool CanAddSelectedCategory() => _selectedAvailableCategory != null;

        [RelayCommand(CanExecute = nameof(CanAddSelectedCategory))]
        private void AddSelectedCategory()
        {
            if (_selectedAvailableCategory == null) return;

            try
            {
                _categoryRepository.AddCategory(
                    _selectedAvailableCategory.BuiltInCategoryId,
                    _selectedAvailableCategory.Label);

                var category = _categoryRepository.GetAll()
                    .FirstOrDefault(c => c.BuiltInCategoryId == _selectedAvailableCategory.BuiltInCategoryId);
                if (category != null)
                {
                    TrackedCategories.Add(new CategoryRowViewModel(category));
                }

                AvailableCategories.Remove(_selectedAvailableCategory);
                _selectedAvailableCategory = null;
                StatusMessage = null;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not add category: {ex.Message}";
            }
        }

        private bool CanRemoveCategory() => _selectedTrackedCategory != null;

        [RelayCommand(CanExecute = nameof(CanRemoveCategory))]
        private void RemoveCategory()
        {
            if (_selectedTrackedCategory == null) return;

            try
            {
                _categoryRepository.RemoveCategory(_selectedTrackedCategory.TrackedCategoryId);
                // Return to the available list so the user can re-add it later.
                var available = new AvailableCategoryItem(
                    _selectedTrackedCategory.BuiltInCategoryId,
                    _selectedTrackedCategory.CategoryLabel);

                AvailableCategories.Add(available);

                // Keep the available list sorted.
                var sorted = AvailableCategories.OrderBy(a => a.Label).ToList();
                AvailableCategories.Clear();
                foreach (var item in sorted) AvailableCategories.Add(item);

                TrackedCategories.Remove(_selectedTrackedCategory);
                _selectedTrackedCategory = null;
                StatusMessage = null;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not remove category: {ex.Message}";
            }
        }

        private bool CanToggleCategoryEnabled() => _selectedTrackedCategory != null;

        [RelayCommand(CanExecute = nameof(CanToggleCategoryEnabled))]
        private void ToggleCategoryEnabled()
        {
            if (_selectedTrackedCategory == null) return;

            try
            {
                var newState = !_selectedTrackedCategory.Enabled;
                _categoryRepository.SetEnabled(_selectedTrackedCategory.TrackedCategoryId, newState);
                _selectedTrackedCategory.Enabled = newState;

                StatusMessage = newState
                    ? $"'{_selectedTrackedCategory.CategoryLabel}' will now trigger detection."
                    : $"'{_selectedTrackedCategory.CategoryLabel}' is paused. Families in this category will not trigger detection until re-enabled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not toggle category: {ex.Message}";
            }
        }

        [RelayCommand]
        private void Close()
        {
            CloseRequested?.Invoke();
        }
    }
}

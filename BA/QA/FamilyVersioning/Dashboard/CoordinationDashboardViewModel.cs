using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BA.QA.FamilyVersioning.Data;
using BA.QA.FamilyVersioning.Models;

namespace BA.QA.FamilyVersioning.Dashboard
{
    public partial class CoordinationDashboardViewModel : ObservableObject
    {
        private readonly DashboardDataService _dataService;
        private readonly CatalogConnectionFactory _factory;
        private readonly FamilyReviewEventHandler _reviewHandler;
        private readonly ReviewViewCleanupEventHandler _cleanupHandler;
        private readonly ExternalEvent _reviewExternalEvent;
        private readonly ExternalEvent _cleanupExternalEvent;
        private List<FamilyDashboardRow> _allRows = new();
        private int? _selectedFamilyId;
        private string _reviewTargetDocumentPath = string.Empty;
        private Dictionary<int, string> _buildingNameById = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MismatchCount))]
        [NotifyPropertyChangedFor(nameof(StatusSummary))]
        private ObservableCollection<FamilyDashboardRow> _visibleRows = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ReviewFamilyCommand))]
        private FamilyDashboardRow? _selectedRow;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusSummary))]
        private bool _showMismatchesOnly = true;

        [ObservableProperty]
        private string? _statusMessage;

        [ObservableProperty]
        private bool _hasStatusMessage;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private ObservableCollection<AuditHistoryDisplayRow> _auditHistory = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasAuditHistory))]
        private string _auditHistoryTitle = string.Empty;

        public bool HasAuditHistory => _auditHistory.Count > 0;

        public List<Building> EnabledBuildings { get; private set; } = new();
        public CatalogConnectionFactory Factory => _factory;

        public int MismatchCount => _allRows.Count(r => r.HasMismatch);

        public string StatusSummary => _allRows.Count == 0
            ? "No families tracked yet."
            : $"{MismatchCount} mismatch{(MismatchCount != 1 ? "es" : "")} across " +
              $"{_allRows.Count} tracked famil{(_allRows.Count != 1 ? "ies" : "y")}. " +
              "* = approved exception.";

        public event Action? ColumnsChanged;
        public event Action? CloseRequested;

        public CoordinationDashboardViewModel(
            DashboardDataService dataService,
            CatalogConnectionFactory factory,
            string targetDocumentPath)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _reviewTargetDocumentPath = targetDocumentPath ?? throw new ArgumentNullException(nameof(targetDocumentPath));

            _reviewHandler = new FamilyReviewEventHandler();
            _cleanupHandler = new ReviewViewCleanupEventHandler();
            _reviewExternalEvent = ExternalEvent.Create(_reviewHandler);
            _cleanupExternalEvent = ExternalEvent.Create(_cleanupHandler);
        }

        [RelayCommand]
        public void Refresh()
        {
            try
            {
                IsLoading = true;
                StatusMessage = null;
                HasStatusMessage = false;

                EnabledBuildings = _dataService.GetEnabledBuildings();
                _buildingNameById = EnabledBuildings.ToDictionary(b => b.BuildingId, b => b.BuildingName);
                _allRows = _dataService.BuildRows(EnabledBuildings);

                ColumnsChanged?.Invoke();
                ApplyFilter();

                OnPropertyChanged(nameof(MismatchCount));
                OnPropertyChanged(nameof(StatusSummary));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading catalog: {ex.Message}";
                HasStatusMessage = true;
            }
            finally
            {
                IsLoading = false;
            }
        }

        partial void OnSelectedRowChanged(FamilyDashboardRow? value)
        {
            _selectedFamilyId = value?.FamilyId;
            LoadAuditHistory(value);
        }

        private void LoadAuditHistory(FamilyDashboardRow? row)
        {
            _auditHistory.Clear();

            if (row == null)
            {
                _auditHistoryTitle = string.Empty;
                OnPropertyChanged(nameof(HasAuditHistory));
                return;
            }

            try
            {
                var auditRepo = new AuditLogRepository(_factory);
                var entries = auditRepo.GetHistoryForFamily(row.FamilyId, maxRows: 50);

                foreach (var entry in entries)
                {
                    var buildingName = _buildingNameById.TryGetValue(entry.BuildingId, out var name)
                        ? name
                        : $"Building {entry.BuildingId}";

                    _auditHistory.Add(new AuditHistoryDisplayRow(entry, buildingName));
                }

                _auditHistoryTitle = _auditHistory.Count > 0
                    ? $"Audit history for '{row.FamilyName}' ({_auditHistory.Count} entries)"
                    : $"No audit history for '{row.FamilyName}'";

                OnPropertyChanged(nameof(HasAuditHistory));
            }
            catch (Exception ex)
            {
                _auditHistoryTitle = $"Could not load audit history: {ex.Message}";
                OnPropertyChanged(nameof(HasAuditHistory));
            }
        }

        private bool CanReviewFamily() =>
            SelectedRow != null && EnabledBuildings.Count > 0;

        [RelayCommand(CanExecute = nameof(CanReviewFamily))]
        private void ReviewFamily()
        {
            if (SelectedRow == null) return;

            var request = new ReviewRequest(
                SelectedRow.FamilyName,
                SelectedRow.CategoryName,
                EnabledBuildings.ToList(),
                _reviewTargetDocumentPath);

            _reviewHandler.SetRequest(request);
            _reviewExternalEvent.Raise();
        }

        private bool CanPromoteToCanonical() => SelectedRow != null;

        /// <summary>
        /// Promotes the selected row's canonical version to match the most recently
        /// confirmed version across all buildings that are NOT in the exception set.
        /// If buildings have different non-excepted versions (i.e. there is a real
        /// mismatch), the user is warned and the command does not proceed automatically,
        /// they must resolve the mismatch first or explicitly choose which version to
        /// promote. If all non-excepted buildings agree on a version, that version is
        /// promoted to canonical silently.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanPromoteToCanonical))]
        private void PromoteToCanonical()
        {
            if (SelectedRow == null) return;

            try
            {
                var nonExceptedVersions = SelectedRow.VersionPerBuilding
                    .Where(kvp =>
                        !SelectedRow.ExceptionBuildingIds.Contains(kvp.Key) &&
                        kvp.Value != null)
                    .Select(kvp => kvp.Value!)
                    .Distinct()
                    .ToList();

                if (nonExceptedVersions.Count == 0)
                {
                    StatusMessage = $"No confirmed versions found for '{SelectedRow.FamilyName}' " +
                        "in any non-excepted building. Load the family into at least one building first.";
                    HasStatusMessage = true;
                    return;
                }

                if (nonExceptedVersions.Count > 1)
                {
                    StatusMessage = $"'{SelectedRow.FamilyName}' has multiple versions across buildings " +
                        $"({string.Join(", ", nonExceptedVersions)}). Resolve the mismatch first, then " +
                        "promote. All non-excepted buildings must agree on the same version before it can " +
                        "be set as canonical.";
                    HasStatusMessage = true;
                    return;
                }

                var versionToPromote = nonExceptedVersions[0];

                var familyRepo = new FamilyRepository(_factory);
                var family = familyRepo.FindByNameAndCategory(
                    SelectedRow.FamilyName, SelectedRow.CategoryName);

                if (family == null)
                {
                    StatusMessage = $"Family '{SelectedRow.FamilyName}' not found in catalog.";
                    HasStatusMessage = true;
                    return;
                }

                // Find the hash from any building that has this version loaded.
                // All non-excepted buildings should have the same hash if they agreed
                // on the same version, so taking the first is safe.
                var stateRepo = new FamilyBuildingStateRepository(_factory);
                var stateWithVersion = EnabledBuildings
                    .Select(b => stateRepo.Get(family.FamilyId, b.BuildingId))
                    .FirstOrDefault(s => s != null &&
                        string.Equals(s.LoadedVersion, versionToPromote, StringComparison.OrdinalIgnoreCase));

                var canonicalHash = stateWithVersion?.LoadedHash ?? string.Empty;

                familyRepo.UpdateCanonicalState(family.FamilyId, versionToPromote, canonicalHash, null);

                var auditRepo = new AuditLogRepository(_factory);
                // Log against the first non-excepted building that has this version.
                var buildingIdForLog = stateWithVersion?.BuildingId ?? EnabledBuildings.FirstOrDefault()?.BuildingId ?? 0;
                if (buildingIdForLog > 0)
                {
                    auditRepo.LogEvent(
                        family.FamilyId,
                        buildingIdForLog,
                        AuditEventType.Confirmed,
                        "Dashboard",
                        detail: $"Promoted version {versionToPromote} to canonical from dashboard.",
                        diffSummary: null);
                }

                StatusMessage = $"Version {versionToPromote} of '{SelectedRow.FamilyName}' " +
                    "is now the canonical office standard.";
                HasStatusMessage = true;

                // Refresh to update the Canonical column.
                Refresh();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not promote to canonical: {ex.Message}";
                HasStatusMessage = true;
            }
        }

        /// <summary>
        /// Called from Window.Closed (covers X button) and the Close command (covers
        /// the Close button). Safe to call multiple times: CreatedViewIds is cleared
        /// after being passed to the cleanup handler so a second call is a no-op.
        /// </summary>
        public void RequestCleanup()
        {
            if (_reviewHandler.CreatedViewIds.Count > 0)
            {
                _cleanupHandler.SetViewIds(
                    _reviewHandler.CreatedViewIds.ToList(),
                    _reviewTargetDocumentPath);
                _reviewHandler.CreatedViewIds.Clear();
                _cleanupExternalEvent.Raise();
            }
        }

        partial void OnShowMismatchesOnlyChanged(bool value)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var filtered = ShowMismatchesOnly
                ? _allRows.Where(r => r.HasMismatch).ToList()
                : _allRows;

            VisibleRows = new ObservableCollection<FamilyDashboardRow>(filtered);

            if (_selectedFamilyId.HasValue)
            {
                SelectedRow = VisibleRows.FirstOrDefault(r => r.FamilyId == _selectedFamilyId.Value);
            }
        }

        [RelayCommand]
        private void Close()
        {
            RequestCleanup();
            CloseRequested?.Invoke();
        }
    }
}

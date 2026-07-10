using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using BA.BAApplication;
using BA.Core.Ledger;
using BA.Core.Mvvm;
using BA.Settings;

namespace BA.ViewModels
{
    public class CategorySelectionItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public long Id { get; set; }
        public string Name { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class LedgerSettingsViewModel : INotifyPropertyChanged
    {
        private readonly LedgerSettings _settings;

        private string _ledgerFilePath;
        private string _retryCountText;
        private string _retryDelayMsText;
        private string _lastSyncDisplay = "Not yet synced in this document";
        private string _trackedFamiliesDisplay = "0";
        private bool _isRefreshing;
        private bool _trackAllCategories;
        private string _centralIdentifierText;
        private bool _isSettingIdentifier;

        public LedgerSettingsViewModel(LedgerSettings settings, LedgerDiagnosticsResult initialDiagnostics)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            LedgerFilePath = _settings.LedgerFilePath;
            RetryCountText = _settings.RetryCount.ToString(CultureInfo.InvariantCulture);
            RetryDelayMsText = _settings.RetryDelayMs.ToString(CultureInfo.InvariantCulture);
            _trackAllCategories = _settings.AllowedCategoryIds.Count == 0;

            Categories = new ObservableCollection<CategorySelectionItem>();
            PendingItems = new ObservableCollection<PendingLedgerItem>();

            ApplyDiagnostics(initialDiagnostics);
            _centralIdentifierText = initialDiagnostics?.CurrentCentralIdentifier ?? string.Empty;

            BrowseCommand = new RelayCommand(ExecuteBrowse);
            SaveCommand = new RelayCommand(ExecuteSave, CanSave);
            CancelCommand = new RelayCommand(ExecuteCancel);
            RefreshCommand = new RelayCommand(ExecuteRefresh, () => !IsRefreshing);
            SetIdentifierCommand = new RelayCommand(ExecuteSetIdentifier, () => !IsSettingIdentifier && !string.IsNullOrWhiteSpace(CentralIdentifierText));
        }
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Window code-behind sets this to close itself; ViewModel never references Window
        /// directly, keeping Revit/UI separation clean per project convention.
        /// </summary>
        public Action<bool?> RequestClose { get; set; }

        /// <summary>
        /// Window code-behind sets this to actually show the file dialog with itself as owner.
        /// Takes (suggestedFileName, initialDirectoryOrNull), returns the chosen full path, or
        /// null if the user cancelled. Not created directly here: SaveFileDialog.ShowDialog()
        /// with no owner can open behind the Revit main window in this hosting scenario. The
        /// code-behind has the actual Window reference needed to set that owner correctly.
        /// </summary>
        public Func<string, string, string> BrowseForFilePath { get; set; }

        public ObservableCollection<CategorySelectionItem> Categories { get; }
        public ObservableCollection<PendingLedgerItem> PendingItems { get; }

        public string LedgerFilePath
        {
            get => _ledgerFilePath;
            set
            {
                if (_ledgerFilePath == value)
                {
                    return;
                }
                _ledgerFilePath = value;
                OnPropertyChanged();
            }
        }

        public string RetryCountText
        {
            get => _retryCountText;
            set
            {
                if (_retryCountText == value)
                {
                    return;
                }
                _retryCountText = value;
                OnPropertyChanged();
            }
        }

        public string RetryDelayMsText
        {
            get => _retryDelayMsText;
            set
            {
                if (_retryDelayMsText == value)
                {
                    return;
                }
                _retryDelayMsText = value;
                OnPropertyChanged();
            }
        }

        public string LastSyncDisplay
        {
            get => _lastSyncDisplay;
            private set
            {
                _lastSyncDisplay = value;
                OnPropertyChanged();
            }
        }

        public string TrackedFamiliesDisplay
        {
            get => _trackedFamiliesDisplay;
            private set
            {
                _trackedFamiliesDisplay = value;
                OnPropertyChanged();
            }
        }

        public bool IsRefreshing
        {
            get => _isRefreshing;
            private set
            {
                _isRefreshing = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Explicit, deliberate toggle for "track everything." Persisted as an empty
        /// AllowedCategoryIds list when true. This exists specifically so the saved meaning of
        /// "no categories checked" is unambiguous, rather than being inferred from whatever
        /// happened to be checked when Save was clicked.
        /// </summary>
        public bool TrackAllCategories
        {
            get => _trackAllCategories;
            set
            {
                if (_trackAllCategories == value)
                {
                    return;
                }
                _trackAllCategories = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CustomCategorySelectionEnabled));
            }
        }

        public bool CustomCategorySelectionEnabled => !TrackAllCategories;

        /// <summary>
        /// Text box value for the manual central identifier. Distinct from LedgerFilePath etc:
        /// this is NOT saved by the main Save button, since it writes into the DOCUMENT
        /// (ExtensibleStorage on ProjectInformation) via a real Transaction, not the per-user
        /// LedgerSettings.json. It has its own explicit "Set" action so the two persistence
        /// targets can't be confused with each other.
        /// </summary>
        public string CentralIdentifierText
        {
            get => _centralIdentifierText;
            set
            {
                if (_centralIdentifierText == value)
                {
                    return;
                }
                _centralIdentifierText = value;
                OnPropertyChanged();
            }
        }

        public bool IsSettingIdentifier
        {
            get => _isSettingIdentifier;
            private set
            {
                _isSettingIdentifier = value;
                OnPropertyChanged();
            }
        }

        public ICommand SetIdentifierCommand { get; }

        public ICommand BrowseCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RefreshCommand { get; }

        private void ExecuteBrowse()
        {
            AppLogger.LogInfo("LedgerSettingsViewModel.ExecuteBrowse: command invoked.");

            if (BrowseForFilePath == null)
            {
                AppLogger.LogInfo("LedgerSettingsViewModel.ExecuteBrowse: BrowseForFilePath delegate is null, window never wired it.");
                return;
            }

            string suggestedFileName = string.IsNullOrWhiteSpace(LedgerFilePath)
                ? "Data_Ledger.json"
                : Path.GetFileName(LedgerFilePath);

            string initialDirectory = ResolveInitialDirectory();

            string chosenPath = BrowseForFilePath(suggestedFileName, initialDirectory);

            AppLogger.LogInfo($"LedgerSettingsViewModel.ExecuteBrowse: delegate returned '{chosenPath ?? "(null)"}'.");

            if (!string.IsNullOrWhiteSpace(chosenPath))
            {
                LedgerFilePath = chosenPath;
            }
        }

        /// <summary>
        /// Validates the directory actually exists before handing it to the dialog as a starting
        /// point. Falls back to null (dialog uses its own default) if it doesn't.
        /// </summary>
        private string ResolveInitialDirectory()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(LedgerFilePath))
                {
                    string directory = Path.GetDirectoryName(LedgerFilePath);
                    if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    {
                        return directory;
                    }
                }
            }
            catch (Exception)
            {
                // Malformed path in LedgerFilePath. Fall through to null.
            }

            return null;
        }

        private bool CanSave()
        {
            return !string.IsNullOrWhiteSpace(LedgerFilePath)
                && int.TryParse(RetryCountText, out int retryCount) && retryCount > 0
                && int.TryParse(RetryDelayMsText, out int retryDelay) && retryDelay > 0;
        }

        private void ExecuteSave()
        {
            AppLogger.LogInfo("LedgerSettingsViewModel.ExecuteSave: command invoked.");

            _settings.LedgerFilePath = LedgerFilePath.Trim();
            _settings.RetryCount = int.Parse(RetryCountText, CultureInfo.InvariantCulture);
            _settings.RetryDelayMs = int.Parse(RetryDelayMsText, CultureInfo.InvariantCulture);
            _settings.AllowedCategoryIds = TrackAllCategories
                ? new System.Collections.Generic.List<long>()
                : Categories.Where(c => c.IsSelected).Select(c => c.Id).ToList();

            _settings.Save();
            LedgerFileService.ReloadSettings();

            RequestClose?.Invoke(true);
        }

        private void ExecuteCancel()
        {
            AppLogger.LogInfo("LedgerSettingsViewModel.ExecuteCancel: command invoked.");
            RequestClose?.Invoke(false);
        }

        private void ExecuteSetIdentifier()
        {
            AppLogger.LogInfo("LedgerSettingsViewModel.ExecuteSetIdentifier: command invoked.");
            IsSettingIdentifier = true;
            string identifier = CentralIdentifierText?.Trim();

            LedgerUiBridge.RequestSetCentralIdentifier(identifier, success =>
            {
                IsSettingIdentifier = false;

                if (success)
                {
                    MessageBox.Show(
                        $"Central identifier set to '{identifier}' for this document.",
                        "Ledger Settings",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        "Could not set the central identifier. Check the log for details.",
                        "Ledger Settings",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            });
        }

        private void ExecuteRefresh()
        {
            AppLogger.LogInfo("LedgerSettingsViewModel.ExecuteRefresh: command invoked.");
            IsRefreshing = true;
            LedgerUiBridge.RequestRefresh(result =>
            {
                ApplyDiagnostics(result);
                IsRefreshing = false;
            });
        }

        private void ApplyDiagnostics(LedgerDiagnosticsResult diagnostics)
        {
            if (diagnostics == null)
            {
                return;
            }

            TrackedFamiliesDisplay = diagnostics.TrackedFamiliesCount.ToString(CultureInfo.InvariantCulture);
            LastSyncDisplay = diagnostics.LastSyncUtc.HasValue
                ? diagnostics.LastSyncUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                : "Not yet synced in this document";
            CentralIdentifierText = diagnostics.CurrentCentralIdentifier ?? string.Empty;

            PendingItems.Clear();
            foreach (PendingLedgerItem item in diagnostics.PendingItems)
            {
                PendingItems.Add(item);
            }

            // Selection now reads deterministically from persisted settings rather than
            // reconciling against whatever was checked before the refresh. TrackAllCategories
            // is the explicit source of truth for "everything"; this list only matters when
            // TrackAllCategories is off.
            Categories.Clear();
            foreach (CategoryOption option in diagnostics.AvailableCategories)
            {
                Categories.Add(new CategorySelectionItem
                {
                    Id = option.Id,
                    Name = option.Name,
                    IsSelected = _settings.AllowedCategoryIds.Contains(option.Id)
                });
            }
        }

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

// Path: BA\Materials\UI\AssetLibraryBrowserViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using BA.Materials.Models;
using BA.UI.ExternalEvents;

namespace BA.Materials.UI
{
    public sealed class AssetLibraryBrowserViewModel : BA.UI.Mvvm.ObservableObject
    {
        private readonly RevitExternalInvoker _invoker;
        private readonly LibraryDocumentService _libraryDocumentService;
        private readonly AssetLibraryBrowserService _browserService;
        private readonly AssetFavoritesStore _favoritesStore;
        private readonly MaterialAssetReader _assetReader;
        private readonly ElementId _targetMaterialId;

        private List<AssetLibraryEntry> _allAssets = new List<AssetLibraryEntry>();

        public AssetLibraryBrowserViewModel(
            RevitExternalInvoker invoker,
            LibraryDocumentService libraryDocumentService,
            AssetLibraryBrowserService browserService,
            AssetFavoritesStore favoritesStore,
            MaterialAssetReader assetReader,
            ElementId targetMaterialId)
        {
            _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
            _libraryDocumentService = libraryDocumentService ?? throw new ArgumentNullException(nameof(libraryDocumentService));
            _browserService = browserService ?? throw new ArgumentNullException(nameof(browserService));
            _favoritesStore = favoritesStore ?? throw new ArgumentNullException(nameof(favoritesStore));
            _assetReader = assetReader ?? throw new ArgumentNullException(nameof(assetReader));
            _targetMaterialId = targetMaterialId;

            FilteredAssets = new ObservableCollection<AssetLibraryEntry>();

            LoadAssetsCommand = new BA.UI.Mvvm.RelayCommand(_ => ExecuteLoadAssets(), _ => !IsBusy);
            ToggleFavoriteCommand = new BA.UI.Mvvm.RelayCommand(_ => ExecuteToggleFavorite(), _ => SelectedAsset != null);
            ApplyCommand = new BA.UI.Mvvm.RelayCommand(_ => ExecuteApply(), _ => !IsBusy && SelectedAsset != null);
        }

        public ObservableCollection<AssetLibraryEntry> FilteredAssets { get; }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { if (SetProperty(ref _isBusy, value)) RaiseCommandsCanExecuteChanged(); }
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { if (SetProperty(ref _searchText, value)) RefreshFiltered(); }
        }

        private bool _showFavoritesOnly;
        public bool ShowFavoritesOnly
        {
            get => _showFavoritesOnly;
            set { if (SetProperty(ref _showFavoritesOnly, value)) RefreshFiltered(); }
        }

        private AssetLibraryEntry _selectedAsset;
        public AssetLibraryEntry SelectedAsset
        {
            get => _selectedAsset;
            set
            {
                if (SetProperty(ref _selectedAsset, value))
                {
                    RaiseCommandsCanExecuteChanged();
                    LoadPreview();
                }
            }
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>Raised with the preview channels for a Generic-schema selection, or
        /// null when the selection can't be previewed (non-Generic schema, or nothing
        /// selected). Window decides how to render null, currently a neutral flat
        /// placeholder rather than an empty/misleading sphere.</summary>
        public event Action<MaterialChannelSet> PreviewChannelsReady;

        /// <summary>Raised once ApplyAssetToMaterial succeeds, window closes the dialog
        /// with DialogResult = true.</summary>
        public event Action ApplySucceeded;

        public BA.UI.Mvvm.RelayCommand LoadAssetsCommand { get; }
        public BA.UI.Mvvm.RelayCommand ToggleFavoriteCommand { get; }
        public BA.UI.Mvvm.RelayCommand ApplyCommand { get; }

        private void RaiseCommandsCanExecuteChanged()
        {
            LoadAssetsCommand.RaiseCanExecuteChanged();
            ToggleFavoriteCommand.RaiseCanExecuteChanged();
            ApplyCommand.RaiseCanExecuteChanged();
        }

        private void ExecuteLoadAssets()
        {
            IsBusy = true;
            StatusMessage = "Loading asset library...";

            HashSet<string> favoriteNames = _favoritesStore.LoadFavoriteNames();

            _invoker.Run(
                uiApp => _browserService.ListAllAssets(uiApp.Application, favoriteNames),
                onCompleted: (List<AssetLibraryEntry> assets) =>
                {
                    IsBusy = false;
                    _allAssets = assets ?? new List<AssetLibraryEntry>();
                    StatusMessage = $"{_allAssets.Count} assets available.";
                    RefreshFiltered();
                },
                onError: ex =>
                {
                    IsBusy = false;
                    StatusMessage = "Failed to load asset library. See BA Tools log for details.";
                });
        }

        private void RefreshFiltered()
        {
            IEnumerable<AssetLibraryEntry> query = _allAssets;

            if (ShowFavoritesOnly)
                query = query.Where(a => a.IsFavorite);

            if (!string.IsNullOrWhiteSpace(SearchText))
                query = query.Where(a => a.Name.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0);

            FilteredAssets.Clear();
            foreach (AssetLibraryEntry entry in query) FilteredAssets.Add(entry);
        }

        private void ExecuteToggleFavorite()
        {
            if (SelectedAsset == null) return;

            bool newState = !SelectedAsset.IsFavorite;
            bool saved = _favoritesStore.ToggleFavorite(SelectedAsset.Name, newState);

            if (!saved)
            {
                StatusMessage = "Failed to save favorite, check network access to the favorites file.";
                return;
            }

            SelectedAsset.IsFavorite = newState;

            AssetLibraryEntry entryInAll = _allAssets.FirstOrDefault(a => string.Equals(a.Name, SelectedAsset.Name, StringComparison.Ordinal));
            if (entryInAll != null) entryInAll.IsFavorite = newState;

            RefreshFiltered();
        }

        private void LoadPreview()
        {
            if (SelectedAsset == null)
            {
                PreviewChannelsReady?.Invoke(null);
                return;
            }

            if (!SelectedAsset.IsGenericSchema)
            {
                PreviewChannelsReady?.Invoke(null);
                StatusMessage = $"'{SelectedAsset.Name}' preview isn't available for this material type, it can still be applied.";
                return;
            }

            string assetName = SelectedAsset.Name;

            _invoker.Run(
                uiApp => _browserService.TryReadPreviewChannels(uiApp.Application, assetName, _assetReader),
                onCompleted: (MaterialChannelSet channels) =>
                {
                    PreviewChannelsReady?.Invoke(channels);
                },
                onError: ex =>
                {
                    StatusMessage = "Failed to load preview. See BA Tools log for details.";
                    PreviewChannelsReady?.Invoke(null);
                });
        }

        private void ExecuteApply()
        {
            if (SelectedAsset == null) return;

            IsBusy = true;
            string assetName = SelectedAsset.Name;

            _invoker.Run(
                uiApp => _browserService.ApplyAssetToMaterial(_libraryDocumentService.LibraryDocument, uiApp.Application, assetName, _targetMaterialId),
                onCompleted: result =>
                {
                    IsBusy = false;

                    if (!result.Success)
                    {
                        StatusMessage = result.FailureReason;
                        return;
                    }

                    ApplySucceeded?.Invoke();
                },
                onError: ex =>
                {
                    IsBusy = false;
                    StatusMessage = "Failed to apply asset. See BA Tools log for details.";
                });
        }
    }
}
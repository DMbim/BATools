using BA.Core.Content.Models;
using BA.Core.Content.Revit;
using BA.Core.Content.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace BA.UI.ContentBrowser
{
    public enum TreeMode
    {
        Classification,
        Folder
    }

    public sealed class ContentBrowserViewModel : INotifyPropertyChanged
    {
        private readonly ContentIndexService _indexService;
        private readonly ContentSearchService _searchService;
        private readonly ContentFavoritesService _favoritesService;
        private readonly ContentRecentService _recentService;
        private readonly RevitContentDispatcher _dispatcher;

        private readonly ObservableCollection<ContentItem> _allItems = new();

        private string _searchText = string.Empty;
        private string _selectedCategory = "All";
        private string _selectedRoot = "All";
        private bool _favoritesOnly;
        private ContentItem? _selectedItem;
        private string _statusText = "Ready";

        private DispatcherTimer? _previewTimer;
        private bool _previewRunning;
        private int _previewPollAttempts;
        private const int MaxPreviewPollAttempts = 30;

        private DispatcherTimer? _loadTimer;
        private bool _loadRunning;
        private int _loadPollAttempts;
        private const int MaxLoadPollAttempts = 30;
        private bool _pendingPlaceAfterLoad;
        private string _pendingFamilyPath = string.Empty;

        private readonly KeynoteHierarchyParser _keynoteParser = new();

        private TreeMode _treeMode = TreeMode.Classification;
        private ObservableCollection<object> _treeRoots = new();
        private object? _selectedTreeNode;

        public ObservableCollection<ContentItem> Items { get; } = new();
        public ObservableCollection<string> Categories { get; } = new();
        public ObservableCollection<string> Roots { get; } = new();

        public BA.UI.Mvvm.RelayCommand RefreshIndexCommand { get; }
        public BA.UI.Mvvm.RelayCommand LoadSelectedCommand { get; }
        public BA.UI.Mvvm.RelayCommand LoadAndPlaceCommand { get; }
        public BA.UI.Mvvm.RelayCommand ToggleFavoriteCommand { get; }
        public BA.UI.Mvvm.RelayCommand OpenFolderCommand { get; }
        public BA.UI.Mvvm.RelayCommand ExportPreviewCommand { get; }
        public ContentBrowserViewModel(
            ContentIndexService indexService,
            ContentSearchService searchService,
            ContentFavoritesService favoritesService,
            ContentRecentService recentService,
            RevitContentDispatcher dispatcher)
        {
            _indexService = indexService ?? throw new ArgumentNullException(nameof(indexService));
            _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
            _favoritesService = favoritesService ?? throw new ArgumentNullException(nameof(favoritesService));
            _recentService = recentService ?? throw new ArgumentNullException(nameof(recentService));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

            RefreshIndexCommand = new BA.UI.Mvvm.RelayCommand(_ => RefreshIndex());
            LoadSelectedCommand = new BA.UI.Mvvm.RelayCommand(_ => LoadSelected(false), _ => SelectedItem != null && !_previewRunning && !_loadRunning);
            LoadAndPlaceCommand = new BA.UI.Mvvm.RelayCommand(_ => LoadSelected(true), _ => SelectedItem != null && !_previewRunning && !_loadRunning);
            ToggleFavoriteCommand = new BA.UI.Mvvm.RelayCommand(_ => ToggleFavorite(), _ => SelectedItem != null);
            OpenFolderCommand = new BA.UI.Mvvm.RelayCommand(_ => OpenFolder(), _ => SelectedItem != null);
            ExportPreviewCommand = new BA.UI.Mvvm.RelayCommand(_ => ExportPreview(), _ => !_previewRunning && !_loadRunning);

            LoadInitial();
            RebuildTree();
        }

        public TreeMode TreeMode
        {
            get => _treeMode;
            set
            {
                if (SetField(ref _treeMode, value))
                    RebuildTree();
            }
        }

        public ObservableCollection<object> TreeRoots
        {
            get => _treeRoots;
            set => SetField(ref _treeRoots, value);
        }

        public object? SelectedTreeNode
        {
            get => _selectedTreeNode;
            set
            {
                if (SetField(ref _selectedTreeNode, value))
                    ApplyFilter();
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetField(ref _searchText, value))
                    ApplyFilter();
            }
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetField(ref _selectedCategory, value))
                    ApplyFilter();
            }
        }

        public string SelectedRoot
        {
            get => _selectedRoot;
            set
            {
                if (SetField(ref _selectedRoot, value))
                    ApplyFilter();
            }
        }

        public bool FavoritesOnly
        {
            get => _favoritesOnly;
            set
            {
                if (SetField(ref _favoritesOnly, value))
                    ApplyFilter();
            }
        }

        public ContentItem? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetField(ref _selectedItem, value))
                {
                    RaiseCommandStates();
                    OnPropertyChanged(nameof(SelectedPreviewPath));
                }
            }
        }

        public string SelectedPreviewPath
        {
            get
            {
                if (SelectedItem != null &&
                    !string.IsNullOrWhiteSpace(SelectedItem.PreviewPath) &&
                    File.Exists(SelectedItem.PreviewPath))
                {
                    return SelectedItem.PreviewPath;
                }

                return string.Empty;
            }
        }

        public string StatusText
        {
            get => _statusText;
            set => SetField(ref _statusText, value);
        }

        private void LoadInitial()
        {
            var items = _indexService.LoadIndex();
            if (items.Count == 0)
            {
                RefreshIndex();
                return;
            }

            ApplyLocalData(items);
        }

        private void RefreshIndex()
        {
            try
            {
                StatusText = "Building content index...";

                var items = _indexService.BuildIndex();
                _indexService.SaveIndex(items);

                ApplyLocalData(items);

                StatusText = $"Indexed {items.Count} items.";
            }
            catch (Exception ex)
            {
                StatusText = ex.Message;
                MessageBox.Show(ex.Message, "BA Content Browser");
            }
        }

        private void ApplyLocalData(IReadOnlyList<ContentItem> items)
        {
            _allItems.Clear();

            HashSet<string> favorites = _favoritesService.Load();
            Dictionary<string, DateTime> recent = _recentService.Load();

            foreach (var item in items)
            {
                item.IsFavorite = favorites.Contains(item.Id);

                if (recent.TryGetValue(item.Id, out DateTime lastUsed))
                    item.LastUsedUtc = lastUsed;

                _allItems.Add(item);
            }

            RebuildFacets();
            RebuildTree();
            ApplyFilter();
        }

        private void RebuildFacets()
        {
            Categories.Clear();
            Roots.Clear();

            Categories.Add("All");
            foreach (string x in _allItems
                .Select(i => i.Category)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .OrderBy(x => x))
            {
                Categories.Add(x);
            }

            Roots.Add("All");
            foreach (string x in _allItems
                .Select(i => i.RootName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .OrderBy(x => x))
            {
                Roots.Add(x);
            }
        }

        private void RebuildTree()
        {
            if (TreeMode == TreeMode.Classification)
            {
                TreeRoots = new ObservableCollection<object>(LoadClassificationTreeNodes().Cast<object>());
            }
            else
            {
                // Requires FolderTreeBuilder.Build(_allItems)
                TreeRoots = new ObservableCollection<object>(FolderTreeBuilder.Build(_allItems).Cast<object>());
            }

            SelectedTreeNode = null;
        }

        private IReadOnlyList<ClassificationNode> LoadClassificationTreeNodes()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string keynotePath = Path.Combine(appData, "BA", "ContentBrowser", "classification-keynotes.txt");

                if (!File.Exists(keynotePath))
                    return Array.Empty<ClassificationNode>();

                return _keynoteParser.ParseFile(keynotePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "BA Content Browser - Classification Tree");
                return Array.Empty<ClassificationNode>();
            }
        }

        private void ApplyFilter()
        {
            ClassificationNode? selectedClassificationNode = SelectedTreeNode as ClassificationNode;
            FolderNode? selectedFolderNode = SelectedTreeNode as FolderNode;

            var filtered = _searchService.Filter(
                _allItems,
                SearchText,
                SelectedCategory,
                SelectedRoot,
                FavoritesOnly,
                selectedClassificationNode,
                selectedFolderNode);

            Items.Clear();

            foreach (var item in filtered)
                Items.Add(item);

            StatusText = $"{Items.Count} items shown.";
        }

        private void CompleteLoad()
        {
            StopLoadPolling();
            _loadRunning = false;
            _loadPollAttempts = 0;
            RaiseCommandStates();

            if (SelectedItem != null)
            {
                var recent = _recentService.Load();
                recent[SelectedItem.Id] = DateTime.UtcNow;
                _recentService.Save(recent);
                SelectedItem.LastUsedUtc = recent[SelectedItem.Id];
            }

            StatusText = _pendingPlaceAfterLoad
                ? $"Loaded + placement requested: {Path.GetFileName(_pendingFamilyPath)}"
                : $"Loaded: {Path.GetFileName(_pendingFamilyPath)}";
        }

        private void FailLoad(string message)
        {
            StopLoadPolling();
            _loadRunning = false;
            _loadPollAttempts = 0;
            RaiseCommandStates();

            StatusText = message;
            MessageBox.Show(message, "BA Content Browser - Load Error");
        }

        private void LoadSelected(bool placeAfterLoad)
        {
            if (SelectedItem == null || _previewRunning || _loadRunning)
                return;

            try
            {
                _loadRunning = true;
                _loadPollAttempts = 0;
                _pendingPlaceAfterLoad = placeAfterLoad;
                _pendingFamilyPath = SelectedItem.FullPath;

                RaiseCommandStates();

                StatusText = $"Loading: {SelectedItem.FullPath}";

                _dispatcher.RequestLoad(new ContentLoadRequest
                {
                    FamilyPath = SelectedItem.FullPath,
                    ActivateFirstSymbol = true,
                    PlaceAfterLoad = placeAfterLoad
                });

                StartLoadPolling();
            }
            catch (Exception ex)
            {
                _loadRunning = false;
                RaiseCommandStates();
                MessageBox.Show(ex.Message, "BA Content Browser - Load");
                StatusText = ex.Message;
            }
        }

        private void LoadTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                _loadPollAttempts++;

                string error = _dispatcher.ConsumeLastLoadError();

                if (!string.IsNullOrWhiteSpace(error))
                {
                    FailLoad(error);
                    return;
                }

                if (_loadPollAttempts >= 3)
                {
                    CompleteLoad();
                    return;
                }

                if (_loadPollAttempts >= MaxLoadPollAttempts)
                {
                    FailLoad("Load operation timed out.");
                    return;
                }
            }
            catch (Exception ex)
            {
                FailLoad(ex.Message);
            }
        }

        private void ExportPreview()
        {
            if (SelectedItem == null || _previewRunning || _loadRunning)
                return;

            try
            {
                _previewRunning = true;
                _previewPollAttempts = 0;
                RaiseCommandStates();

                StatusText = $"Exporting preview: {SelectedItem.DisplayName}";

                _dispatcher.RequestPreviewExport(new ContentPreviewExportRequest
                {
                    FamilyPaths = new List<string>
                    {
                        SelectedItem.FullPath
                    },
                    OverwriteExisting = true
                });

                StartPreviewPolling();
            }
            catch (Exception ex)
            {
                _previewRunning = false;
                RaiseCommandStates();
                MessageBox.Show(ex.Message, "BA Content Browser - Preview Export");
                StatusText = ex.Message;
            }
        }

        private void StopLoadPolling()
        {
            if (_loadTimer != null)
                _loadTimer.Stop();
        }

        private void StartLoadPolling()
        {
            if (_loadTimer == null)
            {
                _loadTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                _loadTimer.Tick += LoadTimer_Tick;
            }

            _loadTimer.Stop();
            _loadTimer.Start();
        }

        private void StartPreviewPolling()
        {
            if (_previewTimer == null)
            {
                _previewTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                _previewTimer.Tick += PreviewTimer_Tick;
            }

            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private void StopPreviewPolling()
        {
            if (_previewTimer != null)
                _previewTimer.Stop();
        }

        private void PreviewTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (SelectedItem == null)
                {
                    FailPreview("No item is selected.");
                    return;
                }

                _previewPollAttempts++;

                string error = _dispatcher.ConsumeLastPreviewExportError();
                IReadOnlyList<ContentPreviewExportItemResult> results = _dispatcher.ConsumeLastPreviewExportResults();

                string expectedPng = Path.ChangeExtension(SelectedItem.FullPath, ".png");
                string expectedJpg = Path.ChangeExtension(SelectedItem.FullPath, ".jpg");
                string expectedJpeg = Path.ChangeExtension(SelectedItem.FullPath, ".jpeg");

                bool pngExists = File.Exists(expectedPng);
                bool jpgExists = File.Exists(expectedJpg) || File.Exists(expectedJpeg);

                if (!string.IsNullOrWhiteSpace(error))
                {
                    if (pngExists)
                    {
                        CompletePreview(expectedPng, jpgExists);
                        return;
                    }

                    FailPreview(error);
                    return;
                }

                if (results.Count > 0)
                {
                    ContentPreviewExportItemResult first = results.First();

                    if (first.Success)
                    {
                        string previewPath = !string.IsNullOrWhiteSpace(first.OutputImagePath) && File.Exists(first.OutputImagePath)
                            ? first.OutputImagePath
                            : (pngExists ? expectedPng : first.OutputImagePath);

                        CompletePreview(previewPath, jpgExists);
                        return;
                    }

                    if (pngExists)
                    {
                        CompletePreview(expectedPng, jpgExists);
                        return;
                    }

                    FailPreview(first.Message);
                    return;
                }

                if (pngExists)
                {
                    CompletePreview(expectedPng, jpgExists);
                    return;
                }

                if (_previewPollAttempts >= MaxPreviewPollAttempts)
                {
                    FailPreview("Preview export timed out.");
                    return;
                }
            }
            catch (Exception ex)
            {
                FailPreview(ex.Message);
            }
        }

        private void CompletePreview(string previewPath, bool jpgExists)
        {
            StopPreviewPolling();
            _previewRunning = false;
            _previewPollAttempts = 0;
            RaiseCommandStates();

            if (SelectedItem != null)
            {
                SelectedItem.PreviewPath = previewPath;
                OnPropertyChanged(nameof(SelectedPreviewPath));
            }

            StatusText = $"Preview ready: PNG=Yes, JPG={(jpgExists ? "Yes" : "No")}";
        }

        private void FailPreview(string message)
        {
            StopPreviewPolling();
            _previewRunning = false;
            _previewPollAttempts = 0;
            RaiseCommandStates();

            StatusText = message;
            MessageBox.Show(message, "BA Content Browser - Preview Export");
        }

        private void ToggleFavorite()
        {
            if (SelectedItem == null)
                return;

            SelectedItem.IsFavorite = !SelectedItem.IsFavorite;

            HashSet<string> favorites = _favoritesService.Load();

            if (SelectedItem.IsFavorite)
                favorites.Add(SelectedItem.Id);
            else
                favorites.Remove(SelectedItem.Id);

            _favoritesService.Save(favorites);
            ApplyFilter();
        }

        private void OpenFolder()
        {
            if (SelectedItem == null)
                return;

            string folder = Path.GetDirectoryName(SelectedItem.FullPath) ?? string.Empty;

            if (!Directory.Exists(folder))
            {
                MessageBox.Show("Folder not found.", "BA Content Browser");
                return;
            }

            Process.Start("explorer.exe", folder);
        }

        private void RaiseCommandStates()
        {
            LoadSelectedCommand.RaiseCanExecuteChanged();
            LoadAndPlaceCommand.RaiseCanExecuteChanged();
            ToggleFavoriteCommand.RaiseCanExecuteChanged();
            OpenFolderCommand.RaiseCanExecuteChanged();
            ExportPreviewCommand.RaiseCanExecuteChanged();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetField<T>(ref T backingField, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(backingField, value))
                return false;

            backingField = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
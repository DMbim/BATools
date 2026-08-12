using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Core.Content.Models;
using BA.Core.Content.Services;
using BA.UI.ExternalEvents;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace BA.UI.LoadedFamilyBrowser
{
    public sealed class LoadedFamilyBrowserViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly UIApplication _uiApp;
        private readonly Document _document;
        private readonly IntPtr _ownerHandle;

        private readonly LoadedFamilyBrowserSettingsService _settingsService;
        private readonly LoadedFamilyFavoritesService _favoritesService;
        private readonly LoadedFamilyIdentity _identity;

        private readonly DispatcherTimer _refreshDebounceTimer;
        private readonly Autodesk.Revit.ApplicationServices.Application _revitApplication;
        private bool _disposed;

        private LoadedFamilyBrowserSettings _settings;
        private Dictionary<string, LoadedFamilyFavoriteEntry> _userFavorites = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, LoadedFamilyFavoriteEntry> _projectFavorites = new(StringComparer.OrdinalIgnoreCase);
        private IReadOnlyList<ContentItem> _libraryIndex = Array.Empty<ContentItem>();

        private string _searchText = string.Empty;
        private bool _isBusy;
        private string _statusMessage = string.Empty;
        private object? _selectedNode;
        private bool _favoritesOnly;
        public bool SelectedNodePreviewMissing =>
    SelectedNode is not LoadedTypeNode typeNode || string.IsNullOrWhiteSpace(typeNode.PreviewPath);
        public ObservableCollection<LoadedCategoryNode> Categories { get; } = new();

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetField(ref _searchText, value))
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

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetField(ref _isBusy, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetField(ref _statusMessage, value);
        }
        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        public object? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (SetField(ref _selectedNode, value))
                {
                    RaiseCommandsChanged();
                    OnPropertyChanged(nameof(SelectedNodePreviewMissing));
                }
            }
        }

        public bool ProjectFavoritesAvailable => _favoritesService.ProjectScopeAvailable;
        public bool SelectedNodePreviewMissing =>
    SelectedNode is not LoadedTypeNode typeNode || string.IsNullOrWhiteSpace(typeNode.PreviewPath);

        public BA.Core.Mvvm.RelayCommand RefreshCommand { get; }
        public BA.Core.Mvvm.RelayCommand RenameCommand { get; }
        public BA.Core.Mvvm.RelayCommand PurgeCheckedCommand { get; }
        public BA.Core.Mvvm.RelayCommand ToggleUserFavoriteCommand { get; }
        public BA.Core.Mvvm.RelayCommand ToggleProjectFavoriteCommand { get; }
        public BA.Core.Mvvm.RelayCommand EditParametersCommand { get; }
        public BA.Core.Mvvm.RelayCommand OpenSettingsCommand { get; }

        public LoadedFamilyBrowserViewModel(
            UIApplication uiApp,
            IntPtr ownerHandle,
            IReadOnlyList<ContentItem> libraryIndex)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _document = uiApp.ActiveUIDocument?.Document
                ?? throw new InvalidOperationException("No active document.");
            _ownerHandle = ownerHandle;
            _revitApplication = uiApp.Application;
            _libraryIndex = libraryIndex ?? Array.Empty<ContentItem>();

            _settingsService = new LoadedFamilyBrowserSettingsService();
            _settings = _settingsService.LoadOrCreateDefault();

            _identity = LoadedFamilyIdentityResolver.Resolve(_document);
            _favoritesService = new LoadedFamilyFavoritesService(_identity);

            LoadFavorites();

            _refreshDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            _refreshDebounceTimer.Tick += (_, _) =>
            {
                _refreshDebounceTimer.Stop();
                Refresh();
            };

            _revitApplication.DocumentChanged += OnDocumentChanged;

            RefreshCommand = new BA.Core.Mvvm.RelayCommand(_ => Refresh(), _ => !IsBusy);
            RenameCommand = new BA.Core.Mvvm.RelayCommand(_ => Rename(), _ => !IsBusy && CanRename());
            PurgeCheckedCommand = new BA.Core.Mvvm.RelayCommand(_ => PurgeChecked(), _ => !IsBusy && GetCheckedTargets().Count > 0);
            ToggleUserFavoriteCommand = new BA.Core.Mvvm.RelayCommand(_ => ToggleFavorite(FavoriteScope.User), _ => SelectedNode is LoadedTypeNode);
            ToggleProjectFavoriteCommand = new BA.Core.Mvvm.RelayCommand(_ => ToggleFavorite(FavoriteScope.Project), _ => SelectedNode is LoadedTypeNode && ProjectFavoritesAvailable);
            EditParametersCommand = new BA.Core.Mvvm.RelayCommand(_ => EditParameters(), _ => !IsBusy && SelectedNode is LoadedTypeNode);
            OpenSettingsCommand = new BA.Core.Mvvm.RelayCommand(_ => OpenSettings());

            Refresh();
        }

        private void OnDocumentChanged(object? sender, Autodesk.Revit.DB.Events.DocumentChangedEventArgs e)
        {
            if (e.GetDocument().Title != _document.Title)
                return;

            _refreshDebounceTimer.Stop();
            _refreshDebounceTimer.Start();
        }

        private void LoadFavorites()
        {
            _userFavorites = _favoritesService.Load(FavoriteScope.User);
            _projectFavorites = _favoritesService.Load(FavoriteScope.Project);
        }

        public void Refresh()
        {
            IsBusy = true;
            StatusMessage = "Refreshing loaded families...";

            AppExternalInvoker.Instance.Run(
                uiApp => LoadedFamilyTreeBuilder.Build(_document, _settings),
                onCompleted: tree =>
                {
                    Categories.Clear();
                    foreach (var category in tree)
                    {
                        ApplyFavoritesAndPreviews(category);
                        Categories.Add(category);
                    }

                    IsBusy = false;
                    StatusMessage = string.Empty;
                    ApplyFilter();
                },
                onError: ex =>
                {
                    IsBusy = false;
                    StatusMessage = $"Refresh failed: {ex.Message}";
                    AppLogger.LogError(nameof(LoadedFamilyBrowserViewModel), ex);
                });
        }

        private void ApplyFavoritesAndPreviews(LoadedCategoryNode category)
        {

            foreach (var family in category.Families)
            {
                string previewPath = LoadedFamilyPreviewMatcher.Match(family.Name, _libraryIndex);

                foreach (var type in family.Types)
                {
                    string key = LoadedFamilyFavoritesService.BuildKey(family.Name, type.Name);
                    bool isUserFav = _userFavorites.TryGetValue(key, out var userEntry) && userEntry.IsFavorite;
                    bool isProjectFav = _projectFavorites.TryGetValue(key, out var projectEntry) && projectEntry.IsFavorite;

                    type.IsFavorite = isUserFav || isProjectFav;
                    type.PreviewPath = previewPath;

                    var tags = new List<string>();
                    if (userEntry?.Tags != null) tags.AddRange(userEntry.Tags);
                    if (projectEntry?.Tags != null) tags.AddRange(projectEntry.Tags);
                    type.Tags = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }
            }
        }

        private void ApplyFilter()
        {
            string search = SearchText?.Trim().ToLowerInvariant() ?? string.Empty;

            foreach (var category in Categories)
            {
                bool categoryHasVisible = false;

                foreach (var family in category.Families)
                {
                    bool familyHasVisible = false;

                    foreach (var type in family.Types)
                    {
                        bool matchesSearch = string.IsNullOrEmpty(search)
                            || type.Name.ToLowerInvariant().Contains(search)
                            || family.Name.ToLowerInvariant().Contains(search);

                        bool matchesFavorites = !FavoritesOnly || type.IsFavorite;

                        bool visible = matchesSearch && matchesFavorites;
                        type.IsVisible = visible;

                        familyHasVisible |= visible;
                    }

                    family.IsVisible = familyHasVisible;
                    categoryHasVisible |= familyHasVisible;
                }

                category.IsVisible = categoryHasVisible;
            }
        }



        private bool CanRename()
        {
            return SelectedNode is LoadedTypeNode || SelectedNode is LoadedFamilyNode;
        }

        private void Rename()
        {
            string currentName;
            string prompt;

            if (SelectedNode is LoadedTypeNode typeNode)
            {
                currentName = typeNode.Name;
                prompt = $"Enter new name for type '{typeNode.Name}':";
            }
            else if (SelectedNode is LoadedFamilyNode familyNode)
            {
                currentName = familyNode.Name;
                prompt = $"Enter new name for family '{familyNode.Name}':";
            }
            else
            {
                return;
            }

            var dialog = new RenamePromptWindow(prompt, currentName, _ownerHandle);
            if (dialog.ShowDialog() != true)
                return;

            string newName = dialog.ResultName;
            IsBusy = true;
            StatusMessage = "Renaming...";

            if (SelectedNode is LoadedTypeNode typeToRename)
            {
                AppExternalInvoker.Instance.Run(
                    uiApp => LoadedFamilyOperations.RenameType(_document, typeToRename.TypeId, newName),
                    onCompleted: result => HandleRenameResult(result, () => typeToRename.Name = newName),
                    onError: HandleUnexpectedError);
            }
            else if (SelectedNode is LoadedFamilyNode familyToRename)
            {
                AppExternalInvoker.Instance.Run(
                    uiApp => LoadedFamilyOperations.RenameFamily(_document, familyToRename.FamilyId, newName),
                    onCompleted: result => HandleRenameResult(result, () => familyToRename.Name = newName),
                    onError: HandleUnexpectedError);
            }
        }

        private void HandleRenameResult(LoadedFamilyOperationResult result, Action onSuccess)
        {
            IsBusy = false;

            if (result.Success)
            {
                onSuccess();
                StatusMessage = string.Empty;
            }
            else
            {
                StatusMessage = $"Rename failed: {result.Message}";
            }
        }

        private List<ElementId> GetCheckedTargets()
        {
            var targets = new List<ElementId>();

            foreach (var category in Categories)
            {
                foreach (var family in category.Families)
                {
                    bool allTypesChecked = family.Types.Count > 0 && family.Types.All(t => t.IsChecked);

                    if (allTypesChecked)
                    {
                        // Whole family checked: target the Family element,
                        // which removes every type under it in one delete.
                        targets.Add(family.FamilyId);
                        continue;
                    }

                    foreach (var type in family.Types.Where(t => t.IsChecked))
                        targets.Add(type.TypeId);
                }
            }

            return targets;
        }

        private void PurgeChecked()
        {
            var targets = GetCheckedTargets();
            if (targets.Count == 0)
                return;

            var confirm = System.Windows.MessageBox.Show(
                $"Delete {targets.Count} checked item(s)? This only removes items with zero placed instances detected. " +
                "Usage inside nested families, legends, or schedules is NOT checked by this tool. " +
                "This action cannot be undone from this window.",
                "Confirm Purge",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (confirm != System.Windows.MessageBoxResult.Yes)
                return;

            IsBusy = true;
            StatusMessage = "Purging...";

            AppExternalInvoker.Instance.Run(
                uiApp => LoadedFamilyOperations.PurgeUnused(_document, targets),
                onCompleted: result =>
                {
                    IsBusy = false;

                    if (result.Skipped.Count > 0)
                    {
                        StatusMessage = $"Purged {result.Deleted.Count} element(s), {result.Skipped.Count} skipped (in use or already removed).";
                    }
                    else
                    {
                        StatusMessage = $"Purged {result.Deleted.Count} element(s).";
                    }

                    Refresh();
                },
                onError: HandleUnexpectedError);
        }

        private void ToggleFavorite(FavoriteScope scope)
        {
            if (SelectedNode is not LoadedTypeNode typeNode)
                return;

            string key = LoadedFamilyFavoritesService.BuildKey(typeNode.ParentFamilyName, typeNode.Name);
            var dict = scope == FavoriteScope.User ? _userFavorites : _projectFavorites;

            if (!dict.TryGetValue(key, out var entry))
            {
                entry = new LoadedFamilyFavoriteEntry
                {
                    FamilyName = typeNode.ParentFamilyName,
                    TypeName = typeNode.Name
                };
                dict[key] = entry;
            }

            entry.IsFavorite = !entry.IsFavorite;

            try
            {
                _favoritesService.Save(scope, dict.Values);
                typeNode.IsFavorite = _userFavorites.TryGetValue(key, out var u) && u.IsFavorite
                    || _projectFavorites.TryGetValue(key, out var p) && p.IsFavorite;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to save favorite: {ex.Message}";
                entry.IsFavorite = !entry.IsFavorite; // revert
            }
        }

        private void EditParameters()
        {
            if (SelectedNode is not LoadedTypeNode typeNode)
                return;

            IsBusy = true;
            StatusMessage = "Loading parameters...";

            AppExternalInvoker.Instance.Run(
                uiApp => TypeParameterEditorViewModel.BuildParameterList(_document, typeNode.TypeId),
                onCompleted: parameters =>
                {
                    IsBusy = false;
                    StatusMessage = string.Empty;

                    var editorVm = new TypeParameterEditorViewModel(typeNode.TypeId, typeNode.Name, parameters);
                    var window = new TypeParameterEditorWindow(editorVm, _ownerHandle);
                    window.ShowDialog();

                    // Refresh instance/display state in case values affecting
                    // display were changed; cheap enough to just re-pull tree.
                    Refresh();
                },
                onError: HandleUnexpectedError);
        }

        private void OpenSettings()
        {
            var knownCategories = Categories.Select(c => c.Name).ToList();
            var settingsWindow = new LoadedFamilyBrowserSettingsWindow(
                knownCategories, _settingsService, _settings, _ownerHandle);

            if (settingsWindow.ShowDialog() == true)
            {
                Refresh();
            }
        }

        private void HandleUnexpectedError(Exception ex)
        {
            IsBusy = false;
            StatusMessage = $"Error: {ex.Message}";
            AppLogger.LogError(nameof(LoadedFamilyBrowserViewModel), ex);
        }

        private void RaiseCommandsChanged()
        {
            RenameCommand.RaiseCanExecuteChanged();
            PurgeCheckedCommand.RaiseCanExecuteChanged();
            ToggleUserFavoriteCommand.RaiseCanExecuteChanged();
            ToggleProjectFavoriteCommand.RaiseCanExecuteChanged();
            EditParametersCommand.RaiseCanExecuteChanged();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _refreshDebounceTimer.Stop();
            _revitApplication.DocumentChanged -= OnDocumentChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
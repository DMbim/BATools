// Path: BA\Materials\UI\MaterialLibraryWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Materials.Models;
using BA.UI.ExternalEvents;

namespace BA.Materials.UI
{
    /// <summary>
    /// Modeless material library window. Owns its own service instances (all stateless
    /// except LibraryDocumentService, which is per-window-session by design, see that
    /// class's remarks). Construct one instance per open window, do not share a single
    /// instance's services across multiple windows, the library lock is tied to this
    /// session's open/close lifecycle.
    /// </summary>
    public partial class MaterialLibraryWindow : Window
    {
        private readonly RevitExternalInvoker _invoker;
        private readonly LibraryDocumentService _libraryDocumentService;
        private readonly MaterialLibraryViewModel _viewModel;
        private bool _isClosed;

        public MaterialLibraryWindow(UIApplication uiApp, RevitExternalInvoker invoker)
        {
            if (uiApp == null) throw new ArgumentNullException(nameof(uiApp));
            _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));

            InitializeComponent();

            _libraryDocumentService = new LibraryDocumentService();
            var identityService = new MaterialIdentityService();
            var graphicsService = new MaterialGraphicsService();
            var assetReader = new MaterialAssetReader();
            var assetWriter = new MaterialAssetWriter(new SeedAssetProvider());
            var copyService = new LibraryCopyService(identityService, graphicsService, assetReader, assetWriter);

            _viewModel = new MaterialLibraryViewModel(
                _invoker, _libraryDocumentService, identityService, graphicsService, assetReader, assetWriter, copyService);

            DataContext = _viewModel;

            _viewModel.ConflictDetected += OnConflictDetected;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.AssetLibraryRequested += OnAssetLibraryRequested;

            WindowInteropHelper interopHelper = new WindowInteropHelper(this)
            {
                Owner = uiApp.MainWindowHandle
            };

            Loaded += (s, e) => _viewModel.LoadLibraryCommand.Execute(null);
            Closing += MaterialLibraryWindow_Closing;
        }

        // ------------------------------------------------------------------
        // Drag-to-category bulk class assignment
        // ------------------------------------------------------------------

        private System.Windows.Point _materialDragStartPoint;

        private void MaterialsListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _materialDragStartPoint = e.GetPosition(null);
        }

        private void MaterialsListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            System.Windows.Point currentPosition = e.GetPosition(null);
            System.Windows.Vector diff = _materialDragStartPoint - currentPosition;

            bool pastDragThreshold =
                Math.Abs(diff.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) >= SystemParameters.MinimumVerticalDragDistance;

            if (!pastDragThreshold) return;

            List<MaterialLibraryEntry> selected = MaterialsListBox.SelectedItems
                .Cast<MaterialLibraryEntry>()
                .ToList();

            if (selected.Count == 0) return;

            DataObject dragData = new DataObject(typeof(List<MaterialLibraryEntry>), selected);
            DragDrop.DoDragDrop(MaterialsListBox, dragData, DragDropEffects.Move);
        }

        private void CategoriesListBox_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(List<MaterialLibraryEntry>))) return;

            var draggedEntries = e.Data.GetData(typeof(List<MaterialLibraryEntry>)) as List<MaterialLibraryEntry>;
            if (draggedEntries == null || draggedEntries.Count == 0) return;

            // Walk up from wherever the drop landed to find the actual ListBoxItem,
            // OriginalSource is typically an inner TextBlock, not the item itself.
            DependencyObject hit = e.OriginalSource as DependencyObject;
            while (hit != null && !(hit is ListBoxItem))
                hit = VisualTreeHelper.GetParent(hit);

            if (!(hit is ListBoxItem listBoxItem)) return;

            string targetCategory = listBoxItem.DataContext as string;

            if (string.IsNullOrEmpty(targetCategory) || string.Equals(targetCategory, "All materials", StringComparison.Ordinal))
            {
                MessageBox.Show(this, "Drop onto a specific category, not 'All materials'.",
                    "Invalid drop target", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            List<ElementId> materialIds = draggedEntries.Select(entry => entry.MaterialId).ToList();
            _viewModel.AssignCategoryToMaterials(materialIds, targetCategory);
        }

        // ------------------------------------------------------------------
        // Identity / Graphics explicit commit
        // ------------------------------------------------------------------

        private void IdentityField_LostFocus(object sender, RoutedEventArgs e)
        {
            _viewModel.CommitIdentityCommand.Execute(null);
        }

        private void GraphicsField_Changed(object sender, RoutedEventArgs e)
        {
            _viewModel.CommitGraphicsCommand.Execute(null);
        }

        private void GraphicsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _viewModel.CommitGraphicsCommand.Execute(null);
        }

        // ------------------------------------------------------------------
        // Appearance preview push, ViewModel stays UI-control-agnostic, this window
        // is what actually knows a WebView2 preview host exists.
        // ------------------------------------------------------------------

        private static readonly string[] AppearancePropertyNames =
        {
            nameof(MaterialLibraryViewModel.AlbedoColor),
            nameof(MaterialLibraryViewModel.Roughness),
            nameof(MaterialLibraryViewModel.Reflectivity),
            nameof(MaterialLibraryViewModel.BumpAmount),
            nameof(MaterialLibraryViewModel.EmissiveColor),
            nameof(MaterialLibraryViewModel.EmissiveLuminanceCdM2),
            nameof(MaterialLibraryViewModel.AppearanceTransparency),
            nameof(MaterialLibraryViewModel.CutoutOpacity)
        };

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == null) return;

            bool isAppearanceProperty = Array.IndexOf(AppearancePropertyNames, e.PropertyName) >= 0;
            if (!isAppearanceProperty) return;

            var channels = new MaterialChannelSet
            {
                AlbedoR = _viewModel.AlbedoColor.R,
                AlbedoG = _viewModel.AlbedoColor.G,
                AlbedoB = _viewModel.AlbedoColor.B,
                Roughness = _viewModel.Roughness,
                Reflectivity = _viewModel.Reflectivity,
                BumpAmount = _viewModel.BumpAmount,
                EmissiveR = _viewModel.EmissiveColor.R,
                EmissiveG = _viewModel.EmissiveColor.G,
                EmissiveB = _viewModel.EmissiveColor.B,
                EmissiveLuminanceCdM2 = _viewModel.EmissiveLuminanceCdM2,
                Transparency = _viewModel.AppearanceTransparency,
                CutoutOpacity = _viewModel.CutoutOpacity
            };

            PreviewHost.UpdateChannels(channels);
        }

        // ------------------------------------------------------------------
        // Load from asset library
        // ------------------------------------------------------------------

        private void OnAssetLibraryRequested(ElementId materialId)
        {
            var browserService = new AssetLibraryBrowserService();
            var favoritesStore = new AssetFavoritesStore();
            var assetReader = new MaterialAssetReader();

            var dialog = new AssetLibraryBrowserWindow(
                _invoker, _libraryDocumentService, browserService, favoritesStore, assetReader, materialId)
            {
                Owner = this
            };

            bool? result = dialog.ShowDialog();

            if (result == true)
            {
                _viewModel.LoadSelectedMaterialDetail(materialId);
            }
        }

        // ------------------------------------------------------------------
        // Conflict prompt, plain MessageBox for now, see class remarks in the
        // ViewModel for why the styled modal from the mockup isn't wired yet.
        // ------------------------------------------------------------------

        private void OnConflictDetected(string materialName, Action onKeep, Action onOverwrite)
        {
            MessageBoxResult result = MessageBox.Show(
                this,
                $"'{materialName}' already exists in the active project.\n\n" +
                "Yes = overwrite the project version with the library version.\n" +
                "No = keep the project's existing version.",
                "Material already exists",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
                onOverwrite();
            else
                onKeep();
        }

        // ------------------------------------------------------------------
        // Save and close
        // ------------------------------------------------------------------

        private void MaterialLibraryWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_isClosed) return;
            _isClosed = true;

            _viewModel.FlushPendingAppearanceWrites();

            _invoker.Run(
                uiApp =>
                {
                    _libraryDocumentService.SaveAndClose(saveChanges: true, uiApp);
                    return true;
                },
                onCompleted: _ => AppLogger.LogInfo("BA.Materials: material library window closed, library saved and closed."),
                onError: ex => AppLogger.LogError("MaterialLibraryWindow_Closing (SaveAndClose)", ex));

            _viewModel.ConflictDetected -= OnConflictDetected;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.AssetLibraryRequested -= OnAssetLibraryRequested;
            _viewModel.Dispose();
        }
    }
}
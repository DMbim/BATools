// Path: BA\Materials\UI\AssetLibraryBrowserWindow.xaml.cs
using System.Windows;
using Autodesk.Revit.DB;
using BA.Materials.Models;
using BA.UI.ExternalEvents;

namespace BA.Materials.UI
{
    /// <summary>
    /// Modal dialog for browsing and applying Revit's built-in appearance asset
    /// library onto a single material. Owned by MaterialLibraryWindow, which reloads
    /// the affected material's Appearance panel after this returns true.
    /// </summary>
    public partial class AssetLibraryBrowserWindow : Window
    {
        private readonly AssetLibraryBrowserViewModel _viewModel;

        public AssetLibraryBrowserWindow(
            RevitExternalInvoker invoker,
            LibraryDocumentService libraryDocumentService,
            AssetLibraryBrowserService browserService,
            AssetFavoritesStore favoritesStore,
            MaterialAssetReader assetReader,
            ElementId targetMaterialId)
        {
            InitializeComponent();

            _viewModel = new AssetLibraryBrowserViewModel(
                invoker, libraryDocumentService, browserService, favoritesStore, assetReader, targetMaterialId);

            DataContext = _viewModel;

            _viewModel.PreviewChannelsReady += OnPreviewChannelsReady;
            _viewModel.ApplySucceeded += OnApplySucceeded;

            Loaded += (s, e) => _viewModel.LoadAssetsCommand.Execute(null);
        }

        private void OnPreviewChannelsReady(MaterialChannelSet channels)
        {
            // Null means "can't preview this schema" or "nothing selected", show a
            // neutral flat gray rather than an empty/misleading render either way.
            PreviewHost.UpdateChannels(channels ?? NeutralPlaceholderChannels());
        }

        private void OnApplySucceeded()
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static MaterialChannelSet NeutralPlaceholderChannels()
        {
            return new MaterialChannelSet
            {
                AlbedoR = 150,
                AlbedoG = 150,
                AlbedoB = 150,
                Roughness = 0.6,
                Reflectivity = 0.0
            };
        }
    }
}
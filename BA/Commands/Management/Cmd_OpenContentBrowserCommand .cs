using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Content.Revit;
using BA.Core.Content.Services;
using BA.UI.ContentBrowser;
using System;
using System.IO;
using System.Windows.Interop;

namespace BA.App.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class Cmd_OpenContentBrowserCommand : IExternalCommand
    {
        private static ContentBrowserWindow? _window;
        private static RevitContentDispatcher? _dispatcher;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (_window != null)
                {
                    if (_window.WindowState == System.Windows.WindowState.Minimized)
                        _window.WindowState = System.Windows.WindowState.Normal;

                    _window.Activate();
                    return Result.Succeeded;
                }

                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string settingsPath = Path.Combine(appData, "BA", "ContentBrowser", "content-browser.settings.json");
                string favoritesPath = Path.Combine(appData, "BA", "ContentBrowser", "favorites.json");
                string recentPath = Path.Combine(appData, "BA", "ContentBrowser", "recent.json");

                var settingsService = new ContentSettingsService(settingsPath);
                var settings = settingsService.LoadOrCreateDefault();

                var indexService = new ContentIndexService(settings);
                var searchService = new ContentSearchService();
                var favoritesService = new ContentFavoritesService(favoritesPath);
                var recentService = new ContentRecentService(recentPath);

                _dispatcher = new RevitContentDispatcher();

                var vm = new ContentBrowserViewModel(
                    indexService,
                    searchService,
                    favoritesService,
                    recentService,
                    _dispatcher);

                _window = new ContentBrowserWindow
                {
                    DataContext = vm
                };

                var helper = new WindowInteropHelper(_window)
                {
                    Owner = commandData.Application.MainWindowHandle
                };

                _window.Closed += OnWindowClosed;
                _window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                return Result.Failed;
            }
        }

        private static void OnWindowClosed(object? sender, EventArgs e)
        {
            if (_window != null)
            {
                _window.Closed -= OnWindowClosed;
                _window = null;
            }

            if (_dispatcher != null)
            {
                _dispatcher.Dispose();
                _dispatcher = null;
            }
        }
    }
}
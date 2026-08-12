using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Core.Content.Models;
using BA.Core.Content.Services;
using BA.UI.LoadedFamilyBrowser;

namespace BA.Commands.Content
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class Cmd_LoadedFamilyBrowser : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Run(commandData.Application, ref message);
        }

        public static Result Run(UIApplication uiApp, ref string message)
        {
            try
            {
                if (uiApp.ActiveUIDocument?.Document == null)
                {
                    message = "No active document.";
                    return Result.Failed;
                }

                IReadOnlyList<ContentItem> libraryIndex = LoadLibraryIndexSafely();

                var window = new LoadedFamilyBrowserWindow(uiApp, libraryIndex);
                window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                AppLogger.LogError(nameof(Cmd_LoadedFamilyBrowser), ex);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Loads the disk content index (used for preview matching only).
        /// The real settings file path used by ContentSettingsService was
        /// not visible in the uploaded files, so this uses the same default
        /// path convention as ContentSettingsService.CreateDefault(). If the
        /// real service exposes a different path, replace this block with a
        /// direct call to the existing ContentSettingsService instance.
        /// </summary>
        private static IReadOnlyList<ContentItem> LoadLibraryIndexSafely()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string settingsPath = System.IO.Path.Combine(appData, "BA", "ContentBrowser", "settings.json");

                var settingsService = new ContentSettingsService(settingsPath);
                var settings = settingsService.LoadOrCreateDefault();

                var indexService = new ContentIndexService(settings);
                return indexService.LoadIndex();
            }
            catch (Exception ex)
            {
                AppLogger.LogError(nameof(Cmd_LoadedFamilyBrowser) + ".LoadLibraryIndexSafely", ex);
                return Array.Empty<ContentItem>();
            }
        }
    }
}
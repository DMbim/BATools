using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using BA.QA.FamilyVersioning.Data;
using BA.QA.FamilyVersioning.ViewModels;
using BA.QA.FamilyVersioning.Views;
using BA.UI.Helpers;

namespace BA.QA.FamilyVersioning.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_FamilyVersioningSetup : IExternalCommand
    {
        /// <summary>
        /// Default BuiltInCategory values to offer as tracked categories on first run.
        /// These are the integer values of the BuiltInCategory enum, stable across
        /// Revit versions and locale-independent. Labels are resolved from the live
        /// document at runtime so they display in the user's Revit locale.
        /// </summary>
        private static readonly int[] DefaultCategoryIds =
        {
            (int)BuiltInCategory.OST_Doors,
            (int)BuiltInCategory.OST_Windows,
            (int)BuiltInCategory.OST_Furniture,
            (int)BuiltInCategory.OST_Casework,
            (int)BuiltInCategory.OST_PlumbingFixtures,
            (int)BuiltInCategory.OST_SpecialityEquipment,
            (int)BuiltInCategory.OST_MechanicalEquipment,
            (int)BuiltInCategory.OST_ElectricalEquipment,
            (int)BuiltInCategory.OST_ElectricalFixtures,
            (int)BuiltInCategory.OST_LightingFixtures,
            (int)BuiltInCategory.OST_StructuralFraming,
            (int)BuiltInCategory.OST_StructuralColumns,
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;

            if (uiDoc == null)
            {
                message = "No active document. Open a Revit model before running Family Versioning Setup.";
                return Result.Failed;
            }

            var doc = uiDoc.Document;
            var modelPath = doc.PathName;

            if (string.IsNullOrWhiteSpace(modelPath))
            {
                message = "The active document has not been saved yet. " +
                    "Save the model to a location on the shared drive before running Family Versioning Setup.";
                return Result.Failed;
            }

            try
            {
                var catalogPath = ResolveCatalogPath(modelPath);
                if (catalogPath == null)
                {
                    return Result.Cancelled;
                }

                var factory = new CatalogConnectionFactory(catalogPath);
                var buildingRepository = new BuildingRepository(factory);
                var categoryRepository = new TrackedCategoryRepository(factory);

                // Build the available categories list from the live document so labels
                // are locale-correct. All loadable family categories are included so the
                // user can track anything, not just the defaults.
                var availableCategories = BuildAvailableCategoryList(doc);

                // First-run category seeding: if no categories have been configured,
                // show a dialog presenting the default set and let the user confirm or
                // adjust before seeding. The seeding itself happens here in the command
                // (Revit API thread context) not in the ViewModel (which has no API access).
                if (!categoryRepository.HasAnyCategories())
                {
                    var seedResult = ShowFirstRunCategorySeedDialog(availableCategories, categoryRepository);
                    if (!seedResult)
                    {
                        return Result.Cancelled;
                    }
                }

                var viewModel = new FamilyVersioningSetupViewModel(
                    buildingRepository,
                    categoryRepository,
                    catalogPath,
                    modelPath,
                    availableCategories);

                var window = new FamilyVersioningSetupWindow(viewModel);
                RevitWindowHelper.SetOwnerToRevit(window, uiApp);
                window.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Family Versioning Setup encountered an unexpected error: {ex.Message}";
                return Result.Failed;
            }
        }

        /// <summary>
        /// Builds the full list of loadable family categories from the Revit document.
        /// Filters to categories that can host loadable families (CategoryType.Model
        /// and AllowsBoundParameters is a reasonable proxy, but checking against the
        /// default category list is more reliable for the typical BIM use case).
        /// Returns all model categories that Revit allows family loading into, sorted
        /// by locale-correct display name.
        /// </summary>
        private static List<AvailableCategoryItem> BuildAvailableCategoryList(Document doc)
        {
            var results = new List<AvailableCategoryItem>();
            var categories = doc.Settings.Categories;

            foreach (Category cat in categories)
            {
                if (cat == null) continue;
                if (cat.CategoryType != CategoryType.Model) continue;
                if (!cat.AllowsBoundParameters) continue;

                try
                {
                    results.Add(new AvailableCategoryItem((int)cat.Id.Value, cat.Name));
                }
                catch
                {
                    // Defensive: some categories throw on Id access in edge cases.
                }
            }

            return results.OrderBy(a => a.Label).ToList();
        }

        /// <summary>
        /// Shows the first-run category seeding dialog using a Revit TaskDialog.
        /// If the user confirms, seeds the default categories. Returns true if seeding
        /// was completed (or the user clicked OK with modifications), false if cancelled.
        /// </summary>
        private static bool ShowFirstRunCategorySeedDialog(
            List<AvailableCategoryItem> availableCategories,
            TrackedCategoryRepository categoryRepository)
        {
            var defaultLabels = availableCategories
                .Where(a => DefaultCategoryIds.Contains(a.BuiltInCategoryId))
                .OrderBy(a => a.Label)
                .Select(a => $"  {a.Label}")
                .ToList();

            var labelList = defaultLabels.Count > 0
                ? string.Join("\n", defaultLabels)
                : "  (None of the default categories were found in this document.)";

            var dialog = new TaskDialog("Family Versioning: First Run Category Setup")
            {
                MainContent =
                    "No tracked categories are configured for this catalog yet.\n\n" +
                    "The following categories will be tracked by default. Families loaded " +
                    "into these categories will trigger version detection.\n\n" +
                    labelList + "\n\n" +
                    "You can add, remove, or disable categories at any time from the " +
                    "Setup window's Categories tab.",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Ok
            };

            var result = dialog.Show();
            if (result != TaskDialogResult.Ok) return false;

            var toSeed = availableCategories
                .Where(a => DefaultCategoryIds.Contains(a.BuiltInCategoryId))
                .Select(a => (a.BuiltInCategoryId, a.Label))
                .ToList();

            categoryRepository.SeedCategories(toSeed);
            return true;
        }

        private static string? ResolveCatalogPath(string modelPath)
        {
            var settings = TryLoadSettings(modelPath);

            if (settings != null &&
                !string.IsNullOrWhiteSpace(settings.CatalogDatabasePath) &&
                File.Exists(settings.CatalogDatabasePath))
            {
                return settings.CatalogDatabasePath;
            }

            var isFirstRun = settings == null || string.IsNullOrWhiteSpace(settings.CatalogDatabasePath);
            var catalogMoved = !isFirstRun;

            var promptTitle = isFirstRun
                ? "Family Versioning: First Run Setup"
                : "Family Versioning: Catalog Not Found";

            var promptMessage = isFirstRun
                ? "No Family Versioning catalog has been configured for this project yet.\n\n" +
                  "Select an existing catalog database (.db) to connect to, or choose a new " +
                  "location to create one. The catalog stores family version history for all " +
                  "buildings in this project."
                : $"The Family Versioning catalog previously configured for this project was not " +
                  $"found at:\n\n{settings!.CatalogDatabasePath}\n\n" +
                  "The file may have been moved or the network location is currently unavailable. " +
                  "Select the new location of the catalog, or choose a new location to create one.";

            var result = TaskDialog.Show(promptTitle, promptMessage,
                TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel);

            if (result == TaskDialogResult.Cancel) return null;

            var dialog = new SaveFileDialog
            {
                Title = "Select or Create Family Versioning Catalog Database",
                Filter = "SQLite Database (*.db)|*.db",
                DefaultExt = ".db",
                FileName = "BA_FamilyVersioning.db",
                OverwritePrompt = false,
                InitialDirectory = Path.GetDirectoryName(modelPath)
            };

            if (catalogMoved && !string.IsNullOrWhiteSpace(settings?.CatalogDatabasePath))
            {
                var previousDirectory = Path.GetDirectoryName(settings!.CatalogDatabasePath);
                if (!string.IsNullOrEmpty(previousDirectory) && System.IO.Directory.Exists(previousDirectory))
                {
                    dialog.InitialDirectory = previousDirectory;
                    dialog.FileName = Path.GetFileName(settings.CatalogDatabasePath);
                }
            }

            if (dialog.ShowDialog() != true) return null;

            var chosenPath = dialog.FileName;
            var updatedSettings = settings ?? new FamilyVersioningSettings();
            updatedSettings.CatalogDatabasePath = chosenPath;
            FamilyVersioningSettingsStore.Save(modelPath, updatedSettings);

            return chosenPath;
        }

        private static FamilyVersioningSettings? TryLoadSettings(string modelPath)
        {
            try
            {
                return FamilyVersioningSettingsStore.Load(modelPath);
            }
            catch (InvalidOperationException)
            {
                TaskDialog.Show(
                    "Family Versioning: Settings File Error",
                    "The Family Versioning settings file for this project appears to be corrupt " +
                    "and could not be read. You will be prompted to re-enter the catalog database path.");
                return null;
            }
        }
    }
}

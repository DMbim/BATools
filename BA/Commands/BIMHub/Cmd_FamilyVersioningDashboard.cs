using System;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.QA.FamilyVersioning.Data;
using BA.QA.FamilyVersioning.Dashboard;
using BA.UI.Helpers;

namespace BA.QA.FamilyVersioning.Commands
{
    /// <summary>
    /// Opens the Family Versioning Coordination Dashboard as a MODELESS window.
    /// Modeless is required so ExternalEvent handlers (review view creation, cleanup)
    /// can execute while the window is open. ShowDialog would block Revit's event
    /// dispatcher and defer all ExternalEvent execution until after close.
    ///
    /// Ribbon registration:
    ///   panel.AddPushButton&lt;Cmd_FamilyVersioningDashboard&gt;(
    ///       "FamilyVersioningDashboard",
    ///       "Version\nDashboard",
    ///       "View family version consistency across all buildings in this project.",
    ///       IconResources.FamilyParameters16, IconResources.FamilyParameters32);
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_FamilyVersioningDashboard : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;

            if (uiDoc == null)
            {
                message = "No active document. Open a Revit model before opening the dashboard.";
                return Result.Failed;
            }

            var modelPath = uiDoc.Document.PathName;
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                message = "The active document has not been saved yet.";
                return Result.Failed;
            }

            try
            {
                var settings = FamilyVersioningSettingsStore.Load(modelPath);
                if (settings == null || string.IsNullOrWhiteSpace(settings.CatalogDatabasePath))
                {
                    TaskDialog.Show(
                        "Family Versioning Dashboard",
                        "No Family Versioning catalog has been configured for this project. " +
                        "Run the Family Versioning Setup command first to create or connect a catalog.");
                    return Result.Cancelled;
                }

                var factory = new CatalogConnectionFactory(settings.CatalogDatabasePath);
                var dataService = new DashboardDataService(factory);
                var viewModel = new CoordinationDashboardViewModel(dataService, factory, modelPath);
                var window = new CoordinationDashboardWindow(viewModel);

                // SetOwnerToRevit without ShowDialog so the window is modeless.
                // Modeless is required because ExternalEvent handlers (review view
                // creation, cleanup on close) must execute while the window is open.
                // ShowDialog blocks Revit's event dispatcher, deferring all
                // ExternalEvent execution until after the window closes, which is
                // exactly the bug being fixed here.
                RevitWindowHelper.SetOwnerToRevit(window, uiApp);
                window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Family Versioning Dashboard encountered an unexpected error: {ex.Message}";
                return Result.Failed;
            }
        }
    }
}

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Core.Ledger;
using BA.Settings;
using BA.UI.Ledger;
using BA.ViewModels;

namespace BA.Commands
{
    /// <summary>
    /// Ribbon command "Ledger Settings". Gathers the initial category list and diagnostics
    /// synchronously here (Execute is a valid Revit API context), then shows the settings
    /// window non-modally. All subsequent Refresh requests from the window go through
    /// LedgerUiBridge/ExternalEvent rather than calling Revit API from WPF directly.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdOpenLedgerSettings : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                LedgerUiBridge.EnsureInitialized();

                Document doc = commandData.Application.ActiveUIDocument?.Document;
                LedgerDiagnosticsResult initialDiagnostics = LedgerDiagnosticsService.Compute(doc);

                LedgerSettings settings = LedgerSettings.Load();
                var viewModel = new LedgerSettingsViewModel(settings, initialDiagnostics);

                var window = new LedgerSettingsWindow(viewModel);
                viewModel.RequestClose = result => window.Close();

                window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("CmdOpenLedgerSettings.Execute failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}

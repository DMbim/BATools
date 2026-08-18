// FILE: BA/Commands/Ribbon/CmdActivateBaTab.cs
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using BA.BAApplication;
using System;
using System.Linq;
using AdWindows = Autodesk.Windows;

namespace BA.Commands.Ribbon
{
    /// <summary>
    /// Activates the BA_Tools ribbon tab. Ribbon tab activation is not exposed by
    /// RevitAPIUI.dll, this goes through the internal Autodesk.Windows (AdWindows.dll)
    /// ComponentManager, which is unsupported by Autodesk and not guaranteed stable
    /// across major Revit version bumps. If tab activation silently does nothing after
    /// a Revit version upgrade, this is the first place to check, the RibbonTab
    /// lookup or the activation property may have changed shape.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdActivateBaTab : IExternalCommand
    {
        // Must match the internal tab name string passed to Application.CreateRibbonTab
        // in BaApplication.OnStartup (the "tabName" const there), not the display label.
        private const string TargetTabInternalName = "BA_Tools";


        public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            string errorMessage = string.Empty;
            bool success = ActivateTab(commandData.Application, ref errorMessage);

            if (!success)
            {
                message = errorMessage;
                AppLogger.LogError("CmdActivateBaTab.Execute", new InvalidOperationException(errorMessage));
                return Result.Failed;
            }

            return Result.Succeeded;
        }

        /// <summary>
        /// Static entry point matching the project's BimHub integration convention
        /// (IExternalCommand.Execute + static Run(UIApplication, ref string)).
        /// </summary>
        public static Result Run(UIApplication uiApp, ref string message)
        {
            bool success = ActivateTab(uiApp, ref message);
            return success ? Result.Succeeded : Result.Failed;
        }

        private static bool ActivateTab(UIApplication uiApp, ref string errorMessage)
        {
            try
            {
                AdWindows.RibbonControl ribbon = AdWindows.ComponentManager.Ribbon;

                if (ribbon == null || ribbon.Tabs == null)
                {
                    errorMessage = "Autodesk.Windows ComponentManager.Ribbon is not available in this session.";
                    return false;
                }

                AdWindows.RibbonTab targetTab = ribbon.Tabs
                    .FirstOrDefault(t => string.Equals(t.Id, TargetTabInternalName, StringComparison.OrdinalIgnoreCase));

                if (targetTab == null)
                {
                    // Fallback: some Revit/AdWindows versions populate Title with the
                    // internal name if no separate display label logic runs. Kept as a
                    // secondary lookup, not a primary strategy.
                    targetTab = ribbon.Tabs
                        .FirstOrDefault(t => string.Equals(t.Title, TargetTabInternalName, StringComparison.OrdinalIgnoreCase));
                }

                if (targetTab == null)
                {
                    errorMessage = $"Could not find ribbon tab with internal name '{TargetTabInternalName}'. " +
                        "Tab may not be registered yet, or the internal name has changed.";
                    return false;
                }

                targetTab.IsActive = true;

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Failed to activate BA_Tools tab: {ex.Message}";
                AppLogger.LogError("CmdActivateBaTab.ActivateTab", ex);
                return false;
            }
        }
    }
}
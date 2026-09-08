// FILE: BA/Core/Ribbon/RibbonTabActivator.cs
using System;
using System.Linq;
using Autodesk.Revit.UI;
using AdWindows = Autodesk.Windows;

namespace BA.Core.Ribbon
{
    /// <summary>
    /// Shared activation logic for jumping the active ribbon tab. Ribbon tab activation is
    /// not exposed by RevitAPIUI.dll, this goes through the internal Autodesk.Windows
    /// (AdWindows.dll) ComponentManager, which is unsupported by Autodesk and not guaranteed
    /// stable across major Revit version bumps. If tab activation silently does nothing after
    /// a Revit version upgrade, this is the first place to check, the RibbonTab lookup or the
    /// IsActive setter may have changed shape.
    /// RibbonControl has no SelectedTab property on this Revit version, confirmed by build
    /// error, IsActive on the RibbonTab itself is the only activation mechanism used here.
    /// </summary>
    internal static class RibbonTabActivator
    {
        internal static bool Activate(string targetTabInternalName, ref string errorMessage)
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
                    .FirstOrDefault(t => string.Equals(t.Id, targetTabInternalName, StringComparison.OrdinalIgnoreCase));

                if (targetTab == null)
                {
                    // Fallback: some Revit/AdWindows versions populate Title with the
                    // internal name if no separate display label logic runs. Kept as a
                    // secondary lookup, not a primary strategy.
                    targetTab = ribbon.Tabs
                        .FirstOrDefault(t => string.Equals(t.Title, targetTabInternalName, StringComparison.OrdinalIgnoreCase));
                }

                if (targetTab == null)
                {
                    errorMessage = $"Could not find ribbon tab with internal name '{targetTabInternalName}'. " +
                        "Tab may not be registered yet, or the internal name has changed.";
                    return false;
                }

                targetTab.IsActive = true;

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Failed to activate tab '{targetTabInternalName}': {ex.Message}";
                BA.BAApplication.AppLogger.LogError("RibbonTabActivator.Activate", ex);
                return false;
            }
        }
    }
}
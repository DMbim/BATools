// File: BA_Tools/VisibilityDiagnostics/Services/Checks/WorksetVisibilityCheck.cs
using System;
using Autodesk.Revit.DB;
using BA.VisibilityDiagnostics.Models;

namespace BA.VisibilityDiagnostics.Services.Checks
{
    /// <summary>
    /// Checks whether the element's Workset is hidden - either via a per-view
    /// override (View.GetWorksetVisibility), or (when the view uses the global
    /// setting) via the document-wide default visibility for that workset.
    ///
    /// The WorksetDefaultVisibilitySettings.GetWorksetDefaultVisibilitySettings(Document)
    /// static accessor name was not independently re-verified against Autodesk
    /// primary source in this session (only its IsWorksetVisible/SetWorksetVisibility
    /// instance members were confirmed) - verify this exact static factory member
    /// name in Object Browser on first build. // <- ASSUMPTION, VERIFY
    /// </summary>
    public sealed class WorksetVisibilityCheck : IVisibilityCheck
    {
        public string CheckName => "Workset";

        public VisibilityCheckItem Evaluate(Document document, View view, Element element)
        {
            if (!document.IsWorkshared)
            {
                return VisibilityCheckItem.NotApplicable(CheckName, "Document is not workshared. Worksets do not apply.");
            }

            WorksetId worksetId = element.WorksetId;
            if (worksetId == WorksetId.InvalidWorksetId)
            {
                return VisibilityCheckItem.NotApplicable(CheckName, "Element is not assigned to a user Workset.");
            }

            Workset? workset;
            try
            {
                workset = document.GetWorksetTable().GetWorkset(worksetId);
            }
            catch (Exception ex)
            {
                return VisibilityCheckItem.Error(CheckName, $"Could not resolve Workset id {worksetId.IntegerValue}: {ex.Message}");
            }

            if (workset is null)
            {
                return VisibilityCheckItem.Error(CheckName, $"Workset id {worksetId.IntegerValue} did not resolve to a Workset.");
            }

            WorksetVisibility viewOverride;
            try
            {
                viewOverride = view.GetWorksetVisibility(worksetId);
            }
            catch (Exception ex)
            {
                return VisibilityCheckItem.Error(CheckName, $"Could not read per-view Workset visibility override: {ex.Message}");
            }

            if (viewOverride == WorksetVisibility.Hidden)
            {
                return VisibilityCheckItem.Hidden(CheckName,
                    $"Workset '{workset.Name}' is explicitly set to HIDDEN in view '{view.Name}' (per-view override).",
                    recommendation: $"View tab > Visibility/Graphics (VG) > Worksets tab, and re-enable Workset " +
                        $"'{workset.Name}' for this view.");
            }

            if (viewOverride == WorksetVisibility.Visible)
            {
                return VisibilityCheckItem.Ok(CheckName,
                    $"Workset '{workset.Name}' is explicitly set to VISIBLE in view '{view.Name}' (per-view override). " +
                    "Workset is not hiding this element.");
            }

            // WorksetVisibility.UseGlobalSetting - fall back to the document-wide default.
            bool globallyVisible;
            try
            {
                WorksetDefaultVisibilitySettings defaults = WorksetDefaultVisibilitySettings.GetWorksetDefaultVisibilitySettings(document);
                globallyVisible = defaults.IsWorksetVisible(worksetId);
            }
            catch (Exception ex)
            {
                return VisibilityCheckItem.Error(CheckName,
                    $"Could not read the document's global default visibility for Workset '{workset.Name}': {ex.Message}");
            }

            if (!globallyVisible)
            {
                return VisibilityCheckItem.Hidden(CheckName,
                    $"Workset '{workset.Name}' has no per-view override in '{view.Name}' (Use Global Setting), and its " +
                    "document-wide default visibility is OFF.",
                    recommendation: $"Either add a per-view override to show Workset '{workset.Name}' in this view, or " +
                        "change its global default visibility (Collaborate tab > Worksets).");
            }

            return VisibilityCheckItem.Ok(CheckName,
                $"Workset '{workset.Name}' has no per-view override in '{view.Name}' and its document-wide default " +
                "visibility is ON. Workset is not hiding this element.");
        }
    }
}

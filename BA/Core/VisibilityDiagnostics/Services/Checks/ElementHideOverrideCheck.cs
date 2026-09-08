// File: BA_Tools/VisibilityDiagnostics/Services/Checks/ElementHideOverrideCheck.cs
using System;
using Autodesk.Revit.DB;
using BA.VisibilityDiagnostics.Models;

namespace BA.VisibilityDiagnostics.Services.Checks
{
    /// <summary>
    /// Checks whether the element was manually hidden in this specific view via
    /// "Hide in View > Elements". Element.IsHidden(View) is scoped EXACTLY to this
    /// cause - confirmed via Autodesk's own Remarks: "This does not determine if
    /// the element is hidden as a result of temporary hide/isolate, view
    /// sectioning, view crop box, or other operations that can cause elements not
    /// to be visible." It is not a general aggregate (an earlier version of this
    /// tool incorrectly treated it as one - see VisibilityDiagnosticReport's
    /// VisibleToViewCollector for the corrected aggregate ground truth).
    /// </summary>
    public sealed class ElementHideOverrideCheck : IVisibilityCheck
    {
        public string CheckName => "Element Hide Override";

        public VisibilityCheckItem Evaluate(Document document, View view, Element element)
        {
            bool manuallyHidden;
            try
            {
                manuallyHidden = element.IsHidden(view);
            }
            catch (Exception ex)
            {
                return VisibilityCheckItem.Error(CheckName, $"Could not read the manual hide state for this element: {ex.Message}");
            }

            if (manuallyHidden)
            {
                return VisibilityCheckItem.Hidden(CheckName,
                    $"Element was manually hidden in view '{view.Name}' via Hide in View > Elements (or an equivalent " +
                    "API call to View.HideElements).",
                    recommendation: "Select the element from a schedule or another view, then Reveal Hidden Elements " +
                        "(View tab) and click Unhide Element, or call View.UnhideElements via the API.");
            }

            return VisibilityCheckItem.Ok(CheckName,
                $"Element has not been manually hidden in view '{view.Name}' via Hide in View > Elements.");
        }
    }
}

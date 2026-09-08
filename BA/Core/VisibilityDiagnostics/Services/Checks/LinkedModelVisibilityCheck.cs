// File: BA_Tools/VisibilityDiagnostics/Services/Checks/LinkedModelVisibilityCheck.cs
using BA.VisibilityDiagnostics.Models;
using Autodesk.Revit.DB;

namespace BA.VisibilityDiagnostics.Services.Checks
{
    /// <summary>
    /// Scoping note: this check only ever runs against elements that live in the
    /// active/host document - the resolver (manual Id entry, "Use Current
    /// Selection") only ever finds elements in the host document, so in that
    /// normal pipeline this check always reports NotApplicable.
    ///
    /// True cross-link diagnosis (an element that lives INSIDE a linked file) is
    /// handled by a separate, narrower path -
    /// VisibilityDiagnosticService.DiagnoseLinkedElement - triggered specifically
    /// when Pick Element selects something inside a link. That path does not run
    /// the full 11-check pipeline against the linked element, because most of
    /// these checks (Category/Phase/Design Option/Workset/View Range) assume
    /// document, view, and element all belong to the SAME document, which breaks
    /// down across a link boundary - phases and design options in particular do
    /// not have a well-defined cross-document mapping. Instead it uses
    /// Revit's own confirmed-correct link-aware collector overload
    /// (FilteredElementCollector(Document, ElementId viewId, ElementId linkId))
    /// to get a direct, real verdict without re-deriving per-cause logic that
    /// hasn't been confirmed to hold across a link.
    /// </summary>
    public sealed class LinkedModelVisibilityCheck : IVisibilityCheck
    {
        public string CheckName => "Linked Model";

        public VisibilityCheckItem Evaluate(Document document, View view, Element element)
        {
            return VisibilityCheckItem.NotApplicable(CheckName,
                "Element is not inside a linked model (it lives in the active document). This check only applies " +
                "when diagnosing an element picked from inside a Revit link - use Pick Element and select geometry " +
                "belonging to a link to trigger that path.");
        }
    }
}

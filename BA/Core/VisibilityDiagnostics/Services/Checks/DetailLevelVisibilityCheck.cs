// File: BA_Tools/VisibilityDiagnostics/Services/Checks/DetailLevelVisibilityCheck.cs
using System;
using Autodesk.Revit.DB;
using BA.VisibilityDiagnostics.Models;

namespace BA.VisibilityDiagnostics.Services.Checks
{
    /// <summary>
    /// Purely informational - never returns Hidden.
    ///
    /// Detail-Level-driven visibility is defined per-geometry INSIDE a family
    /// (Family Editor > Visibility Settings on a solid/void/line/import, tied to
    /// Coarse/Medium/Fine checkboxes), or via a subcategory's own Detail Level
    /// control in Object Styles. Neither is reliably introspectable from an
    /// already-placed instance via the public API without opening and walking the
    /// family document itself - doing that reliably for every family type
    /// (system families, loadable families, in-place families, nested families)
    /// is out of scope for a lightweight read-only check. This check reports the
    /// view's Detail Level as context only and always returns Warning, so it can
    /// never masquerade as a confirmed cause.
    /// </summary>
    public sealed class DetailLevelVisibilityCheck : IVisibilityCheck
    {
        public string CheckName => "Detail Level";

        public VisibilityCheckItem Evaluate(Document document, View view, Element element)
        {
            ViewDetailLevel detailLevel;
            try
            {
                detailLevel = view.DetailLevel;
            }
            catch (Exception ex)
            {
                return VisibilityCheckItem.Error(CheckName, $"Could not read Detail Level for view '{view.Name}': {ex.Message}");
            }

            return VisibilityCheckItem.Warning(CheckName,
                $"View '{view.Name}' Detail Level is '{detailLevel}'. If this element's family (or specific geometry " +
                "within it) has Family-Editor Visibility Settings restricted to a different Detail Level, or an Object " +
                "Styles subcategory Detail Level restriction, that would hide it here - but neither is reliably " +
                "readable from the placed instance via the public API, so this check cannot confirm or rule that out. " +
                "If no check above explains the hidden state, open the family in the Family Editor and inspect " +
                "Visibility Settings on its geometry, or check Object Styles for its subcategory.");
        }
    }
}

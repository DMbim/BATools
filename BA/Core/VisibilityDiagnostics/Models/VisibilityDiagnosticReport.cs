// File: BA_Tools/VisibilityDiagnostics/Models/VisibilityDiagnosticReport.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.VisibilityDiagnostics.Models
{
    /// <summary>
    /// Full result of running the checklist against one element/view pair.
    /// Immutable POCO - built entirely inside a valid Revit API context and then
    /// handed back across the ExternalEvent boundary to the WPF/UI thread. Holds
    /// no live Revit API object references (Element, View, Category, ...) on
    /// purpose - only primitive/plain data - so it stays safe to read from the UI
    /// thread indefinitely after the Revit-side callback that produced it returns.
    /// </summary>
    public sealed class VisibilityDiagnosticReport
    {
        public long ElementId { get; init; }
        public string ElementUniqueId { get; init; } = string.Empty;
        public string ElementCategoryName { get; init; } = string.Empty;
        public string ElementTypeName { get; init; } = string.Empty;
        public string ElementName { get; init; } = string.Empty;

        public long ViewId { get; init; }
        public string ViewName { get; init; } = string.Empty;
        public string ViewType { get; init; } = string.Empty;

        // PHASE 1 CORRECTION: this report used to carry an "AggregateIsHiddenPerApi"
        // field populated from Element.IsHidden(view), documented as "Revit's own
        // opaque aggregate." That was wrong and has been removed. Autodesk's own
        // Remarks for IsHidden(View) state it "does not determine if the element is
        // hidden as a result of temporary hide/isolate, view sectioning, view crop
        // box, or OTHER OPERATIONS that can cause elements not to be visible" - i.e.
        // it is scoped specifically to the manual "Hide in View > Elements" override,
        // not category/filter/phase/design-option/workset/view-range/crop state. That
        // narrow, CORRECT meaning is exactly what Services.Checks.ElementHideOverrideCheck
        // now reports as its own row. The real cross-check ground truth is
        // VisibleToViewCollector below.

        /// <summary>
        /// Ground-truth aggregate: true if a view-scoped FilteredElementCollector (which
        /// applies Revit's real Category/Filter/Phase/Design-Option/Workset/View-Range
        /// visibility pipeline) would include this element. Confirmed against Autodesk's
        /// official Remarks for the FilteredElementCollector(Document, ElementId viewId)
        /// constructor. One explicitly documented gap: per that same Remarks text, Crop
        /// Region exclusion is NOT reliably reflected here ("Revit relies on later
        /// processing to eliminate the elements hidden by the crop") - Crop Region has
        /// its own dedicated check for that reason; do not treat this flag as covering it.
        /// </summary>
        public bool VisibleToViewCollector { get; init; }

        public IReadOnlyList<VisibilityCheckItem> Checks { get; init; } = Array.Empty<VisibilityCheckItem>();

        public bool AnyCoreCheckReportsHidden => Checks.Any(c => c.Status == CheckStatus.Hidden);

        /// <summary>
        /// True when the view-scoped collector ground truth says this element is NOT
        /// visible, but none of the implemented checks (View Range, Category/VG, Phase
        /// Filter, Design Option, Workset, View Filter, Crop Region, Element Hide
        /// Override) explain why. Remaining honest gaps at that point: View Template lock
        /// (informational only - never itself the direct cause), Detail Level (not
        /// reliably introspectable via public API - see DetailLevelVisibilityCheck), or a
        /// geometric edge case in the View Range / Crop Region bounding-box approximation.
        /// </summary>
        public bool UnexplainedHidden => !VisibleToViewCollector && !AnyCoreCheckReportsHidden;
    }
}

// File: BA_Tools/VisibilityDiagnostics/Services/VisibilityDiagnosticService.cs
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using BA.VisibilityDiagnostics.Models;
using BA.VisibilityDiagnostics.Services.Checks;

namespace BA.VisibilityDiagnostics.Services
{
    /// <summary>
    /// Coordinates the visibility checklist: runs every registered IVisibilityCheck
    /// against one element/view pair and aggregates the results into a
    /// VisibilityDiagnosticReport. Stateless aside from the fixed check list, so a
    /// single shared instance is safe.
    ///
    /// MUST be called from a valid Revit API context (inside IExternalCommand.Execute,
    /// an IExternalEventHandler.Execute callback, or Idling) - never from WPF UI code
    /// directly. Every check runs read-only; no Transaction is opened here.
    /// </summary>
    public sealed class VisibilityDiagnosticService
    {
        public static VisibilityDiagnosticService Instance { get; } = new VisibilityDiagnosticService();

        private readonly IReadOnlyList<IVisibilityCheck> _checks;

        private VisibilityDiagnosticService()
        {
            // Full checklist (Phase 1 + Phase 2), run in this fixed order. Anything
            // still unexplained after all of these run - see
            // VisibilityDiagnosticReport.UnexplainedHidden - is most likely a
            // View-Template-locked setting, a Detail Level restriction defined inside
            // the family (see DetailLevelVisibilityCheck's documented API limits), or a
            // geometric edge case in the View Range / Crop Region bounding-box
            // approximations.
            _checks = new IVisibilityCheck[]
            {
                new ViewRangeVisibilityCheck(),
                new CategoryVisibilityCheck(),
                new PhaseFilterVisibilityCheck(),
                new DesignOptionVisibilityCheck(),
                new WorksetVisibilityCheck(),
                new ViewFilterVisibilityCheck(),
                new CropRegionVisibilityCheck(),
                new ElementHideOverrideCheck(),
                new ViewTemplateLockCheck(),
                new DetailLevelVisibilityCheck(),
                new LinkedModelVisibilityCheck(),
            };
        }

        public VisibilityDiagnosticReport Diagnose(Document document, View view, Element element)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));
            if (view is null) throw new ArgumentNullException(nameof(view));
            if (element is null) throw new ArgumentNullException(nameof(element));

            var results = new List<VisibilityCheckItem>(_checks.Count);
            foreach (IVisibilityCheck check in _checks)
            {
                VisibilityCheckItem item;
                try
                {
                    item = check.Evaluate(document, view, element);
                }
                catch (Exception ex)
                {
                    // A check throwing is a bug in that check, not a valid diagnostic
                    // outcome - surface it as an Error row instead of aborting the
                    // whole report, so the remaining checks still run.
                    item = VisibilityCheckItem.Error(check.CheckName, $"Unhandled exception evaluating this check: {ex.Message}");
                }

                results.Add(item);
            }

            // Ground-truth cross-check: a view-scoped FilteredElementCollector applies
            // Revit's real Category/Filter/Phase/Design-Option/Workset/View-Range
            // visibility pipeline (confirmed via the constructor's official Remarks).
            // Documented gap in that same Remarks text: Crop Region exclusion is NOT
            // reliably reflected here, which is exactly why Crop Region has its own
            // dedicated geometric check above rather than relying on this collector.
            bool visibleToCollector;
            try
            {
                visibleToCollector = new FilteredElementCollector(document, view.Id)
                    .WhereElementIsNotElementType()
                    .ToElementIds()
                    .Contains(element.Id);
            }
            catch
            {
                // If the collector itself fails for this view type, don't let that
                // silently claim the element is hidden - fall back to "visible" so
                // UnexplainedHidden doesn't fire on a false premise.
                visibleToCollector = true;
            }

            return new VisibilityDiagnosticReport
            {
                ElementId = element.Id.Value,
                ElementUniqueId = element.UniqueId,
                ElementCategoryName = element.Category?.Name ?? "(none)",
                ElementTypeName = ResolveTypeName(document, element),
                ElementName = string.IsNullOrEmpty(element.Name) ? string.Empty : element.Name,
                ViewId = view.Id.Value,
                ViewName = view.Name,
                ViewType = view.ViewType.ToString(),
                VisibleToViewCollector = visibleToCollector,
                Checks = results,
            };
        }

        /// <summary>
        /// Narrow path for an element that lives INSIDE a linked model, reached from
        /// VisibilityDiagnosticCommand's Pick flow when the picked Reference has a
        /// non-invalid LinkedElementId. Deliberately does NOT run the full checklist
        /// against the linked element - see LinkedModelVisibilityCheck's remarks for
        /// why (Category/Phase/Design Option/Workset/View Range semantics don't cross
        /// a link boundary cleanly). Instead uses Revit's own link-aware collector
        /// overload (FilteredElementCollector(Document, ElementId viewId, ElementId
        /// linkId), confirmed against its official Remarks) for a direct, real
        /// verdict on whether this exact linked element is visible through the link
        /// in the given host view.
        /// </summary>
        public VisibilityDiagnosticReport DiagnoseLinkedElement(
            Document hostDocument, View hostView, RevitLinkInstance linkInstance, Document linkedDocument, Element linkedElement)
        {
            if (hostDocument is null) throw new ArgumentNullException(nameof(hostDocument));
            if (hostView is null) throw new ArgumentNullException(nameof(hostView));
            if (linkInstance is null) throw new ArgumentNullException(nameof(linkInstance));
            if (linkedDocument is null) throw new ArgumentNullException(nameof(linkedDocument));
            if (linkedElement is null) throw new ArgumentNullException(nameof(linkedElement));

            string linkName = string.IsNullOrEmpty(linkInstance.Name) ? $"Link Id {linkInstance.Id.Value}" : linkInstance.Name;

            bool visible;
            try
            {
                visible = new FilteredElementCollector(hostDocument, hostView.Id, linkInstance.Id)
                    .WhereElementIsNotElementType()
                    .ToElementIds()
                    .Contains(linkedElement.Id);
            }
            catch (Exception ex)
            {
                var errorItem = VisibilityCheckItem.Error("Linked Model",
                    $"Could not evaluate visibility for the element through link '{linkName}': {ex.Message}");
                return new VisibilityDiagnosticReport
                {
                    ElementId = linkedElement.Id.Value,
                    ElementUniqueId = linkedElement.UniqueId,
                    ElementCategoryName = linkedElement.Category?.Name ?? "(none)",
                    ElementTypeName = ResolveTypeName(linkedDocument, linkedElement),
                    ElementName = string.IsNullOrEmpty(linkedElement.Name) ? string.Empty : linkedElement.Name,
                    ViewId = hostView.Id.Value,
                    ViewName = $"{hostView.Name} (via link '{linkName}')",
                    ViewType = hostView.ViewType.ToString(),
                    VisibleToViewCollector = true,
                    Checks = new[] { errorItem },
                };
            }

            VisibilityCheckItem resultItem = visible
                ? VisibilityCheckItem.Ok("Linked Model",
                    $"Revit reports this element, as seen through link '{linkName}', IS visible in host view " +
                    $"'{hostView.Name}'. (Per-cause breakdown across a link boundary is not implemented - this is a " +
                    "direct verdict from Revit's link-aware element collector, not a decomposed reason.)")
                : VisibilityCheckItem.Hidden("Linked Model",
                    $"Revit reports this element, as seen through link '{linkName}', is NOT visible in host view " +
                    $"'{hostView.Name}'. Likely causes: the host view's per-link category/workset override " +
                    "(Visibility/Graphics Overrides > Revit Links tab > Edit for this link), the link's own display " +
                    "mode (By Host View / By Linked View / Custom), or a Category/Phase/Workset/Filter setting inside " +
                    "the linked file itself.",
                    recommendation: "Check Visibility/Graphics Overrides > Revit Links tab for this view first. If the " +
                        "link's display mode is 'By Linked View' or 'Custom', open the linked file directly and run " +
                        "this same diagnostic tool there (treating it as the active document) for a full per-cause breakdown.");

            return new VisibilityDiagnosticReport
            {
                ElementId = linkedElement.Id.Value,
                ElementUniqueId = linkedElement.UniqueId,
                ElementCategoryName = linkedElement.Category?.Name ?? "(none)",
                ElementTypeName = ResolveTypeName(linkedDocument, linkedElement),
                ElementName = string.IsNullOrEmpty(linkedElement.Name) ? string.Empty : linkedElement.Name,
                ViewId = hostView.Id.Value,
                ViewName = $"{hostView.Name} (via link '{linkName}')",
                ViewType = hostView.ViewType.ToString(),
                VisibleToViewCollector = visible,
                Checks = new[] { resultItem },
            };
        }

        private static string ResolveTypeName(Document document, Element element)
        {
            ElementId typeId = element.GetTypeId();
            return typeId != ElementId.InvalidElementId && document.GetElement(typeId) is Element typeElement
                ? typeElement.Name
                : "(none)";
        }
    }
}

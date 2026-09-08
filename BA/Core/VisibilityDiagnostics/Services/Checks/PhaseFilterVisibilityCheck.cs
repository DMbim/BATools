// File: BA_Tools/VisibilityDiagnostics/Services/Checks/PhaseFilterVisibilityCheck.cs
using System;
using Autodesk.Revit.DB;
using BA.VisibilityDiagnostics.Models;

namespace BA.VisibilityDiagnostics.Services.Checks
{
    /// <summary>
    /// Checks whether the element's phase status relative to the view's Phase is
    /// invalid (element does not exist at that phase), or is valid but suppressed
    /// by the view's Phase Filter.
    ///
    /// Uses Element.GetPhaseStatus(ElementId) - Revit's own computed
    /// New/Existing/Demolished/Temporary/None status - rather than manually
    /// re-deriving it from CreatedPhaseId/DemolishedPhaseId, and PhaseFilter.
    /// GetPhaseStatusPresentation(ElementOnPhaseStatus) for the filter's per-status
    /// Normal/Overridden/DontShow setting. The PhaseStatusPresentation.DontShow
    /// member name is confirmed against a real-world code sample; the other two
    /// enum members were not independently re-verified in this session - confirm
    /// the full enum in Object Browser on first build. // <- ASSUMPTION, VERIFY
    /// </summary>
    public sealed class PhaseFilterVisibilityCheck : IVisibilityCheck
    {
        public string CheckName => "Phase / Phase Filter";

        public VisibilityCheckItem Evaluate(Document document, View view, Element element)
        {
            Parameter? viewPhaseParam = view.get_Parameter(BuiltInParameter.VIEW_PHASE);
            if (viewPhaseParam is null)
            {
                return VisibilityCheckItem.NotApplicable(CheckName,
                    $"View '{view.Name}' ({view.ViewType}) does not expose a Phase parameter. Phasing does not apply here.");
            }

            ElementId viewPhaseId = viewPhaseParam.AsElementId();
            if (viewPhaseId == ElementId.InvalidElementId)
            {
                return VisibilityCheckItem.Warning(CheckName,
                    $"View '{view.Name}' has no Phase assigned. Phase-based visibility cannot be evaluated reliably.");
            }

            ElementOnPhaseStatus phaseStatus;
            try
            {
                phaseStatus = element.GetPhaseStatus(viewPhaseId);
            }
            catch (Exception ex)
            {
                return VisibilityCheckItem.Error(CheckName, $"Could not read phase status for this element: {ex.Message}");
            }

            string viewPhaseName = document.GetElement(viewPhaseId) is Phase viewPhase ? viewPhase.Name : $"Phase {viewPhaseId.Value}";

            if (phaseStatus == ElementOnPhaseStatus.None)
            {
                return VisibilityCheckItem.Hidden(CheckName,
                    $"Element does not exist relative to view '{view.Name}''s phase ('{viewPhaseName}') - it was either " +
                    "created in a later phase than this view's phase, or was already demolished before this view's phase.",
                    recommendation: "Change the element's Phase Created / Phase Demolished, or switch this view to a phase " +
                        "in which the element exists.");
            }

            Parameter? phaseFilterParam = view.get_Parameter(BuiltInParameter.VIEW_PHASE_FILTER);
            ElementId phaseFilterId = phaseFilterParam?.AsElementId() ?? ElementId.InvalidElementId;

            if (phaseFilterId == ElementId.InvalidElementId || document.GetElement(phaseFilterId) is not PhaseFilter phaseFilter)
            {
                return VisibilityCheckItem.Ok(CheckName,
                    $"Element's phase status relative to '{viewPhaseName}' is '{phaseStatus}', and the view has no Phase " +
                    "Filter restricting that status further. Phase is not hiding this element.");
            }

            PhaseStatusPresentation presentation;
            try
            {
                presentation = phaseFilter.GetPhaseStatusPresentation(phaseStatus);
            }
            catch (Exception ex)
            {
                return VisibilityCheckItem.Error(CheckName,
                    $"Could not read Phase Filter '{phaseFilter.Name}' presentation for status '{phaseStatus}': {ex.Message}");
            }

            if (presentation == PhaseStatusPresentation.DontShow)
            {
                return VisibilityCheckItem.Hidden(CheckName,
                    $"Element's phase status relative to '{viewPhaseName}' is '{phaseStatus}', and the view's Phase Filter " +
                    $"'{phaseFilter.Name}' is set to NOT display elements with '{phaseStatus}' status.",
                    recommendation: $"Edit Phase Filter '{phaseFilter.Name}' (Manage tab > Phasing > Phase Filters) and " +
                        $"change the '{phaseStatus}' column away from 'Not Displayed', or assign this view a different Phase Filter.");
            }

            return VisibilityCheckItem.Ok(CheckName,
                $"Element's phase status relative to '{viewPhaseName}' is '{phaseStatus}', and the view's Phase Filter " +
                $"'{phaseFilter.Name}' displays that status ({presentation}). Phase is not hiding this element.");
        }
    }
}

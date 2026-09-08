// File: BA_Tools/VisibilityDiagnostics/Services/Checks/ViewTemplateLockCheck.cs
using Autodesk.Revit.DB;
using BA.VisibilityDiagnostics.Models;

namespace BA.VisibilityDiagnostics.Services.Checks
{
    /// <summary>
    /// Purely informational - never itself the reason an element is hidden.
    ///
    /// Scoping decision: Revit exposes View.GetTemplateParameterIds() /
    /// GetNonControlledTemplateParameterIds() to determine exactly which controls a
    /// View Template locks, keyed by BuiltInParameter ids such as
    /// BuiltInParameter.VIS_GRAPHICS_MODEL (confirmed against an accepted-answer
    /// Autodesk forum code sample). The equivalent ids for the Filters and
    /// Worksets tabs specifically were NOT independently confirmed against primary
    /// source in this session. Rather than mix one confirmed BuiltInParameter with
    /// two guessed ones - which would make this check silently wrong for exactly
    /// the cases it exists to catch - it stays narrative-only: it tells you a
    /// template is applied and to check the greyed-out state in the VG dialog
    /// yourself, rather than claiming to know which specific controls are locked.
    /// // <- SCOPE LIMITATION, NOT A BUG - see chat explanation
    /// </summary>
    public sealed class ViewTemplateLockCheck : IVisibilityCheck
    {
        public string CheckName => "View Template";

        public VisibilityCheckItem Evaluate(Document document, View view, Element element)
        {
            ElementId templateId = view.ViewTemplateId;
            if (templateId == ElementId.InvalidElementId)
            {
                return VisibilityCheckItem.NotApplicable(CheckName, $"No View Template is applied to view '{view.Name}'.");
            }

            string templateName = document.GetElement(templateId) is Element templateElement ? templateElement.Name : $"Id {templateId.Value}";

            return VisibilityCheckItem.Warning(CheckName,
                $"View Template '{templateName}' is applied to view '{view.Name}'. If any check above (Category, View " +
                "Filter, Workset, ...) reports Hidden, that setting may be locked by the template rather than editable " +
                "on this view directly - Revit greys out template-controlled fields in Visibility/Graphics Overrides " +
                "and the Properties palette. This automated check does not determine WHICH specific settings the " +
                "template locks (that requires per-tab BuiltInParameter ids not fully confirmed in this session) - " +
                "check the greyed-out state in the relevant dialog directly.",
                recommendation: $"If a setting above is greyed out, edit View Template '{templateName}' itself (View tab " +
                    "> View Templates > Edit/Apply Template Properties), or exclude that parameter from the template's " +
                    "control for this view (the small icon next to a template-controlled field).");
        }
    }
}

// File: BA_Tools/VisibilityDiagnostics/Services/Checks/CategoryVisibilityCheck.cs
using System;
using Autodesk.Revit.DB;
using BA.VisibilityDiagnostics.Models;

namespace BA.VisibilityDiagnostics.Services.Checks
{
    /// <summary>
    /// Checks whether the element's Category is switched off in Visibility/Graphics
    /// Overrides for the given view.
    /// </summary>
    public sealed class CategoryVisibilityCheck : IVisibilityCheck
    {
        public string CheckName => "Category / Visibility Graphics";

        public VisibilityCheckItem Evaluate(Document document, View view, Element element)
        {
            Category? category = element.Category;
            if (category is null)
            {
                return VisibilityCheckItem.NotApplicable(CheckName,
                    "Element has no Category. Category-based visibility does not apply to it.");
            }

            bool controllable;
            try
            {
                controllable = category.get_AllowsVisibilityControl(view);
            }
            catch (Exception ex)
            {
                return VisibilityCheckItem.Error(CheckName,
                    $"Could not determine whether category '{category.Name}' allows visibility control in this view: {ex.Message}");
            }

            if (!controllable)
            {
                return VisibilityCheckItem.NotApplicable(CheckName,
                    $"Category '{category.Name}' does not support per-category visibility control in a view of type " +
                    $"{view.ViewType} - it is forced on/off for this view type, independent of the V/G Overrides dialog.");
            }

            bool hidden;
            try
            {
                hidden = view.GetCategoryHidden(category.Id);
            }
            catch (Exception ex)
            {
                return VisibilityCheckItem.Error(CheckName,
                    $"Could not read category visibility state for '{category.Name}': {ex.Message}");
            }

            bool templateControlled = view.ViewTemplateId != ElementId.InvalidElementId;

            if (hidden)
            {
                string templateNote = templateControlled
                    ? " This view has a View Template applied - if the category visibility control is NOT excluded from " +
                      "the template's controlled parameters, the checkbox will be greyed out and must be changed on the " +
                      "TEMPLATE, not on this view. (Automated View-Template-lock detection is a Phase 2 check - verify " +
                      "this manually for now via Visibility/Graphics Overrides.)"
                    : string.Empty;

                return VisibilityCheckItem.Hidden(CheckName,
                    $"Category '{category.Name}' is switched OFF in Visibility/Graphics Overrides for view '{view.Name}'." + templateNote,
                    recommendation: $"Open Visibility/Graphics Overrides (VG / VV) for this view and re-enable the " +
                        $"'{category.Name}' category" + (templateControlled ? ", on the View Template if the control is locked." : "."));
            }

            return VisibilityCheckItem.Ok(CheckName,
                $"Category '{category.Name}' is switched ON in Visibility/Graphics Overrides for view '{view.Name}'.");
        }
    }
}

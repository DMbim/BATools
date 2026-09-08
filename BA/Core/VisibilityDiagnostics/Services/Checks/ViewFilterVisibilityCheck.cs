// File: BA_Tools/VisibilityDiagnostics/Services/Checks/ViewFilterVisibilityCheck.cs
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using BA.VisibilityDiagnostics.Models;

namespace BA.VisibilityDiagnostics.Services.Checks
{
    /// <summary>
    /// Checks whether a rule-based View Filter (ParameterFilterElement) applied to
    /// the view matches this element's category and rule, and is set to hide
    /// matches. All members used here (View.GetFilters/GetIsFilterEnabled/
    /// GetFilterVisibility, ParameterFilterElement.GetCategories/GetElementFilter,
    /// ElementFilter.PassesFilter(Document, ElementId)) were confirmed against
    /// Autodesk API documentation.
    /// </summary>
    public sealed class ViewFilterVisibilityCheck : IVisibilityCheck
    {
        public string CheckName => "View Filter";

        public VisibilityCheckItem Evaluate(Document document, View view, Element element)
        {
            if (element.Category is null)
            {
                return VisibilityCheckItem.NotApplicable(CheckName,
                    "Element has no Category. View Filters match by category first, so none can apply to it.");
            }

            ICollection<ElementId> filterIds;
            try
            {
                filterIds = view.GetFilters();
            }
            catch (Exception ex)
            {
                return VisibilityCheckItem.Error(CheckName, $"Could not read filters applied to view '{view.Name}': {ex.Message}");
            }

            if (filterIds.Count == 0)
            {
                return VisibilityCheckItem.Ok(CheckName, $"No View Filters are applied to view '{view.Name}'.");
            }

            var matchedVisible = new List<string>();

            foreach (ElementId filterId in filterIds)
            {
                if (document.GetElement(filterId) is not ParameterFilterElement pfe)
                {
                    continue;
                }

                bool enabled;
                bool filterVisible;
                try
                {
                    enabled = view.GetIsFilterEnabled(filterId);
                    filterVisible = view.GetFilterVisibility(filterId);
                }
                catch (Exception ex)
                {
                    return VisibilityCheckItem.Error(CheckName, $"Could not read enabled/visibility state for filter '{pfe.Name}': {ex.Message}");
                }

                if (!enabled)
                {
                    // A disabled filter's rule/visibility does not apply at all.
                    continue;
                }

                ICollection<ElementId> categories;
                try
                {
                    categories = pfe.GetCategories();
                }
                catch (Exception ex)
                {
                    return VisibilityCheckItem.Error(CheckName, $"Could not read categories for filter '{pfe.Name}': {ex.Message}");
                }

                if (!categories.Contains(element.Category.Id))
                {
                    continue;
                }

                bool passesRule;
                try
                {
                    ElementFilter? elementFilter = pfe.GetElementFilter();
                    passesRule = elementFilter is null || elementFilter.PassesFilter(document, element.Id);
                }
                catch (Exception ex)
                {
                    return VisibilityCheckItem.Error(CheckName, $"Could not evaluate the rule for filter '{pfe.Name}': {ex.Message}");
                }

                if (!passesRule)
                {
                    continue;
                }

                if (!filterVisible)
                {
                    return VisibilityCheckItem.Hidden(CheckName,
                        $"Element's category is included in View Filter '{pfe.Name}' and matches its rule, and the " +
                        $"filter's Visibility is switched OFF in view '{view.Name}'.",
                        recommendation: $"Open Visibility/Graphics Overrides (VG) > Filters tab for this view and check " +
                            $"the Visibility box for '{pfe.Name}', or edit the filter's rule so this element no longer matches.");
                }

                matchedVisible.Add(pfe.Name);
            }

            return matchedVisible.Count > 0
                ? VisibilityCheckItem.Ok(CheckName,
                    $"Element matches enabled View Filter(s) [{string.Join(", ", matchedVisible)}], all set to Visible. " +
                    "Filters are not hiding this element.")
                : VisibilityCheckItem.Ok(CheckName,
                    $"Element does not match any enabled View Filter applied to view '{view.Name}'. Filters are not hiding this element.");
        }
    }
}

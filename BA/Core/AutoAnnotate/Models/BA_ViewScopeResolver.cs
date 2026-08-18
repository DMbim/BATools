using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace BA.BIM.Core.ViewScoping
{
    public static class BA_ViewScopeResolver
    {
        public static IList<ViewPlan> Resolve(
            Document doc, BA_ViewScopeMode mode, View activeView, IList<ElementId> explicitViewIds)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            switch (mode)
            {
                case BA_ViewScopeMode.ActiveViewOnly:
                    if (activeView is ViewPlan activePlan && !activePlan.IsTemplate)
                        return new List<ViewPlan> { activePlan };
                    return new List<ViewPlan>();

                case BA_ViewScopeMode.SelectedViews:
                    if (explicitViewIds == null || explicitViewIds.Count == 0)
                        return new List<ViewPlan>();
                    return explicitViewIds
                        .Select(id => doc.GetElement(id) as ViewPlan)
                        .Where(v => v != null && !v.IsTemplate)
                        .ToList();

                case BA_ViewScopeMode.AllFloorPlans:
                    return new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewPlan))
                        .Cast<ViewPlan>()
                        .Where(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan && v.CanBePrinted)
                        .ToList();

                default:
                    return new List<ViewPlan>();
            }
        }
    }
}
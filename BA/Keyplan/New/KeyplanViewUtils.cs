using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Keyplan
{
    public static class KeyplanViewUtils
    {
        public static View FindViewByName(Document doc, string name)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(name)) return null;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v != null)
                .FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public static View FindTemplateByName(Document doc, string name, ViewType? requiredViewType = null)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(name)) return null;

            IEnumerable<View> views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v != null && v.IsTemplate);

            if (requiredViewType.HasValue)
            {
                views = views.Where(v => v.ViewType == requiredViewType.Value);
            }

            return views.FirstOrDefault(v =>
                string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public static ViewDrafting CreateDraftingView(Document doc, string name)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("View name is required.", nameof(name));

            ViewFamilyType draftingType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.Drafting);

            if (draftingType == null)
            {
                throw new InvalidOperationException("No drafting view family type was found in the document.");
            }

            ViewDrafting draftingView = ViewDrafting.Create(doc, draftingType.Id);
            if (draftingView == null)
            {
                throw new InvalidOperationException("Failed to create drafting view.");
            }

            draftingView.Name = name;
            return draftingView;
        }

        public static bool IsSupported2DSourceView(View view)
        {
            if (view == null) return false;
            if (view.IsTemplate) return false;

            switch (view.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.EngineeringPlan:
                case ViewType.AreaPlan:
                case ViewType.Elevation:
                case ViewType.Section:
                case ViewType.Detail:
                case ViewType.DraftingView:
                    return true;

                default:
                    return false;
            }
        }
    }
}
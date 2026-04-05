using Autodesk.Revit.DB;
using System;
using System.Linq;

namespace BA.UI.KeyplanGrid
{
    public static class KeyplanViewService
    {
        public static View FindViewByName(Document doc, string name)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(name)) return null;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public static ViewDrafting FindOrCreateDraftingView(Document doc, string name)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            ViewDrafting existing = FindViewByName(doc, name) as ViewDrafting;
            if (existing != null)
                return existing;

            ViewFamilyType draftingType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.Drafting);

            if (draftingType == null)
                throw new InvalidOperationException("No drafting view family type found.");

            ViewDrafting draftingView = ViewDrafting.Create(doc, draftingType.Id);
            draftingView.Name = name;
            return draftingView;
        }

        public static void ApplyTemplateIfPossible(Document doc, View view, string templateName)
        {
            if (doc == null || view == null || string.IsNullOrWhiteSpace(templateName))
                return;

            View template = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v => v.IsTemplate && string.Equals(v.Name, templateName, StringComparison.OrdinalIgnoreCase));

            if (template == null)
                return;

            if (view.IsValidViewTemplate(template.Id))
            {
                view.ViewTemplateId = template.Id;
            }
        }
    }
}
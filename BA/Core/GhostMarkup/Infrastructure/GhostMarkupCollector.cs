// BA/Core/GhostMarkup/GhostMarkupCollector.cs
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace BA.Core.GhostMarkup
{
    /// <summary>
    /// Resolves which ghost markup elements need to be hidden for a given
    /// export target, and in which views. Uses a document wide collector
    /// filtered by OwnerViewId rather than FilteredElementCollector(doc,
    /// viewId), because the view scoped collector constructor silently
    /// excludes elements that are already hidden in that view, which would
    /// make already hidden ghost markup elements invisible to this pass.
    ///
    /// For Sheets, walks every placed Viewport in addition to the sheet
    /// itself, since Text Notes and Detail Items drawn on the underlying
    /// plan, section, or detail view are owned by that view, not the sheet.
    /// </summary>
    public static class GhostMarkupCollector
    {
        private static readonly List<BuiltInCategory> ScopedCategories = new List<BuiltInCategory>
        {
            BuiltInCategory.OST_TextNotes,
            BuiltInCategory.OST_Lines,
            BuiltInCategory.OST_DetailComponents
        };

        public static Dictionary<ElementId, List<ElementId>> CollectForSheet(Document doc, ViewSheet sheet)
        {
            var result = new Dictionary<ElementId, List<ElementId>>();

            AddGhostElementsForView(doc, sheet.Id, result);

            foreach (var viewportId in sheet.GetAllViewports())
            {
                if (doc.GetElement(viewportId) is not Viewport viewport)
                {
                    continue;
                }

                var viewId = viewport.ViewId;
                if (viewId == ElementId.InvalidElementId)
                {
                    continue;
                }

                AddGhostElementsForView(doc, viewId, result);
            }

            return result;
        }

        public static Dictionary<ElementId, List<ElementId>> CollectForView(Document doc, View view)
        {
            var result = new Dictionary<ElementId, List<ElementId>>();
            AddGhostElementsForView(doc, view.Id, result);
            return result;
        }

        private static void AddGhostElementsForView(
            Document doc,
            ElementId viewId,
            Dictionary<ElementId, List<ElementId>> result)
        {
            var multiCategoryFilter = new ElementMulticategoryFilter(ScopedCategories);

            var candidateIds = new FilteredElementCollector(doc)
                .WherePasses(multiCategoryFilter)
                .WhereElementIsNotElementType()
                .Where(e => e.OwnerViewId == viewId)
                .Where(e => GhostMarkupDetector.IsGhostMarkup(e, doc))
                .Select(e => e.Id)
                .ToList();

            if (candidateIds.Count == 0)
            {
                return;
            }

            if (result.TryGetValue(viewId, out var existing))
            {
                existing.AddRange(candidateIds);
            }
            else
            {
                result[viewId] = candidateIds;
            }
        }
    }
}
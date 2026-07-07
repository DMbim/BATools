using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using BA.BIM.Core.Annotations;

namespace BA.BIM.Commands.Anno
{
    [Transaction(TransactionMode.Manual)]
    public class TagAllSelectedCommand : IExternalCommand
    {
        // Annotation categories considered as "obstacles" a new tag can collide with.
        // Covers tags, text, dimensions, generic annotations, and BA symbol families
        // (which are placed as generic annotation or detail item instances).
        private static readonly BuiltInCategory[] ObstacleCategories = new[]
        {
            BuiltInCategory.OST_Tags,
            BuiltInCategory.OST_MultiCategoryTags,
            BuiltInCategory.OST_TextNotes,
            BuiltInCategory.OST_Dimensions,
            BuiltInCategory.OST_GenericAnnotation,
            BuiltInCategory.OST_DetailComponents,

            // Per-category tag categories - these are the actual categories
            // IndependentTag instances live in for built in categories such as
            // doors/windows/rooms/etc. Most fall under OST_*Tags naming.
            BuiltInCategory.OST_DoorTags,
            BuiltInCategory.OST_WindowTags,
            BuiltInCategory.OST_WallTags,
            BuiltInCategory.OST_RoomTags,
            BuiltInCategory.OST_AreaTags,
            BuiltInCategory.OST_FurnitureTags,
            BuiltInCategory.OST_FurnitureSystemTags,
            BuiltInCategory.OST_CeilingTags,
            BuiltInCategory.OST_FloorTags,
            BuiltInCategory.OST_StructuralColumnTags,
            BuiltInCategory.OST_StructuralFramingTags,
            BuiltInCategory.OST_MEPSpaceTags,
            BuiltInCategory.OST_PlumbingFixtureTags,
            BuiltInCategory.OST_ElectricalEquipmentTags,
            BuiltInCategory.OST_LightingFixtureTags,
            BuiltInCategory.OST_CaseworkTags,
            BuiltInCategory.OST_SpecialityEquipmentTags,
            BuiltInCategory.OST_GenericModelTags,
            BuiltInCategory.OST_PartTags,
            BuiltInCategory.OST_StairsTags,
            BuiltInCategory.OST_RailingSystemTags,
            BuiltInCategory.OST_CurtainWallPanelTags,
            BuiltInCategory.OST_MaterialTags,
        };

        public Result Execute(ExternalCommandData c, ref string message, ElementSet elements)
        {
            UIApplication uiapp = c.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            try
            {
                // ---------------- 1. Selection ----------------

                IList<Reference> picked = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new TaggableElementSelectionFilter(view.Id),
                    "Select elements to tag. ESC to cancel.");

                var pickedElems = picked
                    .Select(r => doc.GetElement(r))
                    .Where(e => e != null)
                    .Distinct(new ElementIdComparer())
                    .ToList();

                if (pickedElems.Count == 0)
                {
                    TaskDialog.Show("BA", "No elements selected.");
                    return Result.Cancelled;
                }

                // ---------------- 2. Group by category, resolve valid tag types ----------------

                var byCategory = pickedElems
                    .Where(e => e.Category != null)
                    .GroupBy(e => e.Category.Id.Value)
                    .ToList();

                var categoryTagOptions = new Dictionary<long, CategoryTagOptions>();

                foreach (var grp in byCategory)
                {
                    var sampleElem = grp.First();
                    Category cat = sampleElem.Category;

                    var tagTypes = GetValidTagTypes(doc, cat);

                    if (tagTypes.Count == 0)
                    {
                        // No tag family available for this category - excluded from
                        // the dialog entirely, reported as skipped at the end.
                        continue;
                    }

                    categoryTagOptions[grp.Key] = new CategoryTagOptions
                    {
                        Category = cat,
                        Elements = grp.ToList(),
                        AvailableTagTypes = tagTypes,
                        DefaultTagTypeId = GetActiveTagTypeIdForCategory(view, cat, tagTypes)
                    };
                }

                if (categoryTagOptions.Count == 0)
                {
                    TaskDialog.Show("BA", "None of the selected categories have an available tag type/family loaded in the project.");
                    return Result.Cancelled;
                }

                // ---------------- 3. Dialog: tag type per category + leader + gap/margin ----------------

                var dlgResult = TagAllSelectedDialog.GetSettings(categoryTagOptions.Values.ToList());
                if (dlgResult == null)
                    return Result.Cancelled;

                // ---------------- 4. Build worklist (skip already-tagged) ----------------

                var alreadyTaggedIds = GetAlreadyTaggedElementIds(doc, view);

                var report = new TagAllReport();
                var workList = new List<(Element Element, ElementId TagTypeId)>();

                foreach (var kvp in categoryTagOptions)
                {
                    long catKey = kvp.Key;
                    var opts = kvp.Value;

                    if (!dlgResult.SelectedTagTypeIdByCategoryKey.TryGetValue(catKey, out ElementId tagTypeId) || tagTypeId == null)
                        continue;

                    foreach (var e in opts.Elements)
                    {
                        if (alreadyTaggedIds.Contains(e.Id.Value))
                        {
                            report.SkippedAlreadyTagged++;
                            continue;
                        }

                        workList.Add((e, tagTypeId));
                    }
                }

                report.Total = pickedElems.Count;

                if (workList.Count == 0)
                {
                    TaskDialog.Show("BA - Tag All",
                        $"Nothing to tag.\nAlready tagged: {report.SkippedAlreadyTagged}");
                    return Result.Cancelled;
                }

                // ---------------- 5. Create tags + resolve collisions ----------------

                ViewPlane2D plane = ViewPlane2D.FromView(view);

                using (Transaction t = new Transaction(doc, "Tag All Selected"))
                {
                    t.Start();

                    // Running obstacle list: existing view-specific annotations,
                    // refreshed as we go (newly created/moved tags get appended).
                    var obstacles = CollectObstacleItems(doc, view);

                    foreach (var (element, tagTypeId) in workList)
                    {
                        IndependentTag newTag;

                        try
                        {
                            newTag = CreateTag(doc, view, element, tagTypeId, dlgResult.UseLeader);
                        }
                        catch (Exception ex)
                        {
                            report.FailedToCreate++;
                            report.FailReasons[ex.GetType().Name] = report.FailReasons.GetValueOrDefault(ex.GetType().Name) + 1;
                            continue;
                        }

                        if (newTag == null)
                        {
                            report.FailedToCreate++;
                            continue;
                        }

                        report.Created++;

                        var newTagBb = newTag.get_BoundingBox(view);
                        if (newTagBb == null)
                        {
                            // can't evaluate collisions without a bbox - leave at default position
                            continue;
                        }

                        var newTagRect = AnnoGeometry.GetRectInViewPlane(plane, newTagBb);
                        double margin = AnnoGeometry.AutoMargin(newTagRect);
                        var newTagRectInflated = newTagRect.Inflate(margin);

                        // Find colliding obstacles (excluding the tag itself, which
                        // CollectObstacleItems may include if it was just appended -
                        // it isn't yet, appended after this block).
                        var colliding = new List<AnnoItem>();
                        foreach (var ob in obstacles)
                        {
                            if (ob.Element.Id == newTag.Id)
                                continue;

                            var obBb = ob.Element.get_BoundingBox(view);
                            if (obBb == null)
                                continue;

                            var obRect = AnnoGeometry.GetRectInViewPlane(plane, obBb);
                            double obMargin = AnnoGeometry.AutoMargin(obRect);
                            var obRectInflated = obRect.Inflate(obMargin);

                            if (newTagRectInflated.IntersectsInclusive(obRectInflated))
                                colliding.Add(ob);
                        }

                        if (colliding.Count > 0)
                        {
                            ResolveTagCollision(doc, view, plane, newTag, newTagRect, margin, colliding, dlgResult);
                            report.CollidedAndResolved++;
                        }

                        // Append the new tag to the obstacle pool so subsequent tags
                        // in this run also avoid it.
                        var refreshedBb = newTag.get_BoundingBox(view);
                        if (refreshedBb != null)
                            obstacles.Add(new AnnoItem(newTag, refreshedBb));
                    }

                    t.Commit();
                }

                TaskDialog.Show("BA - Tag All report",
                    $"Selected: {report.Total}\n" +
                    $"Created: {report.Created}\n" +
                    $"Already tagged (skipped): {report.SkippedAlreadyTagged}\n" +
                    $"Collided and resolved: {report.CollidedAndResolved}\n" +
                    $"Failed to create: {report.FailedToCreate}");

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                return Result.Failed;
            }
        }

        // ---------------- Tag creation ----------------

        private static IndependentTag CreateTag(Document doc, View view, Element element, ElementId tagTypeId, bool useLeader)
        {
            if (!AnnoLocation.TryGetRepresentativePoint(view, element, out XYZ p))
                return null;

            Reference reference = new Reference(element);

            // IndependentTag.Create (Revit 2022+) signature:
            // Create(Document, ElementId tagTypeId, ElementId viewId, Reference reference,
            //        bool addLeader, TagOrientation orientation, XYZ point)
            IndependentTag tag = IndependentTag.Create(
                doc,
                tagTypeId,
                view.Id,
                reference,
                useLeader,
                TagOrientation.Horizontal,
                p);

            return tag;
        }

        // ---------------- Collision resolution for a single new tag ----------------

        private static void ResolveTagCollision(
            Document doc,
            View view,
            ViewPlane2D plane,
            IndependentTag newTag,
            Rect2D newTagRectInflated,
            double newTagMargin,
            List<AnnoItem> colliding,
            TagAllSettingsResult settings)
        {
            // Build working set: [0] = new tag, [1..] = colliding obstacles.
            // Use inflated rects for the simulation (consistent margins, matches
            // ResolveCollisions behavior).
            var workingRects = new List<Rect2D> { newTagRectInflated };
            var workingElements = new List<Element> { newTag };

            foreach (var ob in colliding)
            {
                var bb = ob.Element.get_BoundingBox(view);
                if (bb == null) continue;

                var r = AnnoGeometry.GetRectInViewPlane(plane, bb);
                double m = AnnoGeometry.AutoMargin(r);
                workingRects.Add(r.Inflate(m));
                workingElements.Add(ob.Element);
            }

            if (workingRects.Count < 2)
                return;

            UV[] deltas = AnnoArrangeOps.RunMtvSimulation(workingRects, settings.Iterations, settings.Damping);

            // Apply deltas to Revit elements.
            for (int i = 0; i < workingElements.Count; i++)
            {
                var d = deltas[i];
                if (Math.Abs(d.U) < 1e-9 && Math.Abs(d.V) < 1e-9)
                    continue;

                var deltaXyz = plane.DeltaToXYZ(d);
                AnnoMove.TryMoveBy(doc, workingElements[i], deltaXyz, out _);
            }

            // ---------------- Alignment pass ----------------
            // Align the new tag's perpendicular axis to the center of the obstacle
            // it overlapped most (by pre-MTV inflated-rect intersection area).

            int biggestOverlapIdx = -1;
            double biggestOverlapArea = -1;

            for (int i = 1; i < workingRects.Count; i++)
            {
                double area = IntersectionArea(workingRects[0], workingRects[i]);
                if (area > biggestOverlapArea)
                {
                    biggestOverlapArea = area;
                    biggestOverlapIdx = i;
                }
            }

            if (biggestOverlapIdx < 0)
                return;

            // Refresh post-MTV rects from the actual Revit geometry (the simulation
            // worked on simplified rects; refreshing avoids drift from the simplification).
            var newTagBbPostMtv = newTag.get_BoundingBox(view);
            var obstacleBbPostMtv = workingElements[biggestOverlapIdx].get_BoundingBox(view);

            if (newTagBbPostMtv == null || obstacleBbPostMtv == null)
                return;

            var newTagRectPostMtv = AnnoGeometry.GetRectInViewPlane(plane, newTagBbPostMtv);
            var obstacleRectPostMtv = AnnoGeometry.GetRectInViewPlane(plane, obstacleBbPostMtv);

            UV alignDelta = AnnoArrangeOps.ComputeAlignmentDelta(newTagRectPostMtv, obstacleRectPostMtv, deltas[0]);

            if (Math.Abs(alignDelta.U) < 1e-9 && Math.Abs(alignDelta.V) < 1e-9)
                return;

            var alignDeltaXyz = plane.DeltaToXYZ(alignDelta);
            AnnoMove.TryMoveBy(doc, newTag, alignDeltaXyz, out _);
        }

        private static double IntersectionArea(Rect2D a, Rect2D b)
        {
            double left = Math.Max(a.MinX, b.MinX);
            double right = Math.Min(a.MaxX, b.MaxX);
            double bottom = Math.Max(a.MinY, b.MinY);
            double top = Math.Min(a.MaxY, b.MaxY);

            double w = right - left;
            double h = top - bottom;

            if (w <= 0 || h <= 0)
                return 0;

            return w * h;
        }

        // ---------------- Obstacle gathering ----------------

        private static List<AnnoItem> CollectObstacleItems(Document doc, View view)
        {
            var multiCatFilter = new ElementMulticategoryFilter(ObstacleCategories);

            var elems = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .WherePasses(multiCatFilter)
                .Where(e => e.ViewSpecific)
                .ToList();

            var items = new List<AnnoItem>();
            foreach (var e in elems)
            {
                var bb = e.get_BoundingBox(view);
                if (bb == null) continue;

                items.Add(new AnnoItem(e, bb));
            }

            return items;
        }

        // ---------------- Already-tagged check ----------------

        private static HashSet<long> GetAlreadyTaggedElementIds(Document doc, View view)
        {
            var result = new HashSet<long>();

            var tags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>();

            foreach (var tag in tags)
            {
                ICollection<ElementId> taggedIds = tag.GetTaggedLocalElementIds();

                foreach (var id in taggedIds)
                    result.Add(id.Value);
            }

            return result;
        }

        // ---------------- Tag type resolution ----------------

        // Revit's API does not expose a generic "model category -> tag category"
        // lookup. The mapping below covers the common architectural categories.
        // Anything not in this map falls back to OST_MultiCategoryTags (tag families
        // placed in this category can tag elements of arbitrary categories via
        // CategorySpecific=false family parameter).
        private static readonly Dictionary<BuiltInCategory, BuiltInCategory[]> CategoryToTagCategories =
            new()
            {
                { BuiltInCategory.OST_Doors, new[] { BuiltInCategory.OST_DoorTags } },
                { BuiltInCategory.OST_Windows, new[] { BuiltInCategory.OST_WindowTags } },
                { BuiltInCategory.OST_Walls, new[] { BuiltInCategory.OST_WallTags } },
                { BuiltInCategory.OST_Rooms, new[] { BuiltInCategory.OST_RoomTags } },
                { BuiltInCategory.OST_Areas, new[] { BuiltInCategory.OST_AreaTags } },
                { BuiltInCategory.OST_Furniture, new[] { BuiltInCategory.OST_FurnitureTags } },
                { BuiltInCategory.OST_FurnitureSystems, new[] { BuiltInCategory.OST_FurnitureSystemTags } },
                { BuiltInCategory.OST_Ceilings, new[] { BuiltInCategory.OST_CeilingTags } },
                { BuiltInCategory.OST_Floors, new[] { BuiltInCategory.OST_FloorTags } },
                { BuiltInCategory.OST_StructuralColumns, new[] { BuiltInCategory.OST_StructuralColumnTags } },
                { BuiltInCategory.OST_StructuralFraming, new[] { BuiltInCategory.OST_StructuralFramingTags } },
                { BuiltInCategory.OST_MEPSpaces, new[] { BuiltInCategory.OST_MEPSpaceTags } },
                { BuiltInCategory.OST_PlumbingFixtures, new[] { BuiltInCategory.OST_PlumbingFixtureTags } },
                { BuiltInCategory.OST_ElectricalEquipment, new[] { BuiltInCategory.OST_ElectricalEquipmentTags } },
                { BuiltInCategory.OST_LightingFixtures, new[] { BuiltInCategory.OST_LightingFixtureTags } },
                { BuiltInCategory.OST_Casework, new[] { BuiltInCategory.OST_CaseworkTags } },
                { BuiltInCategory.OST_SpecialityEquipment, new[] { BuiltInCategory.OST_SpecialityEquipmentTags } },
                { BuiltInCategory.OST_GenericModel, new[] { BuiltInCategory.OST_GenericModelTags } },
                { BuiltInCategory.OST_Stairs, new[] { BuiltInCategory.OST_StairsTags } },
                { BuiltInCategory.OST_Railings, new[] { BuiltInCategory.OST_RailingSystemTags } },
                { BuiltInCategory.OST_CurtainWallPanels, new[] { BuiltInCategory.OST_CurtainWallPanelTags } },
            };

        private static List<FamilySymbol> GetValidTagTypes(Document doc, Category targetCategory)
        {
            var result = new List<FamilySymbol>();

            var tagCategorySet = new HashSet<long>();

            // 1. Specific tag category(ies) for this model category, if mapped.
            if (CategoryToTagCategories.TryGetValue((BuiltInCategory)(int)targetCategory.Id.Value, out var tagBuiltIns))
            {
                foreach (var tagBic in tagBuiltIns)
                {
                    var tagCat = Category.GetCategory(doc, tagBic);
                    if (tagCat != null)
                        tagCategorySet.Add(tagCat.Id.Value);
                }
            }

            // 2. Always also offer multi-category tags - these can tag any category.
            var multiCat = Category.GetCategory(doc, BuiltInCategory.OST_MultiCategoryTags);
            if (multiCat != null)
                tagCategorySet.Add(multiCat.Id.Value);

            if (tagCategorySet.Count == 0)
                return result;

            var allTagSymbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null && tagCategorySet.Contains(fs.Category.Id.Value));

            result.AddRange(allTagSymbols);

            return result;
        }

        private static ElementId GetActiveTagTypeIdForCategory(View view, Category targetCategory, List<FamilySymbol> availableTagTypes)
        {
            // Revit does not expose a direct "active tag type per category" API for
            // arbitrary categories. Try the category-specific tag category's default
            // family type first (set via "Manage > Settings > Object Styles" /
            // loaded family defaults); fall back to the first available tag type
            // (which, by construction in GetValidTagTypes, is a category-specific
            // tag type if one exists, since the map is iterated before
            // OST_MultiCategoryTags is appended... NOTE: HashSet does not preserve
            // insertion order reliably for iteration via FilteredElementCollector
            // ordering either, so "first available" is best-effort, not guaranteed
            // to prefer category-specific over multi-category. See remarks below.)
            if (availableTagTypes.Count == 0)
                return ElementId.InvalidElementId;

            if (CategoryToTagCategories.TryGetValue((BuiltInCategory)(int)targetCategory.Id.Value, out var tagBuiltIns))
            {
                foreach (var tagBic in tagBuiltIns)
                {
                    var tagCat = Category.GetCategory(view.Document, tagBic);
                    if (tagCat == null) continue;

                    var defaultId = view.Document.GetDefaultFamilyTypeId(tagCat.Id);
                    if (defaultId != ElementId.InvalidElementId &&
                        availableTagTypes.Any(t => t.Id == defaultId))
                    {
                        return defaultId;
                    }

                    // No default set, but at least one category-specific tag type
                    // exists - prefer it over a multi-category tag as the fallback.
                    var firstCategorySpecific = availableTagTypes.FirstOrDefault(t => t.Category?.Id == tagCat.Id);
                    if (firstCategorySpecific != null)
                        return firstCategorySpecific.Id;
                }
            }

            return availableTagTypes[0].Id;
        }

        // ---------------- Helper types ----------------

        private sealed class ElementIdComparer : IEqualityComparer<Element>
        {
            public bool Equals(Element x, Element y)
                => (x?.Id?.Value ?? -1) == (y?.Id?.Value ?? -2);

            public int GetHashCode(Element obj)
                => unchecked((int)(obj?.Id?.Value ?? 0));
        }

        private sealed class TagAllReport
        {
            public int Total { get; set; }
            public int Created { get; set; }
            public int SkippedAlreadyTagged { get; set; }
            public int CollidedAndResolved { get; set; }
            public int FailedToCreate { get; set; }
            public Dictionary<string, int> FailReasons { get; } = new();
        }
    }

    /// <summary>
    /// Per-category info shown in the Tag All dialog: which elements were selected
    /// for this category, and which tag types/families can be used to tag them.
    /// </summary>
    public sealed class CategoryTagOptions
    {
        public Category Category { get; set; }
        public List<Element> Elements { get; set; }
        public List<FamilySymbol> AvailableTagTypes { get; set; }
        public ElementId DefaultTagTypeId { get; set; }
    }

    /// <summary>
    /// Result of the Tag All settings dialog.
    /// </summary>
    public sealed class TagAllSettingsResult
    {
        // Category.Id.Value -> chosen tag FamilySymbol ElementId
        public Dictionary<long, ElementId> SelectedTagTypeIdByCategoryKey { get; set; } = new();

        public bool UseLeader { get; set; }

        // MTV resolution settings, reused from arrange config conventions
        public double Gap { get; set; }                 // internal units
        public int Iterations { get; set; } = 30;
        public double Damping { get; set; } = 0.75;
    }

    internal class TaggableElementSelectionFilter : ISelectionFilter
    {
        private readonly ElementId _viewId;
        public TaggableElementSelectionFilter(ElementId viewId) => _viewId = viewId;

        public bool AllowElement(Element elem)
        {
            if (elem == null) return false;
            if (elem.Category == null) return false;

            // Exclude annotation-ish elements - this command tags MODEL elements,
            // it does not let the user select existing tags/text/etc. as tag targets.
            if (elem.ViewSpecific) return false;

            if (elem.Pinned) return false;

            return true;
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
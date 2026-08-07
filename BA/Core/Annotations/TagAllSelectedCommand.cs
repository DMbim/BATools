using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;

using BA.BIM.Core.Annotations;

namespace BA.BIM.Commands.Anno
{
    [Transaction(TransactionMode.Manual)]
    public class TagAllSelectedCommand : IExternalCommand
    {
        private static readonly BuiltInCategory[] ObstacleCategories = new[]
        {
            BuiltInCategory.OST_Tags,
            BuiltInCategory.OST_MultiCategoryTags,
            BuiltInCategory.OST_TextNotes,
            BuiltInCategory.OST_Dimensions,
            BuiltInCategory.OST_GenericAnnotation,
            BuiltInCategory.OST_DetailComponents,

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
                // ---------------- 1. Use current selection (no pick prompt) ----------------
                // Reuses TaggableElementSelectionFilter's AllowElement logic directly so
                // pre-selected elements are validated against the exact same rules manual
                // picking enforced before (must have a category, must not be view specific,
                // must not be pinned).

                ICollection<ElementId> preSelectedIds = uidoc.Selection.GetElementIds();

                if (preSelectedIds == null || preSelectedIds.Count == 0)
                {
                    TaskDialog.Show("BA", "Select elements to tag first, then run this command.");
                    return Result.Cancelled;
                }

                var taggableFilter = new TaggableElementSelectionFilter(view.Id);

                List<Element> workingSelection = preSelectedIds
                    .Select(id => doc.GetElement(id))
                    .Where(e => e != null && taggableFilter.AllowElement(e))
                    .Distinct(new ElementIdComparer())
                    .ToList();

                if (workingSelection.Count == 0)
                {
                    TaskDialog.Show("BA", "Nothing in the current selection can be tagged. " +
                        "View-specific and pinned elements are excluded. Select model elements first, then run this command.");
                    return Result.Cancelled;
                }

                // ---------------- 2. Filter / Select all instances / Proceed loop ----------------
                //
                // The dialog can hand control back with an expansion request instead of
                // final settings. PostCommand cannot be used for Revit's native Select All
                // Instances here: it only runs after this Execute() returns, cannot accept
                // parameters, and re-triggers full manual UI interaction, none of which
                // fits a modal command that needs the result immediately. So this replicates
                // the same outcome directly: same family+type as anything currently selected,
                // scoped to either the active view or the whole document.

                Dictionary<long, CategoryTagOptions> categoryTagOptions = null;
                TagAllSettingsResult dlgResult = null;

                while (true)
                {
                    categoryTagOptions = BuildCategoryTagOptions(doc, view, workingSelection);

                    if (categoryTagOptions.Count == 0)
                    {
                        TaskDialog.Show("BA", "None of the selected categories have an available tag type/family loaded in the project.");
                        return Result.Cancelled;
                    }

                    var dlgOutcome = TagAllSelectedDialog.GetResult(categoryTagOptions.Values.ToList());

                    if (dlgOutcome.Action == TagAllDialogAction.Cancel)
                        return Result.Cancelled;

                    if (dlgOutcome.Action == TagAllDialogAction.Proceed)
                    {
                        dlgResult = dlgOutcome.Settings;
                        break;
                    }

                    bool entireProject = dlgOutcome.Action == TagAllDialogAction.ExpandEntireProject;
                    var expanded = ExpandSelectionToAllInstances(doc, view, workingSelection, entireProject);

                    if (expanded.Count == 0)
                    {
                        TaskDialog.Show("BA", "No matching instances found.");
                        continue; // reopen the dialog with the selection unchanged
                    }

                    workingSelection = expanded;
                }

                if (dlgResult == null || dlgResult.SelectedTagTypeIdByCategoryKey.Count == 0)
                {
                    TaskDialog.Show("BA", "No categories selected to tag.");
                    return Result.Cancelled;
                }

                // ---------------- 3. Build worklist (skip already-tagged) ----------------

                var alreadyTaggedIds = GetAlreadyTaggedElementIds(doc, view);

                var report = new TagAllReport { Total = workingSelection.Count };
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

                if (workList.Count == 0)
                {
                    TaskDialog.Show("BA - Tag All",
                        $"Nothing to tag.\nAlready tagged: {report.SkippedAlreadyTagged}");
                    return Result.Cancelled;
                }

                // ---------------- 4. Create tags + resolve collisions ----------------

                ViewPlane2D plane = ViewPlane2D.FromView(view);

                using (Transaction t = new Transaction(doc, "Tag All Selected"))
                {
                    t.Start();

                    var preexistingObstacles = CollectObstacleItems(doc, view);
                    var obstacles = new List<AnnoItem>(preexistingObstacles);
                    var newTagItems = new List<AnnoItem>();

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
                            continue;
                        }

                        var newTagRect = AnnoGeometry.GetRectInViewPlane(plane, newTagBb);

                        double margin = dlgResult.Gap;
                        var newTagRectInflated = newTagRect.Inflate(margin);

                        var colliding = new List<AnnoItem>();
                        foreach (var ob in obstacles)
                        {
                            if (ob.Element.Id == newTag.Id)
                                continue;

                            var obBb = ob.Element.get_BoundingBox(view);
                            if (obBb == null)
                                continue;

                            var obRect = AnnoGeometry.GetRectInViewPlane(plane, obBb);
                            double obMargin = dlgResult.Gap;
                            var obRectInflated = obRect.Inflate(obMargin);

                            if (newTagRectInflated.IntersectsInclusive(obRectInflated))
                                colliding.Add(ob);
                        }

                        if (colliding.Count > 0)
                        {
                            ResolveTagCollision(doc, view, plane, newTag, newTagRectInflated, margin, colliding, dlgResult);
                            report.CollidedAndResolved++;
                        }

                        var refreshedBb = newTag.get_BoundingBox(view);
                        if (refreshedBb != null)
                        {
                            var newTagAnnoItem = new AnnoItem(newTag, refreshedBb);
                            obstacles.Add(newTagAnnoItem);
                            newTagItems.Add(newTagAnnoItem);
                        }
                    }

                    if (newTagItems.Count > 0)
                    {
                        RunFinalGlobalRecheck(doc, view, plane, newTagItems, preexistingObstacles, dlgResult, report);
                    }

                    t.Commit();
                }

                TaskDialog.Show("BA - Tag All report",
                    $"Selected: {report.Total}\n" +
                    $"Created: {report.Created}\n" +
                    $"Already tagged (skipped): {report.SkippedAlreadyTagged}\n" +
                    $"Collided and resolved: {report.CollidedAndResolved}\n" +
                    $"Failed to create: {report.FailedToCreate}\n" +
                    $"Final recheck, elements moved: {report.FinalPassMoved}\n" +
                    $"Final recheck, remaining collisions: {report.FinalPassRemainingCollisions}");

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

        // ---------------- Category grouping ----------------

        private static Dictionary<long, CategoryTagOptions> BuildCategoryTagOptions(Document doc, View view, List<Element> elements)
        {
            var byCategory = elements
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
                    continue;

                categoryTagOptions[grp.Key] = new CategoryTagOptions
                {
                    Category = cat,
                    Elements = grp.ToList(),
                    AvailableTagTypes = tagTypes,
                    DefaultTagTypeId = GetActiveTagTypeIdForCategory(view, cat, tagTypes)
                };
            }

            return categoryTagOptions;
        }

        // ---------------- Select all instances ----------------
        //
        // Matches Revit's own "Select All Instances" semantics: same family AND type
        // as anything currently in the working selection, unioned across every distinct
        // type present. "Visible in view" scopes the collector to the active view.
        // "Entire project" scopes it to the whole document; instances that come back
        // from elsewhere with no representation in the active view will simply fail to
        // create later and land in the Failed to create count, same as any other element
        // Revit can't place a tag reference for in this view.

        private static List<Element> ExpandSelectionToAllInstances(Document doc, View view, List<Element> seedElements, bool entireProject)
        {
            var typeIds = new HashSet<long>(
                seedElements
                    .Select(e => e.GetTypeId())
                    .Where(id => id != ElementId.InvalidElementId)
                    .Select(id => id.Value));

            if (typeIds.Count == 0)
                return seedElements;

            var builtInCats = seedElements
                .Where(e => e.Category != null)
                .Select(e => (BuiltInCategory)(int)e.Category.Id.Value)
                .Distinct()
                .ToList();

            if (builtInCats.Count == 0)
                return seedElements;

            var catFilter = new ElementMulticategoryFilter(builtInCats);

            FilteredElementCollector collector = entireProject
                ? new FilteredElementCollector(doc)
                : new FilteredElementCollector(doc, view.Id);

            var matches = collector
                .WhereElementIsNotElementType()
                .WherePasses(catFilter)
                .Where(e => typeIds.Contains(e.GetTypeId().Value))
                .ToList();

            return matches
                .Distinct(new ElementIdComparer())
                .ToList();
        }

        // ---------------- Tag creation ----------------

        private static IndependentTag CreateTag(Document doc, View view, Element element, ElementId tagTypeId, bool useLeader)
        {
            if (!AnnoLocation.TryGetRepresentativePoint(view, element, out XYZ p))
                return null;

            Reference reference = new Reference(element);

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

        // ---------------- Collision resolution for a single new tag (local pass) ----------------

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
            var workingRects = new List<Rect2D> { newTagRectInflated };
            var workingElements = new List<Element> { newTag };

            foreach (var ob in colliding)
            {
                var bb = ob.Element.get_BoundingBox(view);
                if (bb == null) continue;

                var r = AnnoGeometry.GetRectInViewPlane(plane, bb);
                double m = settings.Gap;
                workingRects.Add(r.Inflate(m));
                workingElements.Add(ob.Element);
            }

            if (workingRects.Count < 2)
                return;

            UV[] deltas = AnnoArrangeOps.RunMtvSimulation(workingRects, settings.Iterations, settings.Damping);

            for (int i = 0; i < workingElements.Count; i++)
            {
                var d = deltas[i];
                if (Math.Abs(d.U) < 1e-9 && Math.Abs(d.V) < 1e-9)
                    continue;

                var deltaXyz = plane.DeltaToXYZ(d);
                AnnoMove.TryMoveBy(doc, workingElements[i], deltaXyz, out _);
            }

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

        // ---------------- Final global collision recheck ----------------

        private static void RunFinalGlobalRecheck(
            Document doc,
            View view,
            ViewPlane2D plane,
            List<AnnoItem> newTags,
            List<AnnoItem> preexistingObstacles,
            TagAllSettingsResult settings,
            TagAllReport report)
        {
            double safetyMargin = Math.Max(settings.Gap * 2.0, UnitUtils.ConvertToInternalUnits(20, UnitTypeId.Millimeters));

            double minU = double.PositiveInfinity, minV = double.PositiveInfinity;
            double maxU = double.NegativeInfinity, maxV = double.NegativeInfinity;

            var newTagRects = new List<Rect2D>();
            var validNewTags = new List<Element>();

            foreach (var t in newTags)
            {
                var bb = t.Element.get_BoundingBox(view);
                if (bb == null) continue;

                var r = AnnoGeometry.GetRectInViewPlane(plane, bb);
                newTagRects.Add(r);
                validNewTags.Add(t.Element);

                minU = Math.Min(minU, r.MinX);
                minV = Math.Min(minV, r.MinY);
                maxU = Math.Max(maxU, r.MaxX);
                maxV = Math.Max(maxV, r.MaxY);
            }

            if (newTagRects.Count == 0)
                return;

            var envelope = new Rect2D(minU, minV, maxU, maxV).Inflate(safetyMargin);

            var nearbyObstacles = new List<Element>();
            foreach (var ob in preexistingObstacles)
            {
                var bb = ob.Element.get_BoundingBox(view);
                if (bb == null) continue;

                var r = AnnoGeometry.GetRectInViewPlane(plane, bb);
                if (r.IntersectsInclusive(envelope))
                    nearbyObstacles.Add(ob.Element);
            }

            var workingElements = new List<Element>();
            var workingRects = new List<Rect2D>();

            foreach (var e in validNewTags)
            {
                var bb = e.get_BoundingBox(view);
                if (bb == null) continue;
                var r = AnnoGeometry.GetRectInViewPlane(plane, bb);
                workingElements.Add(e);
                workingRects.Add(r.Inflate(settings.Gap));
            }

            foreach (var e in nearbyObstacles)
            {
                var bb = e.get_BoundingBox(view);
                if (bb == null) continue;
                var r = AnnoGeometry.GetRectInViewPlane(plane, bb);
                workingElements.Add(e);
                workingRects.Add(r.Inflate(settings.Gap));
            }

            if (workingElements.Count < 2)
                return;

            UV[] deltas = AnnoArrangeOps.RunMtvSimulation(workingRects, settings.Iterations, settings.Damping);

            int movedInFinalPass = 0;
            for (int i = 0; i < workingElements.Count; i++)
            {
                var d = deltas[i];
                if (Math.Abs(d.U) < 1e-9 && Math.Abs(d.V) < 1e-9)
                    continue;

                var deltaXyz = plane.DeltaToXYZ(d);
                if (AnnoMove.TryMoveBy(doc, workingElements[i], deltaXyz, out _))
                    movedInFinalPass++;
            }

            report.FinalPassMoved = movedInFinalPass;

            var finalRects = new List<Rect2D>();
            foreach (var e in workingElements)
            {
                var bb = e.get_BoundingBox(view);
                if (bb == null) continue;
                finalRects.Add(AnnoGeometry.GetRectInViewPlane(plane, bb).Inflate(settings.Gap));
            }

            int remaining = 0;
            for (int i = 0; i < finalRects.Count; i++)
                for (int j = i + 1; j < finalRects.Count; j++)
                    if (finalRects[i].IntersectsInclusive(finalRects[j]))
                        remaining++;

            report.FinalPassRemainingCollisions = remaining;
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

            if (CategoryToTagCategories.TryGetValue((BuiltInCategory)(int)targetCategory.Id.Value, out var tagBuiltIns))
            {
                foreach (var tagBic in tagBuiltIns)
                {
                    var tagCat = Category.GetCategory(doc, tagBic);
                    if (tagCat != null)
                        tagCategorySet.Add(tagCat.Id.Value);
                }
            }

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
            public int FinalPassMoved { get; set; }
            public int FinalPassRemainingCollisions { get; set; }
            public Dictionary<string, int> FailReasons { get; } = new();
        }
    }

    public sealed class CategoryTagOptions
    {
        public Category Category { get; set; }
        public List<Element> Elements { get; set; }
        public List<FamilySymbol> AvailableTagTypes { get; set; }
        public ElementId DefaultTagTypeId { get; set; }
    }

    public sealed class TagAllSettingsResult
    {
        public Dictionary<long, ElementId> SelectedTagTypeIdByCategoryKey { get; set; } = new();

        public bool UseLeader { get; set; }

        public double Gap { get; set; }
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

            if (elem.ViewSpecific) return false;

            if (elem.Pinned) return false;

            return true;
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
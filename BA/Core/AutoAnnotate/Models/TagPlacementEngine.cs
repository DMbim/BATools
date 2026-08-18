using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using View = Autodesk.Revit.DB.View;

namespace BA.BIM.Core.Annotations
{
    /// <summary>
    /// Shared tag-creation, tag-type-resolution, and collision-resolution logic.
    /// Extracted from TagAllSelectedCommand so both the selection-driven single-view
    /// command and TagAllBatchCommand call the SAME code. If a future command needs
    /// tag placement, it calls into this class - it does not reimplement any part of it.
    /// </summary>
    public static class TagPlacementEngine
    {
        public static readonly BuiltInCategory[] ObstacleCategories = new[]
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

        public static readonly Dictionary<BuiltInCategory, BuiltInCategory[]> CategoryToTagCategories =
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

        public static Dictionary<long, CategoryTagOptions> BuildCategoryTagOptions(Document doc, View view, List<Element> elements)
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

        public static List<FamilySymbol> GetValidTagTypes(Document doc, Category targetCategory)
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

        public static ElementId GetActiveTagTypeIdForCategory(View view, Category targetCategory, List<FamilySymbol> availableTagTypes)
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

        public static IndependentTag CreateTag(Document doc, View view, Element element, ElementId tagTypeId, bool useLeader)
        {
            if (!AnnoLocation.TryGetRepresentativePoint(view, element, out XYZ p))
                return null;

            Reference reference = new Reference(element);

            return IndependentTag.Create(
                doc, tagTypeId, view.Id, reference, useLeader, TagOrientation.Horizontal, p);
        }

        public static void ResolveTagCollision(
            Document doc,
            View view,
            ViewPlane2D plane,
            IndependentTag newTag,
            Rect2D newTagRectInflated,
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
                workingRects.Add(r.Inflate(settings.Gap));
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

        public static void RunFinalGlobalRecheck(
            Document doc,
            View view,
            ViewPlane2D plane,
            List<AnnoItem> newTags,
            List<AnnoItem> preexistingObstacles,
            TagAllSettingsResult settings,
            TagPlacementReport report)
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

            report.FinalPassMoved += movedInFinalPass;

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

            report.FinalPassRemainingCollisions += remaining;
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

        public static List<AnnoItem> CollectObstacleItems(Document doc, View view)
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

        public static HashSet<long> GetAlreadyTaggedElementIds(Document doc, View view)
        {
            var result = new HashSet<long>();

            var tags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>();

            foreach (var tag in tags)
            {
                ICollection<ElementId> taggedIds;
                try { taggedIds = tag.GetTaggedLocalElementIds(); }
                catch { continue; }

                foreach (var id in taggedIds)
                    result.Add(id.Value);
            }

            return result;
        }
    }

    /// <summary>
    /// Formerly a private nested class named TagAllReport inside TagAllSelectedCommand.
    /// Made public and top-level, and its Final* fields are now += accumulators
    /// (RunFinalGlobalRecheck may be called once per view in batch mode) rather than
    /// = assignments, so a batch run's report reflects totals across every view, not
    /// just the last one processed.
    /// </summary>
    public sealed class TagPlacementReport
    {
        public int Total { get; set; }
        public int Created { get; set; }
        public int SkippedAlreadyTagged { get; set; }
        public int CollidedAndResolved { get; set; }
        public int FailedToCreate { get; set; }
        public int FinalPassMoved { get; set; }
        public int FinalPassRemainingCollisions { get; set; }
        public Dictionary<string, int> FailReasons { get; } = new();

        public int ViewsProcessed { get; set; }
        public int ViewsFailed { get; set; }
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

    public sealed class ElementIdComparer : IEqualityComparer<Element>
    {
        public bool Equals(Element x, Element y)
            => (x?.Id?.Value ?? -1) == (y?.Id?.Value ?? -2);

        public int GetHashCode(Element obj)
            => unchecked((int)(obj?.Id?.Value ?? 0));
    }
}
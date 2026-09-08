using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.BIM.Core.Dimensioning.Models;

namespace BA.BIM.Core.Dimensioning.Services
{
    public static class BA_DimensionCandidateService
    {
        internal const double OffsetFeet = 800.0 / 304.8;

        private static readonly string[] ConcreteMaterialClassHints = { "Concrete" };
        private static readonly string[] PlasterboardMaterialClassHints = { "Gypsum", "Plasterboard", "Drywall" };

        public static (List<BA_DimensionCandidate> Candidates, List<BA_DimensionSkip> Skips) ScanView(
            Document doc, ViewPlan view, bool extendThroughCollinearWalls)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (view == null) throw new ArgumentNullException(nameof(view));

            var candidates = new List<BA_DimensionCandidate>();
            var skips = new List<BA_DimensionSkip>();

            var allWalls = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .ToList();

            var openingsByHost = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi => fi.Category != null &&
                             (fi.Category.Id == new ElementId(BuiltInCategory.OST_Doors) ||
                              fi.Category.Id == new ElementId(BuiltInCategory.OST_Windows)))
                .Where(fi => fi.Host != null)
                .GroupBy(fi => fi.Host.Id)
                .ToDictionary(g => g.Key, g => g.ToList());

            var straightBasicWalls = new List<Wall>();
            var wallLines = new Dictionary<ElementId, Line>();

            foreach (var wall in allWalls)
            {
                string wallName = SafeWallName(wall);

                if (wall.WallType == null || wall.WallType.Kind != WallKind.Basic)
                {
                    skips.Add(new BA_DimensionSkip
                    {
                        ViewId = view.Id,
                        ViewName = view.Name,
                        WallId = wall.Id,
                        WallName = wallName,
                        Reason = BA_DimensionSkipReason.NonBasicWallType,
                        Detail = $"WallType.Kind = {wall.WallType?.Kind}; v1 supports Basic walls only."
                    });
                    continue;
                }

                if (!(wall.Location is LocationCurve lc) || !(lc.Curve is Line wallLine))
                {
                    skips.Add(new BA_DimensionSkip
                    {
                        ViewId = view.Id,
                        ViewName = view.Name,
                        WallId = wall.Id,
                        WallName = wallName,
                        Reason = BA_DimensionSkipReason.WallIsCurved,
                        Detail = "LocationCurve is not a straight Line; v1 supports straight walls only."
                    });
                    continue;
                }

                straightBasicWalls.Add(wall);
                wallLines[wall.Id] = wallLine;
            }

            List<List<Wall>> runs = extendThroughCollinearWalls
                ? BuildWallRuns(straightBasicWalls, wallLines)
                : straightBasicWalls.Select(w => new List<Wall> { w }).ToList();

            foreach (var run in runs)
            {
                string wallName = run.Count == 1
                    ? SafeWallName(run[0])
                    : $"{SafeWallName(run[0])} plus {run.Count - 1} more";
                ElementId primaryWallId = run[0].Id;

                var hostedOpenings = new List<FamilyInstance>();
                foreach (var w in run)
                {
                    if (openingsByHost.TryGetValue(w.Id, out var list))
                        hostedOpenings.AddRange(list);
                }

                if (hostedOpenings.Count < 2)
                {
                    skips.Add(new BA_DimensionSkip
                    {
                        ViewId = view.Id,
                        ViewName = view.Name,
                        WallId = primaryWallId,
                        WallName = wallName,
                        Reason = BA_DimensionSkipReason.FewerThanTwoOpenings,
                        Detail = $"Found {hostedOpenings.Count} door/window opening(s) across {run.Count} wall(s); v1 requires >= 2."
                    });
                    continue;
                }

                var (runStart, runEnd, combinedLine) = BA_DimensionRunGeometry.ComputeRunExtents(run, wallLines);

                var ordered = hostedOpenings
                    .Select(fi => new { Instance = fi, Param = ProjectParameter(combinedLine, fi) })
                    .Where(x => x.Param.HasValue)
                    .OrderBy(x => x.Param.Value)
                    .ToList();

                if (ordered.Count < 2)
                {
                    skips.Add(new BA_DimensionSkip
                    {
                        ViewId = view.Id,
                        ViewName = view.Name,
                        WallId = primaryWallId,
                        WallName = wallName,
                        Reason = BA_DimensionSkipReason.NoValidOpeningReference,
                        Detail = "Could not resolve a LocationPoint projection for enough openings."
                    });
                    continue;
                }

                var openingRefs = new List<BA_DimensionOpeningRef>();
                bool allSatisfied = true;
                string failDetail = null;

                foreach (var o in ordered)
                {
                    ElementId hostWallId = o.Instance.Host?.Id ?? ElementId.InvalidElementId;
                    var hostWall = doc.GetElement(hostWallId) as Wall;

                    BA_WallConstructionType constructionType = hostWall != null
                        ? ClassifyWallConstruction(doc, hostWall)
                        : BA_WallConstructionType.Undetermined;

                    // Undetermined defaults to Axis, the permissive scheme, since there is no
                    // per opening manual override control in this round.
                    BA_DimensionStyle style = constructionType == BA_WallConstructionType.Concrete
                        ? BA_DimensionStyle.Edges
                        : BA_DimensionStyle.Axis;

                    bool hasCenterRef = HasReference(o.Instance, FamilyInstanceReferenceType.CenterLeftRight);
                    bool hasJambRefs = HasReference(o.Instance, FamilyInstanceReferenceType.Left)
                                       && HasReference(o.Instance, FamilyInstanceReferenceType.Right);

                    bool satisfied = style == BA_DimensionStyle.Edges ? hasJambRefs : hasCenterRef;

                    if (!satisfied)
                    {
                        allSatisfied = false;
                        failDetail = $"Opening {o.Instance.Id.Value} does not expose the reference(s) required for {style} style ({constructionType} wall).";
                        break;
                    }

                    openingRefs.Add(new BA_DimensionOpeningRef
                    {
                        OpeningId = o.Instance.Id,
                        HostWallId = hostWallId,
                        WallConstructionType = constructionType,
                        DimensionStyle = style
                    });
                }

                if (!allSatisfied)
                {
                    skips.Add(new BA_DimensionSkip
                    {
                        ViewId = view.Id,
                        ViewName = view.Name,
                        WallId = primaryWallId,
                        WallName = wallName,
                        Reason = BA_DimensionSkipReason.NoValidOpeningReference,
                        Detail = failDetail ?? "One or more hosted openings do not expose the required reference(s)."
                    });
                    continue;
                }

                candidates.Add(new BA_DimensionCandidate
                {
                    ViewId = view.Id,
                    ViewName = view.Name,
                    WallIds = run.Select(w => w.Id).ToList(),
                    WallName = wallName,
                    OrderedOpenings = openingRefs,
                    IsFlipped = false,
                    OffsetFeet = OffsetFeet
                });
            }

            return (candidates, skips);
        }

        private static List<List<Wall>> BuildWallRuns(List<Wall> walls, Dictionary<ElementId, Line> wallLines)
        {
            var runs = new List<List<Wall>>();
            var visited = new HashSet<ElementId>();

            foreach (var wall in walls)
            {
                if (visited.Contains(wall.Id)) continue;

                var run = new List<Wall> { wall };
                visited.Add(wall.Id);

                ExtendRun(run, walls, wallLines, visited, atStart: false);
                ExtendRun(run, walls, wallLines, visited, atStart: true);

                runs.Add(run);
            }

            return runs;
        }

        private static void ExtendRun(List<Wall> run, List<Wall> allWalls, Dictionary<ElementId, Line> wallLines, HashSet<ElementId> visited, bool atStart)
        {
            bool extended = true;
            while (extended)
            {
                extended = false;
                Wall edgeWall = atStart ? run[0] : run[run.Count - 1];
                Line edgeLine = wallLines[edgeWall.Id];

                foreach (var candidateWall in allWalls)
                {
                    if (visited.Contains(candidateWall.Id)) continue;
                    if (!wallLines.TryGetValue(candidateWall.Id, out var candidateLine)) continue;

                    if (AreCollinearAndJoined(edgeLine, candidateLine))
                    {
                        if (atStart) run.Insert(0, candidateWall);
                        else run.Add(candidateWall);

                        visited.Add(candidateWall.Id);
                        extended = true;
                        break;
                    }
                }
            }
        }

        private static bool AreCollinearAndJoined(Line a, Line b)
        {
            XYZ aDir = a.Direction.Normalize();
            XYZ bDir = b.Direction.Normalize();
            double cross = aDir.CrossProduct(bDir).GetLength();
            if (cross > 0.01) return false; // about 0.57 degrees

            XYZ[] aPts = { a.GetEndPoint(0), a.GetEndPoint(1) };
            XYZ[] bPts = { b.GetEndPoint(0), b.GetEndPoint(1) };

            foreach (var ap in aPts)
                foreach (var bp in bPts)
                    if (ap.DistanceTo(bp) <= BA_DimensionRunGeometry.RunJoinToleranceFeet)
                        return true;

            return false;
        }

        private static bool HasReference(FamilyInstance fi, FamilyInstanceReferenceType type)
        {
            try
            {
                IList<Reference> refs = fi.GetReferences(type);
                return refs != null && refs.Count > 0;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                return false;
            }
        }

        private static BA_WallConstructionType ClassifyWallConstruction(Document doc, Wall wall)
        {
            try
            {
                CompoundStructure cs = wall.WallType?.GetCompoundStructure();
                if (cs == null) return BA_WallConstructionType.Undetermined;

                int coreStart = cs.GetFirstCoreLayerIndex();
                int coreEnd = cs.GetLastCoreLayerIndex();
                if (coreStart < 0 || coreEnd < 0) return BA_WallConstructionType.Undetermined;
                if (coreStart != coreEnd) return BA_WallConstructionType.Undetermined;

                ElementId materialId = cs.GetMaterialId(coreStart);
                if (materialId == ElementId.InvalidElementId) return BA_WallConstructionType.Undetermined;

                var material = doc.GetElement(materialId) as Material;
                if (material == null || string.IsNullOrWhiteSpace(material.MaterialClass))
                    return BA_WallConstructionType.Undetermined;

                string materialClass = material.MaterialClass.Trim();

                if (ConcreteMaterialClassHints.Any(h => materialClass.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0))
                    return BA_WallConstructionType.Concrete;

                if (PlasterboardMaterialClassHints.Any(h => materialClass.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0))
                    return BA_WallConstructionType.Plasterboard;

                return BA_WallConstructionType.Undetermined;
            }
            catch
            {
                return BA_WallConstructionType.Undetermined;
            }
        }

        private static string SafeWallName(Wall wall)
        {
            try { return wall.Name; } catch { return $"Wall {wall.Id.Value}"; }
        }

        private static double? ProjectParameter(Line line, FamilyInstance fi)
        {
            if (!(fi.Location is LocationPoint lp)) return null;
            IntersectionResult result = line.Project(lp.Point);
            return result?.Parameter;
        }
    }
}
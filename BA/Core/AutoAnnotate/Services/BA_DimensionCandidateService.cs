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

        public static (List<BA_DimensionCandidate> Candidates, List<BA_DimensionSkip> Skips) ScanView(Document doc, ViewPlan view)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (view == null) throw new ArgumentNullException(nameof(view));

            var candidates = new List<BA_DimensionCandidate>();
            var skips = new List<BA_DimensionSkip>();

            var walls = new FilteredElementCollector(doc, view.Id)
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

            foreach (var wall in walls)
            {
                string wallName = $"Wall {wall.Id.Value}";
                try { wallName = wall.Name; } catch { }

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

                if (!openingsByHost.TryGetValue(wall.Id, out var hostedOpenings) || hostedOpenings.Count < 2)
                {
                    skips.Add(new BA_DimensionSkip
                    {
                        ViewId = view.Id,
                        ViewName = view.Name,
                        WallId = wall.Id,
                        WallName = wallName,
                        Reason = BA_DimensionSkipReason.FewerThanTwoOpenings,
                        Detail = $"Found {(hostedOpenings?.Count ?? 0)} door/window opening(s); v1 requires >= 2."
                    });
                    continue;
                }

                var ordered = hostedOpenings
                    .Select(fi => new { Instance = fi, Param = ProjectParameter(wallLine, fi) })
                    .Where(x => x.Param.HasValue)
                    .OrderBy(x => x.Param.Value)
                    .ToList();

                if (ordered.Count < 2)
                {
                    skips.Add(new BA_DimensionSkip
                    {
                        ViewId = view.Id,
                        ViewName = view.Name,
                        WallId = wall.Id,
                        WallName = wallName,
                        Reason = BA_DimensionSkipReason.NoValidOpeningReference,
                        Detail = "Could not resolve a LocationPoint projection for enough openings."
                    });
                    continue;
                }

                bool allHaveReference = true;
                foreach (var o in ordered)
                {
                    IList<Reference> refs;
                    try { refs = o.Instance.GetReferences(FamilyInstanceReferenceType.CenterLeftRight); }
                    catch (Autodesk.Revit.Exceptions.ArgumentException) { refs = null; }

                    if (refs == null || refs.Count == 0) { allHaveReference = false; break; }
                }

                if (!allHaveReference)
                {
                    skips.Add(new BA_DimensionSkip
                    {
                        ViewId = view.Id,
                        ViewName = view.Name,
                        WallId = wall.Id,
                        WallName = wallName,
                        Reason = BA_DimensionSkipReason.NoValidOpeningReference,
                        Detail = "One or more hosted openings do not expose a CenterLeftRight reference."
                    });
                    continue;
                }

                candidates.Add(new BA_DimensionCandidate
                {
                    ViewId = view.Id,
                    ViewName = view.Name,
                    WallId = wall.Id,
                    WallName = wallName,
                    OrderedOpeningIds = ordered.Select(x => x.Instance.Id).ToList(),
                    WallStart = wallLine.GetEndPoint(0),
                    WallEnd = wallLine.GetEndPoint(1),
                    WallOrientation = wall.Orientation
                });
            }

            return (candidates, skips);
        }

        private static double? ProjectParameter(Line wallLine, FamilyInstance fi)
        {
            if (!(fi.Location is LocationPoint lp)) return null;
            IntersectionResult result = wallLine.Project(lp.Point);
            return result?.Parameter;
        }
    }
}
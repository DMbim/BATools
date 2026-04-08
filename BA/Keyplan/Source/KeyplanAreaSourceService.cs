using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BA.UI.KeyplanGrid
{
    public static class KeyplanAreaSourceService
    {
        private const double Tol = 1e-6;

        public static CurveLoop GetLargestOuterLoopFromView(Document doc, View sourceView)
        {
            KeyplanSourceResolutionResult resolved = ResolveLargestOuterLoopFromView(doc, sourceView);
            return resolved?.OuterLoop;
        }

        public static KeyplanSourceReference CaptureSourceReferenceFromLargestArea(Document doc, View sourceView)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sourceView == null) throw new ArgumentNullException(nameof(sourceView));

            KeyplanSourceResolutionResult resolved = ResolveLargestOuterLoopFromView(doc, sourceView);
            if (resolved == null || resolved.OuterLoop == null)
                return null;

            KeyplanSourceReference reference = new KeyplanSourceReference
            {
                SourceViewId = sourceView.Id.Value,
                SourceViewUniqueId = sourceView.UniqueId ?? string.Empty,
                SourceViewName = sourceView.Name ?? string.Empty,
                SourceAreaId = resolved.SourceAreaId != null && resolved.SourceAreaId != ElementId.InvalidElementId
                    ? resolved.SourceAreaId.Value
                    : 0,
                SourceAreaUniqueId = resolved.SourceAreaUniqueId ?? string.Empty,
                SourceAreaName = resolved.SourceAreaName ?? string.Empty,
                SourceAreaNumber = resolved.SourceAreaNumber ?? string.Empty,
                BoundarySignature = resolved.BoundarySignature ?? string.Empty
            };

            reference.Normalize();
            return reference;
        }

        public static KeyplanSourceResolutionResult ResolveOuterLoopFromReference(
            Document doc,
            KeyplanSourceReference sourceReference)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sourceReference == null) throw new ArgumentNullException(nameof(sourceReference));

            sourceReference.Normalize();

            View sourceView = ResolveSourceView(doc, sourceReference);
            if (sourceView == null)
                return null;

            List<AreaBoundaryCandidate> candidates = GetAreaBoundaryCandidates(doc, sourceView);
            if (candidates.Count == 0)
                return null;

            AreaBoundaryCandidate byId = TryResolveByAreaId(doc, sourceReference, sourceView, candidates);
            if (byId != null)
                return CreateResolutionResult(byId, "AreaId");

            AreaBoundaryCandidate byUniqueId = TryResolveByAreaUniqueId(doc, sourceReference, sourceView, candidates);
            if (byUniqueId != null)
                return CreateResolutionResult(byUniqueId, "AreaUniqueId");

            AreaBoundaryCandidate bySignature = TryResolveByBoundarySignature(sourceReference, candidates);
            if (bySignature != null)
                return CreateResolutionResult(bySignature, "BoundarySignature");

            AreaBoundaryCandidate largest = candidates
                .OrderByDescending(x => x.AreaValue)
                .ThenBy(x => x.BoundarySignature, StringComparer.Ordinal)
                .FirstOrDefault();

            if (largest == null)
                return null;

            return CreateResolutionResult(largest, "LargestAreaFallback");
        }

        private static KeyplanSourceResolutionResult ResolveLargestOuterLoopFromView(Document doc, View sourceView)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sourceView == null) throw new ArgumentNullException(nameof(sourceView));

            List<AreaBoundaryCandidate> candidates = GetAreaBoundaryCandidates(doc, sourceView);
            if (candidates.Count == 0)
                return null;

            AreaBoundaryCandidate largest = candidates
                .OrderByDescending(x => x.AreaValue)
                .ThenBy(x => x.BoundarySignature, StringComparer.Ordinal)
                .FirstOrDefault();

            if (largest == null)
                return null;

            return CreateResolutionResult(largest, "LargestArea");
        }

        private static View ResolveSourceView(Document doc, KeyplanSourceReference sourceReference)
        {
            if (sourceReference.SourceViewId > 0)
            {
                Element byId = doc.GetElement(new ElementId(sourceReference.SourceViewId));
                if (byId is View viewById)
                    return viewById;
            }

            if (!string.IsNullOrWhiteSpace(sourceReference.SourceViewUniqueId))
            {
                Element byUniqueId = doc.GetElement(sourceReference.SourceViewUniqueId);
                if (byUniqueId is View viewByUniqueId)
                    return viewByUniqueId;
            }

            if (!string.IsNullOrWhiteSpace(sourceReference.SourceViewName))
            {
                return KeyplanViewService.FindViewByName(doc, sourceReference.SourceViewName);
            }

            return null;
        }

        private static AreaBoundaryCandidate TryResolveByAreaId(
            Document doc,
            KeyplanSourceReference sourceReference,
            View sourceView,
            List<AreaBoundaryCandidate> candidates)
        {
            if (sourceReference.SourceAreaId <= 0)
                return null;

            Element areaElement = doc.GetElement(new ElementId(sourceReference.SourceAreaId));
            Area area = areaElement as Area;
            if (area == null)
                return null;

            if (area.ViewSpecific && area.OwnerViewId != sourceView.Id)
                return null;

            return candidates.FirstOrDefault(x => x.AreaId == area.Id);
        }

        private static AreaBoundaryCandidate TryResolveByAreaUniqueId(
            Document doc,
            KeyplanSourceReference sourceReference,
            View sourceView,
            List<AreaBoundaryCandidate> candidates)
        {
            if (string.IsNullOrWhiteSpace(sourceReference.SourceAreaUniqueId))
                return null;

            Element areaElement = doc.GetElement(sourceReference.SourceAreaUniqueId);
            Area area = areaElement as Area;
            if (area == null)
                return null;

            if (area.ViewSpecific && area.OwnerViewId != sourceView.Id)
                return null;

            return candidates.FirstOrDefault(x =>
                string.Equals(x.AreaUniqueId, area.UniqueId, StringComparison.OrdinalIgnoreCase));
        }

        private static AreaBoundaryCandidate TryResolveByBoundarySignature(
            KeyplanSourceReference sourceReference,
            List<AreaBoundaryCandidate> candidates)
        {
            if (string.IsNullOrWhiteSpace(sourceReference.BoundarySignature))
                return null;

            AreaBoundaryCandidate exact = candidates.FirstOrDefault(x =>
                string.Equals(x.BoundarySignature, sourceReference.BoundarySignature, StringComparison.Ordinal));

            if (exact != null)
                return exact;

            return candidates
                .Select(x => new
                {
                    Candidate = x,
                    Score = ComputeBoundarySimilarityScore(sourceReference.BoundarySignature, x.BoundarySignature)
                })
                .Where(x => x.Score > 0.90)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Candidate.AreaValue)
                .ThenBy(x => x.Candidate.BoundarySignature, StringComparer.Ordinal)
                .Select(x => x.Candidate)
                .FirstOrDefault();
        }

        private static double ComputeBoundarySimilarityScore(string a, string b)
        {
            a = a ?? string.Empty;
            b = b ?? string.Empty;

            if (string.Equals(a, b, StringComparison.Ordinal))
                return 1.0;

            string[] aa = a.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            string[] bb = b.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            if (aa.Length == 0 || bb.Length == 0)
                return 0.0;

            HashSet<string> sa = new HashSet<string>(aa, StringComparer.Ordinal);
            HashSet<string> sb = new HashSet<string>(bb, StringComparer.Ordinal);

            int intersection = sa.Intersect(sb, StringComparer.Ordinal).Count();
            int union = sa.Union(sb, StringComparer.Ordinal).Count();

            if (union == 0)
                return 0.0;

            return (double)intersection / union;
        }

        private static List<AreaBoundaryCandidate> GetAreaBoundaryCandidates(Document doc, View sourceView)
        {
            SpatialElementBoundaryOptions options = new SpatialElementBoundaryOptions();

            List<Area> areas = new FilteredElementCollector(doc, sourceView.Id)
                .OfCategory(BuiltInCategory.OST_Areas)
                .WhereElementIsNotElementType()
                .Cast<Area>()
                .OrderBy(x => x.Id.Value)
                .ToList();

            List<AreaBoundaryCandidate> candidates = new List<AreaBoundaryCandidate>();

            foreach (Area area in areas)
            {
                IList<IList<BoundarySegment>> boundaries = area.GetBoundarySegments(options);
                if (boundaries == null || boundaries.Count == 0)
                    continue;

                foreach (IList<BoundarySegment> segList in boundaries)
                {
                    List<XYZ> polygon = BuildClosedPolygonFromBoundarySegments(segList);
                    if (polygon == null || polygon.Count < 3)
                        continue;

                    polygon = KeyplanPolygonUtils.CleanPolygonStrict(polygon);
                    if (polygon == null || polygon.Count < 3)
                        continue;

                    double areaValue = Math.Abs(KeyplanPolygonUtils.ComputeSignedArea2D(polygon));
                    if (areaValue < KeyplanGeometryTolerance.PolygonArea)
                        continue;

                    if (!KeyplanPolygonUtils.TryCreateCurveLoopFromPolygon(polygon, out CurveLoop loop) || loop == null)
                        continue;

                    candidates.Add(new AreaBoundaryCandidate
                    {
                        AreaId = area.Id,
                        AreaUniqueId = area.UniqueId ?? string.Empty,
                        AreaName = area.Name ?? string.Empty,
                        AreaNumber = area.Number ?? string.Empty,
                        Polygon = polygon,
                        OuterLoop = loop,
                        AreaValue = areaValue,
                        BoundarySignature = BuildBoundarySignature(polygon)
                    });
                }
            }

            return candidates
                .OrderByDescending(x => x.AreaValue)
                .ThenBy(x => x.BoundarySignature, StringComparer.Ordinal)
                .ToList();
        }

        private static KeyplanSourceResolutionResult CreateResolutionResult(
            AreaBoundaryCandidate candidate,
            string mode)
        {
            if (candidate == null)
                return null;

            return new KeyplanSourceResolutionResult
            {
                OuterLoop = candidate.OuterLoop,
                SourceAreaId = candidate.AreaId,
                SourceAreaUniqueId = candidate.AreaUniqueId ?? string.Empty,
                SourceAreaName = candidate.AreaName ?? string.Empty,
                SourceAreaNumber = candidate.AreaNumber ?? string.Empty,
                BoundarySignature = candidate.BoundarySignature ?? string.Empty,
                ResolutionMode = mode ?? string.Empty
            };
        }

        private static string BuildBoundarySignature(IList<XYZ> polygon)
        {
            List<XYZ> pts = KeyplanPolygonUtils.CleanPolygonStrict(polygon);
            if (pts == null || pts.Count == 0)
                return string.Empty;

            List<string> keys = pts
                .Select(p =>
                {
                    XYZ fp = KeyplanPolygonUtils.FlattenPoint(p);
                    long x = (long)Math.Round(fp.X / KeyplanGeometryTolerance.KeyRounding);
                    long y = (long)Math.Round(fp.Y / KeyplanGeometryTolerance.KeyRounding);

                    return x.ToString(CultureInfo.InvariantCulture) + "," +
                           y.ToString(CultureInfo.InvariantCulture);
                })
                .ToList();

            int minIndex = 0;
            for (int i = 1; i < keys.Count; i++)
            {
                if (string.CompareOrdinal(keys[i], keys[minIndex]) < 0)
                    minIndex = i;
            }

            List<string> rotated = new List<string>();
            for (int i = 0; i < keys.Count; i++)
                rotated.Add(keys[(minIndex + i) % keys.Count]);

            return string.Join(";", rotated);
        }

        private static List<XYZ> BuildClosedPolygonFromBoundarySegments(IList<BoundarySegment> segList)
        {
            if (segList == null || segList.Count == 0)
                return null;

            List<XYZ> pts = new List<XYZ>();
            XYZ currentEnd = null;

            foreach (BoundarySegment seg in segList)
            {
                Curve c = seg?.GetCurve();
                if (c == null)
                    continue;

                XYZ a = KeyplanPolygonUtils.FlattenPoint(c.GetEndPoint(0));
                XYZ b = KeyplanPolygonUtils.FlattenPoint(c.GetEndPoint(1));

                if (a.DistanceTo(b) < Tol)
                    continue;

                if (pts.Count == 0)
                {
                    pts.Add(a);
                    pts.Add(b);
                    currentEnd = b;
                    continue;
                }

                if (currentEnd.DistanceTo(a) < Tol)
                {
                    pts.Add(b);
                    currentEnd = b;
                }
                else if (currentEnd.DistanceTo(b) < Tol)
                {
                    pts.Add(a);
                    currentEnd = a;
                }
                else
                {
                    if (currentEnd.DistanceTo(a) <= currentEnd.DistanceTo(b))
                    {
                        if (currentEnd.DistanceTo(a) < 1e-3)
                        {
                            pts.Add(b);
                            currentEnd = b;
                        }
                        else
                        {
                            pts.Add(a);
                            pts.Add(b);
                            currentEnd = b;
                        }
                    }
                    else
                    {
                        if (currentEnd.DistanceTo(b) < 1e-3)
                        {
                            pts.Add(a);
                            currentEnd = a;
                        }
                        else
                        {
                            pts.Add(b);
                            pts.Add(a);
                            currentEnd = a;
                        }
                    }
                }
            }

            pts = KeyplanPolygonUtils.CleanPolygonStrict(pts);

            if (pts == null || pts.Count < 3)
                return null;

            return pts;
        }

        private sealed class AreaBoundaryCandidate
        {
            public ElementId AreaId { get; set; } = ElementId.InvalidElementId;
            public string AreaUniqueId { get; set; } = string.Empty;
            public string AreaName { get; set; } = string.Empty;
            public string AreaNumber { get; set; } = string.Empty;
            public List<XYZ> Polygon { get; set; } = new List<XYZ>();
            public CurveLoop OuterLoop { get; set; }
            public double AreaValue { get; set; }
            public string BoundarySignature { get; set; } = string.Empty;
        }
    }
}
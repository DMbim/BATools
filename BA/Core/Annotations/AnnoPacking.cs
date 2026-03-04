using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace BA.BIM.Core.Annotations
{
    public sealed class AnnoPackingItem
    {
        public Element Element { get; }
        public XYZ RepPoint { get; }
        public BoundingBoxXYZ BBoxInView { get; }

        public AnnoPackingItem(Element element, XYZ repPoint, BoundingBoxXYZ bboxInView)
        {
            Element = element;
            RepPoint = repPoint;
            BBoxInView = bboxInView;
        }
    }

    public sealed class PackingConfig
    {
        public double Margin { get; set; }     // clearance around bbox (internal units)
        public double Step { get; set; }       // spiral step (internal units)
        public int MaxRings { get; set; }      // search budget
        public bool TouchCountsAsCollision { get; set; } = true;

        public static PackingConfig Default()
        {
            return new PackingConfig
            {
                Margin = UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters),
                Step = UnitUtils.ConvertToInternalUnits(3, UnitTypeId.Millimeters),
                MaxRings = 160,
                TouchCountsAsCollision = true
            };
        }
    }

    public sealed class PackingReport
    {
        public int Total { get; set; }
        public int CollidingInitially { get; set; }
        public int Moved { get; set; }
        public int FailedToMove { get; set; }
        public int CouldNotFindSpot { get; set; }
        public Dictionary<string, int> FailReasons { get; } = new();
    }

    public static class AnnoPacking
    {
        public static PackingReport DeoverlapGreedySpiral(
           Document doc,
           View view,
           ViewPlane2D plane,
           IList<AnnoPackingItem> items,
           PackingConfig cfg)
        {
            var report = new PackingReport { Total = items.Count };

            var ordered = items
                .Select(i =>
                {
                    var rect = AnnoGeometry.GetRectInViewPlane(plane, i.BBoxInView);
                    var c = rect.Center();
                    return new { Item = i, Center = c };
                })
                .OrderByDescending(x => x.Center.V)
                .ThenBy(x => x.Center.U)
                .Select(x => x.Item)
                .ToList();

            var placed = new List<Rect2D>();

            foreach (var it in ordered)
            {
                var bb = it.Element.get_BoundingBox(view);
                if (bb == null)
                    continue;

                Rect2D rect = AnnoGeometry.GetRectInViewPlane(plane, bb).Inflate(cfg.Margin);

                bool collides = placed.Any(r => Intersects(r, rect, cfg.TouchCountsAsCollision));
                if (collides) report.CollidingInitially++;

                if (!collides)
                {
                    placed.Add(rect);
                    continue;
                }

                // ---------- NEW STRATEGY ----------
                // Push away from center of already placed cluster

                UV myCenter = rect.Center();
                UV clusterCenter = GetClusterCenter(placed);

                UV direction = new UV(
                    myCenter.U - clusterCenter.U,
                    myCenter.V - clusterCenter.V);

                double len = System.Math.Sqrt(direction.U * direction.U + direction.V * direction.V);
                if (len < 1e-6)
                    direction = new UV(1, 0); // fallback

                direction = new UV(direction.U / len, direction.V / len);

                bool moved = false;

                // Push outward in increasing distance
                for (int i = 1; i <= cfg.MaxRings; i++)
                {
                    UV delta = new UV(direction.U * i * cfg.Step,
                                      direction.V * i * cfg.Step);

                    var candidate = rect.MoveBy(delta);

                    if (placed.Any(r => Intersects(r, candidate, cfg.TouchCountsAsCollision)))
                        continue;

                    var deltaXyz = plane.DeltaToXYZ(delta);

                    if (AnnoMove.TryMoveBy(doc, it.Element, deltaXyz, out string reason))
                    {
                        placed.Add(candidate);
                        report.Moved++;
                        moved = true;
                        break;
                    }
                }

                if (!moved)
                {
                    report.CouldNotFindSpot++;
                    placed.Add(rect);
                }
            }

            return report;
        }

        private static UV GetClusterCenter(List<Rect2D> rects)
        {
            if (rects.Count == 0)
                return new UV(0, 0);

            double u = 0;
            double v = 0;

            foreach (var r in rects)
            {
                var c = r.Center();
                u += c.U;
                v += c.V;
            }

            return new UV(u / rects.Count, v / rects.Count);
        }

        private static bool Intersects(Rect2D a, Rect2D b, bool touchCounts)
        {
            if (!touchCounts)
                return a.Intersects(b);

            // Inclusive intersection (touching edges counts as collision)
            return !(b.MinX > a.MaxX || b.MaxX < a.MinX || b.MinY > a.MaxY || b.MaxY < a.MinY);
        }

        private static IEnumerable<UV> SpiralOffsets(double step, int maxRings)
        {
            yield return new UV(0, 0);

            for (int r = 1; r <= maxRings; r++)
            {
                double d = r * step;

                for (int i = -r; i <= r; i++)
                {
                    yield return new UV(i * step, d);
                    yield return new UV(i * step, -d);
                }

                for (int j = -r + 1; j <= r - 1; j++)
                {
                    yield return new UV(-d, j * step);
                    yield return new UV(d, j * step);
                }
            }
        }
    }
}
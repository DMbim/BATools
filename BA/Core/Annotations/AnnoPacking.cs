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
        public double Step { get; set; }       // spiral ring pitch (internal units)
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
        // Processes elements top to bottom, left to right. For each element that
        // collides with something already placed, searches an expanding square
        // spiral of candidate offsets around its ORIGINAL position (via
        // SpiralOffsets) and takes the first offset that clears every placed
        // rect. This is a true spiral search, not a single direction push, so it
        // will find a clear spot on either side of an obstacle rather than only
        // committing to one direction and failing if that direction is blocked.
        public static PackingReport DeoverlapGreedySpiral(
           Document doc,
           Autodesk.Revit.DB.View view,
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

                Rect2D originalRect = AnnoGeometry.GetRectInViewPlane(plane, bb).Inflate(cfg.Margin);

                bool collides = placed.Any(r => Intersects(r, originalRect, cfg.TouchCountsAsCollision));
                if (collides) report.CollidingInitially++;

                if (!collides)
                {
                    placed.Add(originalRect);
                    continue;
                }

                bool moved = false;

                foreach (var offset in SpiralOffsets(cfg.Step, cfg.MaxRings))
                {
                    if (offset.U == 0 && offset.V == 0)
                        continue; // ring 0 is the original position, already known to collide

                    var candidate = originalRect.MoveBy(offset);

                    if (placed.Any(r => Intersects(r, candidate, cfg.TouchCountsAsCollision)))
                        continue;

                    var deltaXyz = plane.DeltaToXYZ(offset);

                    if (AnnoMove.TryMoveBy(doc, it.Element, deltaXyz, out string reason))
                    {
                        placed.Add(candidate);
                        report.Moved++;
                        moved = true;
                        break;
                    }
                    else
                    {
                        report.FailReasons[reason] = report.FailReasons.GetValueOrDefault(reason) + 1;
                    }
                }

                if (!moved)
                {
                    report.CouldNotFindSpot++;
                    placed.Add(originalRect);
                }
            }

            return report;
        }

        private static bool Intersects(Rect2D a, Rect2D b, bool touchCounts)
        {
            if (!touchCounts)
                return a.Intersects(b);

            return !(b.MinX > a.MaxX || b.MaxX < a.MinX || b.MinY > a.MaxY || b.MaxY < a.MinY);
        }

        // Enumerates offsets on an expanding square ring around (0,0): ring 0 is
        // the origin itself, ring r covers the perimeter of a (2r+1) x (2r+1)
        // square of cells at spacing `step`.
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
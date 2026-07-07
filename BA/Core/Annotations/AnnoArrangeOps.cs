using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using View = Autodesk.Revit.DB.View;

namespace BA.BIM.Core.Annotations
{
    public enum ArrangeMode
    {
        Cancel = 0,
        ResolveCollisions = 1,
        DistributeHorizontal = 2,
        DistributeVertical = 3,
        StackListVertical = 4,
        StackListHorizontal = 5,
        SnapToGuideLine = 6,
        SnapGrid = 7
    }

    public sealed class ArrangeConfig
    {
        public ArrangeMode Mode { get; set; }

        // spacing (internal units)
        public double Gap { get; set; }

        // collision solver
        public int Iterations { get; set; }
        public double Damping { get; set; }         // 0..1
        public bool UseAutoMargin { get; set; }
        public double FixedMargin { get; set; }     // if not auto

        // SnapGrid: cap on per element displacement from its original position,
        // expressed as a multiple of Gap. MaxDisplacement = Gap * MaxDisplacementFactor.
        public double MaxDisplacementFactor { get; set; }

        public static ArrangeConfig DefaultResolve()
        {
            return new ArrangeConfig
            {
                Mode = ArrangeMode.ResolveCollisions,
                Gap = UnitUtils.ConvertToInternalUnits(6, UnitTypeId.Millimeters),
                Iterations = 30,
                Damping = 0.75,
                UseAutoMargin = true,
                FixedMargin = UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters),
                MaxDisplacementFactor = 3.0,
            };
        }
    }

    public sealed class AnnoItem
    {
        public Element Element { get; }
        public BoundingBoxXYZ BBoxInView { get; }

        public AnnoItem(Element e, BoundingBoxXYZ bb)
        {
            Element = e;
            BBoxInView = bb;
        }
    }

    public readonly struct GuideLine2D
    {
        public UV P0 { get; }
        public UV P1 { get; }

        public GuideLine2D(UV p0, UV p1)
        {
            P0 = p0;
            P1 = p1;
        }

        public UV Dir()
        {
            var d = new UV(P1.U - P0.U, P1.V - P0.V);
            double len = Math.Sqrt(d.U * d.U + d.V * d.V);
            if (len < 1e-9) return new UV(1, 0);
            return new UV(d.U / len, d.V / len);
        }

        public UV Normal()
        {
            var d = Dir();
            return new UV(-d.V, d.U);
        }

        public UV ProjectPoint(UV p)
        {
            var d = Dir();
            var v = new UV(p.U - P0.U, p.V - P0.V);
            double t = v.U * d.U + v.V * d.V;
            return new UV(P0.U + t * d.U, P0.V + t * d.V);
        }
    }

    public sealed class ArrangeReport
    {
        public int Total { get; set; }
        public int InitiallyColliding { get; set; }
        public int RemainingCollisions { get; set; }
        public int Moved { get; set; }
        public int Iterations { get; set; }

        // SnapGrid specific
        public int GridCols { get; set; }
        public int GridRows { get; set; }
        public int GrowthSteps { get; set; }
        public int OverThreshold { get; set; }
    }

    public static class AnnoArrangeOps
    {
        public static ArrangeReport Execute(
            Document doc,
            View view,
            ViewPlane2D plane,
            IList<AnnoItem> items,
            ArrangeConfig cfg,
            GuideLine2D? guide)
        {
            return cfg.Mode switch
            {
                ArrangeMode.ResolveCollisions => ResolveCollisions(doc, view, plane, items, cfg),
                ArrangeMode.DistributeHorizontal => Distribute(doc, view, plane, items, horizontal: true),
                ArrangeMode.DistributeVertical => Distribute(doc, view, plane, items, horizontal: false),
                ArrangeMode.StackListVertical => Stack(doc, view, plane, items, vertical: true, cfg.Gap),
                ArrangeMode.StackListHorizontal => Stack(doc, view, plane, items, vertical: false, cfg.Gap),
                ArrangeMode.SnapToGuideLine => SnapToGuideLine(doc, view, plane, items, cfg.Gap, guide),
                ArrangeMode.SnapGrid => SnapGrid(doc, view, plane, items, cfg),
                _ => new ArrangeReport { Total = items.Count }
            };
        }

        // ---------------- Distribute ----------------

        private static ArrangeReport Distribute(Document doc, View view, ViewPlane2D plane, IList<AnnoItem> items, bool horizontal)
        {
            if (items.Count == 0)
                return new ArrangeReport { Total = 0 };

            var rects = GetRects(view, plane, items, margin: 0);

            // Compute average center on the alignment axis
            double sum = 0;
            int valid = 0;
            for (int i = 0; i < rects.Count; i++)
            {
                UV c = rects[i].Center();
                sum += horizontal ? c.V : c.U; // V for horizontal row, U for vertical column
                valid++;
            }

            if (valid == 0)
                return new ArrangeReport { Total = items.Count };

            double target = sum / valid;

            int moved = 0;
            for (int i = 0; i < items.Count; i++)
            {
                UV c = rects[i].Center();

                // Move each element so its bbox center lands on the shared axis value
                UV delta = horizontal
                    ? new UV(0, target - c.V)   // align V, preserve U
                    : new UV(target - c.U, 0);  // align U, preserve V

                if (delta.U * delta.U + delta.V * delta.V < 1e-18)
                    continue;

                if (TryMove(doc, plane, items[i].Element, delta))
                    moved++;
            }

            return new ArrangeReport { Total = items.Count, Moved = moved };
        }

        // ---------------- Stack list ----------------

        private static ArrangeReport Stack(Document doc, View view, ViewPlane2D plane, IList<AnnoItem> items, bool vertical, double gap)
        {
            // sort by top-to-bottom then left-to-right for vertical stack, reverse for horizontal
            var rects = GetRects(view, plane, items, margin: 0);

            var order = Enumerable.Range(0, items.Count)
                .OrderByDescending(i => rects[i].MaxY)
                .ThenBy(i => rects[i].MinX)
                .ToList();

            int moved = 0;

            if (vertical)
            {
                // Anchor at first top, keep X as-is
                double cursorTop = rects[order[0]].MaxY;

                for (int k = 0; k < order.Count; k++)
                {
                    int idx = order[k];
                    var r = rects[idx];

                    double dy = cursorTop - r.MaxY;
                    if (TryMove(doc, plane, items[idx].Element, new UV(0, dy)))
                        moved++;

                    cursorTop = cursorTop - r.Height - gap;
                }
            }
            else
            {
                // Horizontal: anchor at first left, keep Y as-is
                double cursorLeft = rects[order[0]].MinX;

                for (int k = 0; k < order.Count; k++)
                {
                    int idx = order[k];
                    var r = rects[idx];

                    double dx = cursorLeft - r.MinX;
                    if (TryMove(doc, plane, items[idx].Element, new UV(dx, 0)))
                        moved++;

                    cursorLeft = cursorLeft + r.Width + gap;
                }
            }

            return new ArrangeReport { Total = items.Count, Moved = moved };
        }

        // ---------------- Snap to guide line ----------------

        private static ArrangeReport SnapToGuideLine(
            Document doc,
            View view,
            ViewPlane2D plane,
            IList<AnnoItem> items,
            double gap,
            GuideLine2D? guideNullable)
        {
            if (guideNullable == null)
                return new ArrangeReport { Total = items.Count };

            var guide = guideNullable.Value;
            var rects = GetRects(view, plane, items, margin: 0);
            var centers = rects.Select(r => r.Center()).ToList();
            var dir = guide.Dir();

            // Project centers onto guide; optionally distribute along guide with gap based on bbox size
            var projected = centers.Select(c => guide.ProjectPoint(c)).ToList();

            // Order along guide by parameter t
            var order = Enumerable.Range(0, items.Count)
                .OrderBy(i => ParamAlong(projected[i], guide.P0, dir))
                .ToList();

            // First pass: snap perpendicular to line (remove normal offset)
            int moved = 0;
            for (int i = 0; i < items.Count; i++)
            {
                UV c = centers[i];
                UV p = projected[i];
                UV delta = new UV(p.U - c.U, p.V - c.V);

                if (TryMove(doc, plane, items[i].Element, delta))
                    moved++;
            }

            // Second pass: distribute along guide with gaps (stack along line)
            // Refresh rects after snap
            rects = GetRects(view, plane, items, margin: 0);
            centers = rects.Select(r => r.Center()).ToList();
            projected = centers.Select(c => guide.ProjectPoint(c)).ToList();

            double cursor = ParamAlong(projected[order[0]], guide.P0, dir);

            for (int k = 0; k < order.Count; k++)
            {
                int idx = order[k];
                var r = rects[idx];
                var c = centers[idx];
                double t = ParamAlong(projected[idx], guide.P0, dir);

                // advance cursor by half width projected onto dir (approx): use max(width,height) as a safe spacing basis
                double size = Math.Max(r.Width, r.Height);
                double targetT = (k == 0) ? t : (cursor + size * 0.5 + gap);

                double dt = targetT - t;
                UV delta = new UV(dir.U * dt, dir.V * dt);

                if (TryMove(doc, plane, items[idx].Element, delta))
                    moved++;

                cursor = targetT + size * 0.5;
            }

            return new ArrangeReport { Total = items.Count, Moved = moved };
        }

        private static double ParamAlong(UV p, UV origin, UV dir)
        {
            var v = new UV(p.U - origin.U, p.V - origin.V);
            return v.U * dir.U + v.V * dir.V;
        }

        // ---------------- Collision resolve (MTV) ----------------

        private static ArrangeReport ResolveCollisions(Document doc, View view, ViewPlane2D plane, IList<AnnoItem> items, ArrangeConfig cfg)
        {
            int n = items.Count;
            var report = new ArrangeReport { Total = n, Iterations = cfg.Iterations };

            // Initial rects + margin
            var rects = GetRects(view, plane, items, margin: 0);
            double[] margins = rects.Select(r => cfg.UseAutoMargin ? AnnoGeometry.AutoMargin(r) : cfg.FixedMargin).ToArray();
            var inflated = rects.Select((r, i) => r.Inflate(margins[i])).ToList();

            report.InitiallyColliding = CountCollisions(inflated);

            UV[] total = RunMtvSimulation(inflated, cfg.Iterations, cfg.Damping);

            // Apply to Revit
            int moved = 0;
            for (int i = 0; i < n; i++)
            {
                if (Math.Abs(total[i].U) < 1e-9 && Math.Abs(total[i].V) < 1e-9)
                    continue;

                if (TryMove(doc, plane, items[i].Element, total[i]))
                    moved++;
            }

            report.Moved = moved;

            // recompute collisions after move (fresh bboxes)
            var finalRects = GetRects(view, plane, items, margin: 0);
            finalRects = finalRects.Select((r, i) => r.Inflate(margins[i])).ToList();
            report.RemainingCollisions = CountCollisions(finalRects);

            return report;
        }

        /// <summary>
        /// Pure MTV (Minimum Translation Vector) collision-resolution simulation.
        /// Takes a list of rects (already inflated by whatever margin the caller wants),
        /// runs up to `iterations` passes of pairwise MTV separation with damping, and
        /// returns the accumulated UV delta per rect. Does not touch Revit elements or
        /// mutate the input list - the caller applies the returned deltas.
        ///
        /// Used by ResolveCollisions (full selection) and by TagAllSelectedCommand's
        /// per-tag collision resolution (small working sets: {new tag} + colliding
        /// existing annotations).
        /// </summary>
        internal static UV[] RunMtvSimulation(IList<Rect2D> rects, int iterations, double damping)
        {
            int n = rects.Count;
            UV[] total = Enumerable.Repeat(new UV(0, 0), n).ToArray();

            // Work on a local mutable copy so the caller's list is untouched.
            var working = rects.ToList();

            for (int iter = 0; iter < iterations; iter++)
            {
                UV[] step = Enumerable.Repeat(new UV(0, 0), n).ToArray();
                bool any = false;

                for (int i = 0; i < n; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        if (!working[i].IntersectsInclusive(working[j]))
                            continue;

                        any = true;

                        // Minimum Translation Vector (axis-aligned in UV)
                        var mtv = ComputeMTV(working[i], working[j]); // move i by -mtv/2, j by +mtv/2
                        step[i] = Add(step[i], new UV(-0.5 * mtv.U, -0.5 * mtv.V));
                        step[j] = Add(step[j], new UV(+0.5 * mtv.U, +0.5 * mtv.V));
                    }
                }

                if (!any)
                    break;

                // apply damped step to rects (simulation space)
                for (int i = 0; i < n; i++)
                {
                    var d = new UV(step[i].U * damping, step[i].V * damping);
                    total[i] = Add(total[i], d);
                    working[i] = working[i].MoveBy(d);
                }
            }

            return total;
        }

        /// <summary>
        /// Given a moved element's rect and the rect of the obstacle it was resolved
        /// against (both post-MTV), returns a delta that snaps the moved element's
        /// center on the axis PERPENDICULAR to the dominant displacement axis to match
        /// the obstacle's center on that axis. E.g. if the element was pushed mostly
        /// along V (vertical), its U (horizontal) center is aligned to the obstacle's
        /// U center, so it ends up directly above/below the obstacle rather than at an
        /// arbitrary horizontal offset.
        ///
        /// `displacement` is the MTV delta that was just applied to `movedRect`
        /// (already applied - movedRect reflects the post-move position).
        /// </summary>
        internal static UV ComputeAlignmentDelta(Rect2D movedRect, Rect2D obstacleRect, UV displacement)
        {
            UV movedCenter = movedRect.Center();
            UV obstacleCenter = obstacleRect.Center();

            bool dominantIsV = Math.Abs(displacement.V) >= Math.Abs(displacement.U);

            if (dominantIsV)
            {
                // pushed mainly vertically -> align horizontal (U) center to obstacle
                return new UV(obstacleCenter.U - movedCenter.U, 0);
            }
            else
            {
                // pushed mainly horizontally -> align vertical (V) center to obstacle
                return new UV(0, obstacleCenter.V - movedCenter.V);
            }
        }

        private static UV ComputeMTV(Rect2D a, Rect2D b)
        {
            // penetration distances along X and Y (inclusive collision)
            double left = b.MinX - a.MaxX;   // negative when overlapping
            double right = b.MaxX - a.MinX;  // positive when overlapping
            double down = b.MinY - a.MaxY;
            double up = b.MaxY - a.MinY;

            // overlap amounts
            double penX = Math.Min(Math.Abs(left), Math.Abs(right));
            double penY = Math.Min(Math.Abs(down), Math.Abs(up));

            UV ca = a.Center();
            UV cb = b.Center();
            double sx = (cb.U >= ca.U) ? 1.0 : -1.0;
            double sy = (cb.V >= ca.V) ? 1.0 : -1.0;

            // move along least penetration axis
            if (penX < penY)
                return new UV(sx * penX, 0);
            else
                return new UV(0, sy * penY);
        }

        private static int CountCollisions(IList<Rect2D> rects)
        {
            int c = 0;
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                    if (rects[i].IntersectsInclusive(rects[j]))
                        c++;
            return c;
        }

        // ---------------- Snap Grid ----------------
        //
        // Builds a uniform grid (cell pitch = maxItemDimension + Gap), centered on the
        // centroid of the items' ORIGINAL bbox centers (captured at call time, before
        // any move). Items are assigned to grid cells by nearest-original-center,
        // greedy, one item per cell. If any item's required displacement to its
        // assigned cell exceeds MaxDisplacement (= Gap * MaxDisplacementFactor), the
        // grid grows by one column or row (whichever axis has the larger aggregate
        // overflow), and reassignment is repeated. Growth is capped at MaxGridDim x
        // MaxGridDim. Items that still exceed the threshold after the cap is reached
        // are placed in their nearest available cell anyway and counted in
        // report.OverThreshold.

        private const int MaxGridDim = 8;

        private static ArrangeReport SnapGrid(Document doc, View view, ViewPlane2D plane, IList<AnnoItem> items, ArrangeConfig cfg)
        {
            int n = items.Count;
            var report = new ArrangeReport { Total = n };

            if (n == 0)
                return report;

            // Original (pre-move) rects and centers - captured once, before any TryMove.
            var origRects = GetRects(view, plane, items, margin: 0);
            var origCenters = origRects.Select(r => r.Center()).ToList();

            // Cell pitch from the largest item dimension across the selection, uniform for all cells.
            double maxDim = 0;
            foreach (var r in origRects)
            {
                maxDim = Math.Max(maxDim, r.Width);
                maxDim = Math.Max(maxDim, r.Height);
            }
            if (maxDim <= 0)
                maxDim = cfg.Gap; // degenerate fallback, avoids zero pitch

            double pitch = maxDim + cfg.Gap;
            double maxDisplacement = cfg.Gap * cfg.MaxDisplacementFactor;

            // Centroid of original centers - grid is built symmetric around this point.
            UV centroid = GetCentroid(origCenters);

            // Baseline topology
            int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(n)));
            int rows = Math.Max(1, (int)Math.Ceiling((double)n / cols));

            int growthSteps = 0;
            Dictionary<int, UV> assignment = null; // itemIndex -> target center (UV)
            int overThreshold = 0;

            while (true)
            {
                var cells = BuildCellCenters(centroid, pitch, cols, rows);

                assignment = AssignItemsToCells(origCenters, cells);

                // Determine per-axis overflow: for items whose displacement exceeds
                // maxDisplacement, how far over are they, and along which axis is the
                // larger component of that excess (in absolute UV terms).
                double overflowU = 0;
                double overflowV = 0;
                overThreshold = 0;

                for (int i = 0; i < n; i++)
                {
                    UV target = assignment[i];
                    UV orig = origCenters[i];
                    double du = target.U - orig.U;
                    double dv = target.V - orig.V;
                    double dist = Math.Sqrt(du * du + dv * dv);

                    if (dist > maxDisplacement)
                    {
                        overThreshold++;
                        double excess = dist - maxDisplacement;
                        overflowU += Math.Abs(du) * (excess / dist);
                        overflowV += Math.Abs(dv) * (excess / dist);
                    }
                }

                if (overThreshold == 0)
                    break;

                if (cols >= MaxGridDim && rows >= MaxGridDim)
                    break; // give up growing, accept overThreshold violations

                growthSteps++;

                if (overflowU >= overflowV)
                {
                    if (cols < MaxGridDim)
                        cols++;
                    else if (rows < MaxGridDim)
                        rows++;
                    else
                        break;
                }
                else
                {
                    if (rows < MaxGridDim)
                        rows++;
                    else if (cols < MaxGridDim)
                        cols++;
                    else
                        break;
                }

                // Grid capacity must cover n items; if growth along the chosen axis
                // doesn't help (capacity already >= n but assignment still overflows
                // because of spatial spread, not raw count), keep growing anyway up
                // to the cap - handled naturally by the loop.
            }

            // Apply moves
            int moved = 0;
            for (int i = 0; i < n; i++)
            {
                UV target = assignment[i];
                UV orig = origCenters[i];
                UV delta = new UV(target.U - orig.U, target.V - orig.V);

                if (delta.U * delta.U + delta.V * delta.V < 1e-18)
                    continue;

                if (TryMove(doc, plane, items[i].Element, delta))
                    moved++;
            }

            report.Moved = moved;
            report.GridCols = cols;
            report.GridRows = rows;
            report.GrowthSteps = growthSteps;
            report.OverThreshold = overThreshold;

            return report;
        }

        // Builds cell center UVs for a cols x rows grid, centered on `centroid`.
        // Cell (0,0) is top-left when iterated, but the grid itself is symmetric
        // around centroid regardless of cols/rows parity.
        private static List<UV> BuildCellCenters(UV centroid, double pitch, int cols, int rows)
        {
            var cells = new List<UV>(cols * rows);

            // offsets so the grid is centered: indices run 0..cols-1 / 0..rows-1,
            // offset by -(cols-1)/2 and -(rows-1)/2 so the centroid sits at the
            // geometric center of the grid.
            double offsetU = -(cols - 1) / 2.0;
            double offsetV = (rows - 1) / 2.0; // rows go downward in V (top row = +V)

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    double u = centroid.U + (col + offsetU) * pitch;
                    double v = centroid.V + (offsetV - row) * pitch;
                    cells.Add(new UV(u, v));
                }
            }

            return cells;
        }

        // Greedy nearest-cell assignment: for each item (processed in order of
        // distance-to-nearest-available-cell, smallest first, so the items with the
        // tightest constraints get first pick), assign the closest still-available
        // cell. Returns itemIndex -> cell center.
        private static Dictionary<int, UV> AssignItemsToCells(List<UV> origCenters, List<UV> cells)
        {
            int n = origCenters.Count;
            var result = new Dictionary<int, UV>(n);
            var availableCells = new List<UV>(cells);

            // Precompute, for each item, the distance to its nearest available cell.
            // Recomputed each iteration since availability changes.
            var remainingItems = Enumerable.Range(0, n).ToList();

            while (remainingItems.Count > 0)
            {
                int bestItem = -1;
                int bestCellIdx = -1;
                double bestDist = double.PositiveInfinity;

                for (int ii = 0; ii < remainingItems.Count; ii++)
                {
                    int itemIdx = remainingItems[ii];
                    UV oc = origCenters[itemIdx];

                    for (int ci = 0; ci < availableCells.Count; ci++)
                    {
                        UV cell = availableCells[ci];
                        double du = cell.U - oc.U;
                        double dv = cell.V - oc.V;
                        double dist = du * du + dv * dv; // squared distance, fine for comparison

                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestItem = itemIdx;
                            bestCellIdx = ci;
                        }
                    }
                }

                // bestItem/bestCellIdx must be valid here: availableCells.Count >= remainingItems.Count
                // is guaranteed by the caller (cols*rows grown until capacity >= n in practice; if not,
                // see fallback below).
                if (bestItem < 0 || bestCellIdx < 0)
                {
                    // Fallback: more items than cells (shouldn't normally happen since
                    // cols*rows >= n always, but guard anyway). Assign remaining items
                    // to the centroid of remaining cells, or (0,0) if none left.
                    foreach (var idx in remainingItems)
                    {
                        result[idx] = availableCells.Count > 0 ? availableCells[0] : new UV(0, 0);
                    }
                    break;
                }

                result[bestItem] = availableCells[bestCellIdx];
                availableCells.RemoveAt(bestCellIdx);
                remainingItems.Remove(bestItem);
            }

            return result;
        }

        private static UV GetCentroid(List<UV> points)
        {
            if (points.Count == 0)
                return new UV(0, 0);

            double u = 0, v = 0;
            foreach (var p in points)
            {
                u += p.U;
                v += p.V;
            }

            return new UV(u / points.Count, v / points.Count);
        }

        private static List<Rect2D> GetRects(View view, ViewPlane2D plane, IList<AnnoItem> items, double margin)
        {
            var rects = new List<Rect2D>(items.Count);
            foreach (var it in items)
            {
                var bb = it.Element.get_BoundingBox(view);
                if (bb == null)
                {
                    rects.Add(new Rect2D(0, 0, 0, 0));
                    continue;
                }

                var r = AnnoGeometry.GetRectInViewPlane(plane, bb);
                rects.Add(margin > 0 ? r.Inflate(margin) : r);
            }
            return rects;
        }

        private static bool TryMove(Document doc, ViewPlane2D plane, Element e, UV deltaUv)
        {
            var deltaXyz = plane.DeltaToXYZ(deltaUv);
            return AnnoMove.TryMoveBy(doc, e, deltaXyz, out _);
        }

        private static UV Add(UV a, UV b) => new UV(a.U + b.U, a.V + b.V);
    }
}

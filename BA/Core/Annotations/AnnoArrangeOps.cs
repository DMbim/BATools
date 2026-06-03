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
        SnapToGuideLine = 6
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
                _ => new ArrangeReport { Total = items.Count }
            };
        }

        // ---------------- Distribute ----------------

        private static ArrangeReport Distribute(Document doc, View view, ViewPlane2D plane, IList<AnnoItem> items, bool horizontal)
        {
            var rects = GetRects(view, plane, items, margin: 0);
            var centers = rects.Select(r => r.Center()).ToList();

            var order = Enumerable.Range(0, items.Count)
                .OrderBy(i => horizontal ? centers[i].U : centers[i].V)
                .ToList();

            double min = order.Min(i => horizontal ? centers[i].U : centers[i].V);
            double max = order.Max(i => horizontal ? centers[i].U : centers[i].V);

            if (Math.Abs(max - min) < 1e-9)
                return new ArrangeReport { Total = items.Count };

            int n = order.Count;
            int moved = 0;

            for (int k = 0; k < n; k++)
            {
                int idx = order[k];
                double t = (n == 1) ? 0 : (double)k / (n - 1);
                double target = min + t * (max - min);

                UV c = centers[idx];
                UV delta = horizontal ? new UV(target - c.U, 0) : new UV(0, target - c.V);

                if (TryMove(doc, plane, items[idx].Element, delta))
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

        // ---------------- Collision resolve (the important one) ----------------

        private static ArrangeReport ResolveCollisions(Document doc, View view, ViewPlane2D plane, IList<AnnoItem> items, ArrangeConfig cfg)
        {
            int n = items.Count;
            var report = new ArrangeReport { Total = n, Iterations = cfg.Iterations };

            // Initial rects + margin
            var rects = GetRects(view, plane, items, margin: 0);
            double[] margins = rects.Select(r => cfg.UseAutoMargin ? AnnoGeometry.AutoMargin(r) : cfg.FixedMargin).ToArray();
            rects = rects.Select((r, i) => r.Inflate(margins[i])).ToList();

            report.InitiallyColliding = CountCollisions(rects);

            // Accumulate total delta per element in UV, apply once at end (more stable)
            UV[] total = Enumerable.Repeat(new UV(0, 0), n).ToArray();

            for (int iter = 0; iter < cfg.Iterations; iter++)
            {
                UV[] step = Enumerable.Repeat(new UV(0, 0), n).ToArray();
                bool any = false;

                for (int i = 0; i < n; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        if (!rects[i].IntersectsInclusive(rects[j]))
                            continue;

                        any = true;

                        // Minimum Translation Vector (axis-aligned in UV)
                        var mtv = ComputeMTV(rects[i], rects[j]); // move i by -mtv/2, j by +mtv/2
                        step[i] = Add(step[i], new UV(-0.5 * mtv.U, -0.5 * mtv.V));
                        step[j] = Add(step[j], new UV(+0.5 * mtv.U, +0.5 * mtv.V));
                    }
                }

                if (!any)
                    break;

                // apply damped step to rects (simulation space)
                for (int i = 0; i < n; i++)
                {
                    var d = new UV(step[i].U * cfg.Damping, step[i].V * cfg.Damping);
                    total[i] = Add(total[i], d);
                    rects[i] = rects[i].MoveBy(d);
                }
            }

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
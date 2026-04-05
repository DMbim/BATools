using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Keyplan
{
    public static class KeyplanGeometryUtils
    {
        private const double Eps = 1e-9;

        public static Curve FlattenCurveToWorldXY(Curve source)
        {
            if (source == null)
                return null;

            if (source is Line line)
            {
                XYZ p0 = FlattenPoint(line.GetEndPoint(0));
                XYZ p1 = FlattenPoint(line.GetEndPoint(1));

                if (p0.DistanceTo(p1) < Eps)
                    return null;

                return Line.CreateBound(p0, p1);
            }

            if (source is Arc arc)
            {
                XYZ p0 = FlattenPoint(arc.GetEndPoint(0));
                XYZ p1 = FlattenPoint(arc.GetEndPoint(1));
                XYZ pm = FlattenPoint(arc.Evaluate(0.5, true));

                if (AreCollinear2D(p0, pm, p1))
                {
                    if (p0.DistanceTo(p1) < Eps)
                        return null;

                    return Line.CreateBound(p0, p1);
                }

                return Arc.Create(p0, p1, pm);
            }

            if (source is Ellipse ellipse)
            {
                IList<XYZ> pts = SampleCurve(ellipse, 48)
                    .Select(FlattenPoint)
                    .ToList();

                return CreatePolylineAsNurbSpline(pts);
            }

            if (source is NurbSpline nurb)
            {
                IList<XYZ> pts = SampleCurve(nurb, 48)
                    .Select(FlattenPoint)
                    .ToList();

                return CreatePolylineAsNurbSpline(pts);
            }

            if (source is HermiteSpline hermite)
            {
                IList<XYZ> pts = SampleCurve(hermite, 48)
                    .Select(FlattenPoint)
                    .ToList();

                return CreatePolylineAsNurbSpline(pts);
            }

            IList<XYZ> fallbackPts = source.Tessellate()
                .Select(FlattenPoint)
                .ToList();

            return CreatePolylineAsNurbSpline(fallbackPts);
        }

        public static XYZ FlattenPoint(XYZ p)
        {
            if (p == null)
                return XYZ.Zero;

            return new XYZ(p.X, p.Y, 0.0);
        }

        private static IList<XYZ> SampleCurve(Curve curve, int segments)
        {
            List<XYZ> pts = new List<XYZ>();

            if (curve == null)
                return pts;

            if (segments < 2)
                segments = 2;

            for (int i = 0; i <= segments; i++)
            {
                double t = (double)i / segments;
                pts.Add(curve.Evaluate(t, true));
            }

            return pts;
        }

        private static Curve CreatePolylineAsNurbSpline(IList<XYZ> points)
        {
            if (points == null || points.Count < 2)
                return null;

            List<XYZ> cleaned = new List<XYZ>();
            XYZ last = null;

            foreach (XYZ p in points)
            {
                if (last == null || last.DistanceTo(p) > Eps)
                {
                    cleaned.Add(p);
                    last = p;
                }
            }

            if (cleaned.Count < 2)
                return null;

            List<double> weights = Enumerable.Repeat(1.0, cleaned.Count).ToList();
            return NurbSpline.CreateCurve(cleaned, weights);
        }

        private static bool AreCollinear2D(XYZ a, XYZ b, XYZ c)
        {
            double area2 = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            return Math.Abs(area2) < Eps;
        }
    }
}
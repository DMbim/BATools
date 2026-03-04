using Autodesk.Revit.DB;
using System;

namespace BA.BIM.Core.Annotations
{
    public readonly struct Rect2D
    {
        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }

        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;

        public Rect2D(double minX, double minY, double maxX, double maxY)
        {
            MinX = Math.Min(minX, maxX);
            MinY = Math.Min(minY, maxY);
            MaxX = Math.Max(minX, maxX);
            MaxY = Math.Max(minY, maxY);
        }

        public UV Center() => new UV((MinX + MaxX) * 0.5, (MinY + MaxY) * 0.5);

        public Rect2D Inflate(double d) => new Rect2D(MinX - d, MinY - d, MaxX + d, MaxY + d);

        public Rect2D MoveBy(UV d) => new Rect2D(MinX + d.U, MinY + d.V, MaxX + d.U, MaxY + d.V);

        // Strict: touching edges is NOT collision
        public bool Intersects(Rect2D o)
        {
            return !(o.MinX >= MaxX || o.MaxX <= MinX || o.MinY >= MaxY || o.MaxY <= MinY);
        }

        // Inclusive: touching edges counts as collision
        public bool IntersectsInclusive(Rect2D o)
        {
            return !(o.MinX > MaxX || o.MaxX < MinX || o.MinY > MaxY || o.MaxY < MinY);
        }
    }

    public static class AnnoGeometry
    {
        public static Rect2D GetRectInViewPlane(ViewPlane2D plane, BoundingBoxXYZ bb)
        {
            XYZ[] corners = new[]
            {
                new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
                new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z),
                new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z),
                new XYZ(bb.Min.X, bb.Max.Y, bb.Max.Z),
                new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z),
                new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z),
                new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z),
                new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z),
            };

            double minU = double.PositiveInfinity, minV = double.PositiveInfinity;
            double maxU = double.NegativeInfinity, maxV = double.NegativeInfinity;

            foreach (var c in corners)
            {
                UV uv = plane.ToUV(c);
                if (uv.U < minU) minU = uv.U;
                if (uv.V < minV) minV = uv.V;
                if (uv.U > maxU) maxU = uv.U;
                if (uv.V > maxV) maxV = uv.V;
            }

            return new Rect2D(minU, minV, maxU, maxV);
        }

        public static double AutoMargin(Rect2D r, double minMm = 1.5, double factor = 0.03)
        {
            double minInternal = UnitUtils.ConvertToInternalUnits(minMm, UnitTypeId.Millimeters);
            double m = Math.Min(r.Width, r.Height) * factor;
            return Math.Max(minInternal, m);
        }
    }
}
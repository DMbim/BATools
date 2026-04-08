using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.UI.KeyplanGrid
{
    public static class KeyplanScaleUtils
    {
        public static List<XYZ> ScalePolygon2D(
            IList<XYZ> polygon,
            double scaleFactor,
            XYZ origin = null)
        {
            List<XYZ> pts = polygon?
                .Where(p => p != null)
                .Select(KeyplanPolygonUtils.FlattenPoint)
                .ToList() ?? new List<XYZ>();

            if (pts.Count == 0)
                return new List<XYZ>();

            if (scaleFactor <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(scaleFactor), "Scale factor must be > 0.");

            XYZ o = origin ?? ComputeCentroid(pts);

            List<XYZ> scaled = new List<XYZ>(pts.Count);

            foreach (XYZ p in pts)
            {
                double x = o.X + (p.X - o.X) * scaleFactor;
                double y = o.Y + (p.Y - o.Y) * scaleFactor;
                scaled.Add(new XYZ(x, y, 0.0));
            }

            return scaled;
        }

        public static XYZ ComputeCentroid(IList<XYZ> pts)
        {
            if (pts == null || pts.Count == 0)
                return XYZ.Zero;

            double x = 0.0;
            double y = 0.0;

            foreach (XYZ p in pts)
            {
                x += p.X;
                y += p.Y;
            }

            return new XYZ(x / pts.Count, y / pts.Count, 0.0);
        }
    }
}
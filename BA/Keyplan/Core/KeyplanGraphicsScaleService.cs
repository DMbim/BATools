using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.UI.KeyplanGrid
{
    public static class KeyplanGraphicScaleService
    {
        public static KeyplanGraphicModel ScaleModel(
            KeyplanGraphicModel source,
            double scaleFactor)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (scaleFactor <= 0.0) throw new ArgumentOutOfRangeException(nameof(scaleFactor), "Scale factor must be > 0.");

            XYZ origin = ComputeModelCentroid(source);

            KeyplanGraphicModel scaled = new KeyplanGraphicModel();

            foreach (KeyplanPolygonGraphicItem item in source.FilledRegions)
            {
                if (item == null)
                    continue;

                scaled.FilledRegions.Add(new KeyplanPolygonGraphicItem
                {
                    StableKey = item.StableKey,
                    Polygon = ScalePolygon(item.Polygon, origin, scaleFactor)
                });
            }

            foreach (KeyplanLineGraphicItem item in source.GridLines)
            {
                if (item == null)
                    continue;

                scaled.GridLines.Add(new KeyplanLineGraphicItem
                {
                    StableKey = item.StableKey,
                    A = ScalePoint(item.A, origin, scaleFactor),
                    B = ScalePoint(item.B, origin, scaleFactor)
                });
            }

            foreach (KeyplanLineGraphicItem item in source.OutlineLines)
            {
                if (item == null)
                    continue;

                scaled.OutlineLines.Add(new KeyplanLineGraphicItem
                {
                    StableKey = item.StableKey,
                    A = ScalePoint(item.A, origin, scaleFactor),
                    B = ScalePoint(item.B, origin, scaleFactor)
                });
            }

            return scaled;
        }

        public static XYZ ComputeModelCentroid(KeyplanGraphicModel model)
        {
            if (model == null)
                return XYZ.Zero;

            List<XYZ> pts = new List<XYZ>();

            foreach (KeyplanPolygonGraphicItem poly in model.FilledRegions ?? Enumerable.Empty<KeyplanPolygonGraphicItem>())
            {
                if (poly?.Polygon == null)
                    continue;

                pts.AddRange(poly.Polygon.Where(p => p != null).Select(KeyplanPolygonUtils.FlattenPoint));
            }

            foreach (KeyplanLineGraphicItem line in model.GridLines ?? Enumerable.Empty<KeyplanLineGraphicItem>())
            {
                if (line == null)
                    continue;

                pts.Add(KeyplanPolygonUtils.FlattenPoint(line.A));
                pts.Add(KeyplanPolygonUtils.FlattenPoint(line.B));
            }

            foreach (KeyplanLineGraphicItem line in model.OutlineLines ?? Enumerable.Empty<KeyplanLineGraphicItem>())
            {
                if (line == null)
                    continue;

                pts.Add(KeyplanPolygonUtils.FlattenPoint(line.A));
                pts.Add(KeyplanPolygonUtils.FlattenPoint(line.B));
            }

            if (pts.Count == 0)
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

        private static List<XYZ> ScalePolygon(IList<XYZ> polygon, XYZ origin, double scaleFactor)
        {
            List<XYZ> result = new List<XYZ>();
            foreach (XYZ p in polygon ?? Enumerable.Empty<XYZ>())
            {
                if (p == null)
                    continue;

                result.Add(ScalePoint(p, origin, scaleFactor));
            }

            return result;
        }

        private static XYZ ScalePoint(XYZ point, XYZ origin, double scaleFactor)
        {
            XYZ p = KeyplanPolygonUtils.FlattenPoint(point);
            XYZ o = KeyplanPolygonUtils.FlattenPoint(origin);

            return new XYZ(
                o.X + (p.X - o.X) * scaleFactor,
                o.Y + (p.Y - o.Y) * scaleFactor,
                0.0);
        }
    }
}
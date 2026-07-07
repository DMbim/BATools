using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BA.UI.KeyplanGrid
{
    public static class KeyplanGeometryKeyService
    {
        public static string MakePointKey(XYZ p)
        {
            XYZ fp = KeyplanPolygonUtils.FlattenPoint(p);
            long x = (long)Math.Round(fp.X / KeyplanGeometryTolerance.KeyRounding);
            long y = (long)Math.Round(fp.Y / KeyplanGeometryTolerance.KeyRounding);

            return x.ToString(CultureInfo.InvariantCulture) + "," +
                   y.ToString(CultureInfo.InvariantCulture);
        }

        public static string MakeUndirectedLineKey(XYZ a, XYZ b)
        {
            string ka = MakePointKey(a);
            string kb = MakePointKey(b);

            return string.CompareOrdinal(ka, kb) <= 0
                ? ka + "|" + kb
                : kb + "|" + ka;
        }

        public static string MakePolygonKey(IList<XYZ> polygon)
        {
            List<XYZ> pts = KeyplanPolygonUtils.CleanPolygonStrict(polygon);
            if (pts == null || pts.Count == 0)
                return string.Empty;

            List<string> keys = pts.Select(MakePointKey).ToList();
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

    }
}
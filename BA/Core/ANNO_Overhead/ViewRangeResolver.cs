using System.Linq;
using Autodesk.Revit.DB;

namespace BA.Core.Overhead
{
    public static class ViewRangeResolver
    {
        public static (double cutZ, double topZ) ResolveCutTopZ(Document doc, ViewPlan view, OverheadSettings s)
        {
            double cutZ = ResolvePlaneZ(doc, view, PlanViewPlane.CutPlane);
            if (double.IsNaN(cutZ))
            {
                var baseLevel = view.GenLevel;
                double fallbackFt = UnitUtils.ConvertToInternalUnits(
                    (s.FallbackCutMm > 0 ? s.FallbackCutMm : 1200.0), UnitTypeId.Millimeters);
                cutZ = (baseLevel?.Elevation ?? 0.0) + fallbackFt;
            }

            double viewTopZ = ResolvePlaneZ(doc, view, PlanViewPlane.TopClipPlane);
            double nextLevel = NextLevelElevation(doc, view);

            double topCand = s.UseNextLevelAsTop ? System.Math.Min(viewTopZ, nextLevel) : viewTopZ;

            if (double.IsInfinity(topCand) || double.IsNaN(topCand))
                topCand = viewTopZ;

            if (double.IsInfinity(topCand) || double.IsNaN(topCand))
                topCand = cutZ + UnitUtils.ConvertToInternalUnits(1000.0, UnitTypeId.Millimeters);

            double minBand = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
            double topZ = System.Math.Max(topCand, cutZ + minBand);

            return (cutZ, topZ);
        }

        private static double ResolvePlaneZ(Document doc, ViewPlan view, PlanViewPlane plane)
        {
            try
            {
                var range = view.GetViewRange();
                var lvlId = range.GetLevelId(plane);
                var off = range.GetOffset(plane);
                var lvl = doc.GetElement(lvlId) as Level;
                if (lvl != null) return lvl.Elevation + off;
            }
            catch { }
            return double.NaN;
        }

        private static double NextLevelElevation(Document doc, ViewPlan view)
        {
            var baseLevel = view.GenLevel;
            if (baseLevel == null) return double.PositiveInfinity;

            double baseZ = baseLevel.Elevation;

            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .Where(l => l.Elevation > baseZ)
                .OrderBy(l => l.Elevation)
                .ToList();

            return levels.Count > 0 ? levels.First().Elevation : double.PositiveInfinity;
        }
    }
}

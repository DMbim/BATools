using System;
using System.Reflection;
using Autodesk.Revit.DB;

namespace BA.Core.Overhead
{
    public static class StyleMapper
    {
        private static readonly MethodInfo? _miGetLineWeight =
            typeof(Category).GetMethod("GetLineWeight", new[] { typeof(GraphicsStyleType) });

        public static OverrideGraphicSettings BuildOGSFrom(GraphicsStyle gs)
        {
            var ogs = new OverrideGraphicSettings();
            if (gs == null) return ogs;

            var cat = gs.GraphicsStyleCategory;
            if (cat == null) return ogs;

            var lw = GetLineWeightSafe(cat, GraphicsStyleType.Projection)
                     ?? GetLineWeightSafe(cat, GraphicsStyleType.Cut);

            if (lw.HasValue && lw.Value > 0)
                ogs.SetProjectionLineWeight(lw.Value);

            var color = cat.LineColor;
            if (color != null)
                ogs.SetProjectionLineColor(color);

            var lpid = cat.GetLinePatternId(GraphicsStyleType.Projection);
            if (lpid != ElementId.InvalidElementId)
                ogs.SetProjectionLinePatternId(lpid);

            return ogs;
        }


        private static int? GetLineWeightSafe(Category cat, GraphicsStyleType gst)
        {
            try
            {
                if (_miGetLineWeight == null) return null;
                var obj = _miGetLineWeight.Invoke(cat, new object[] { gst });
                if (obj == null) return null;
                return Convert.ToInt32(obj);
            }
            catch
            {
                return null;
            }
        }
    }
}

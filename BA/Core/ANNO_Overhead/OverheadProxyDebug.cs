// File: BA.Core.Overhead/OverheadProxyDebug.cs
using Autodesk.Revit.DB;
using System;
using System.Linq;
using View = Autodesk.Revit.DB.View;
using System.Text;

namespace BA.Core.Overhead
{
    public static class OverheadProxyDebug
    {
        public static string DescribeOverheadProxiesInView(Document doc, View view, string expectedLineStyleName)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (view == null) throw new ArgumentNullException(nameof(view));

            // Try common annotation proxy types: DetailCurve, FilledRegion, GenericAnnotation
            var els = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .ToElements();

            int count = 0;
            var sb = new StringBuilder();

            foreach (var e in els)
            {
                // Heuristic: your ProxyManager likely marks them somehow; if you have a known parameter or name prefix, use it.
                // For now, detect detail curves that use the expected linestyle.
                if (e is DetailCurve dc)
                {
                    var gs = dc.LineStyle as GraphicsStyle;
                    var name = gs?.Name ?? "(null)";
                    if (!string.Equals(name, expectedLineStyleName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    count++;
                    var catName = e.Category?.Name ?? "(no category)";
                    var catId = e.Category?.Id?.Value ?? -1;
                    var lsCat = gs?.GraphicsStyleCategory;
                    var lsCatId = lsCat?.Id?.Value ?? -1;

                    sb.AppendLine($"DetailCurve Id={e.Id.Value} Cat={catName} CatId={catId} LineStyle={name} LineStyleCatId={lsCatId}");
                }
            }

            if (count == 0)
                return $"No DetailCurves found in view '{view.Name}' using line style '{expectedLineStyleName}'.";

            return $"Found {count} DetailCurves in view '{view.Name}' using '{expectedLineStyleName}':\n{sb}";
        }
    }
}
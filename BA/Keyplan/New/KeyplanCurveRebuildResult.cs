using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace BA.Keyplan
{
    public sealed class KeyplanCurveRebuildResult
    {
        public int SourceCurveCount { get; set; }
        public int SourceAreaLoopCount { get; set; }
        public int CreatedCount { get; set; }
        public int SkippedCount { get; set; }
    }

    public static class KeyplanCurveRebuilder
    {
        public static KeyplanCurveRebuildResult RecreateVisibleNonViewSpecificCurves(
            Document doc,
            View sourceView,
            ViewDrafting targetView)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sourceView == null) throw new ArgumentNullException(nameof(sourceView));
            if (targetView == null) throw new ArgumentNullException(nameof(targetView));

            KeyplanCurveRebuildResult result = new KeyplanCurveRebuildResult();

            //
            // 1. Rebuild supported non-view-specific CurveElements
            //
            IList<CurveElement> sourceCurves =
                KeyplanElementCollector.CollectVisibleNonViewSpecificCurveElements(doc, sourceView);

            result.SourceCurveCount = sourceCurves.Count;

            foreach (CurveElement sourceCurveElement in sourceCurves)
            {
                try
                {
                    Curve sourceCurve = sourceCurveElement.GeometryCurve;
                    if (sourceCurve == null)
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    IList<Curve> flattenedCurves = (IList<Curve>)KeyplanGeometryUtils.FlattenCurveToWorldXY(sourceCurve);
                    if (flattenedCurves == null || flattenedCurves.Count == 0)
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    int createdForThisSource = 0;

                    foreach (Curve flattened in flattenedCurves)
                    {
                        if (flattened == null || !flattened.IsBound)
                            continue;

                        DetailCurve newDetailCurve = doc.Create.NewDetailCurve(targetView, flattened);
                        if (newDetailCurve == null)
                            continue;

                        CopyLineStyleIfPossible(sourceCurveElement, newDetailCurve);

                        result.CreatedCount++;
                        createdForThisSource++;
                    }

                    if (createdForThisSource == 0)
                    {
                        result.SkippedCount++;
                    }
                }
                catch
                {
                    result.SkippedCount++;
                }
            }

            //
            // 2. Rebuild Area boundary loops
            //
            IList<CurveLoop> areaLoops = KeyplanElementCollector.GetAreaBoundaryLoops(doc, sourceView);
            result.SourceAreaLoopCount = areaLoops.Count;

            foreach (CurveLoop loop in areaLoops)
            {
                try
                {
                    int createdForThisLoop = 0;

                    foreach (Curve c in loop)
                    {
                        if (c == null)
                            continue;

                        IList<Curve> flattenedCurves = (IList<Curve>)KeyplanGeometryUtils.FlattenCurveToWorldXY(c);
                        if (flattenedCurves == null || flattenedCurves.Count == 0)
                            continue;

                        foreach (Curve flattened in flattenedCurves)
                        {
                            if (flattened == null || !flattened.IsBound)
                                continue;

                            DetailCurve newDetailCurve = doc.Create.NewDetailCurve(targetView, flattened);
                            if (newDetailCurve == null)
                                continue;

                            result.CreatedCount++;
                            createdForThisLoop++;
                        }
                    }

                    if (createdForThisLoop == 0)
                    {
                        result.SkippedCount++;
                    }
                }
                catch
                {
                    result.SkippedCount++;
                }
            }

            return result;
        }

        private static void CopyLineStyleIfPossible(CurveElement source, CurveElement target)
        {
            if (source == null || target == null)
                return;

            try
            {
                GraphicsStyle sourceStyle = source.LineStyle as GraphicsStyle;
                if (sourceStyle == null)
                    return;

                Category linesCategory = target.Document.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                if (linesCategory == null)
                    return;

                foreach (Category subCat in linesCategory.SubCategories)
                {
                    if (subCat == null)
                        continue;

                    if (!string.Equals(subCat.Name, sourceStyle.Name, StringComparison.OrdinalIgnoreCase))
                        continue;

                    GraphicsStyle gs = subCat.GetGraphicsStyle(GraphicsStyleType.Projection);
                    if (gs != null)
                    {
                        target.LineStyle = gs;
                    }

                    return;
                }
            }
            catch
            {
                // Ignore line-style transfer failures.
            }
        }
    }
}
// File: BA/Core/CurveToElement/Services/DetailLineClassificationService.cs
// Action: REPLACE (full file)

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.Core.CurveToElement.Models;

namespace BA.Core.CurveToElement.Services
{
    /// <summary>
    /// Groups selected curve-based elements (DetailCurve and ModelCurve, both deriving from the
    /// CurveElement base class) by their GraphicsStyle (line style/subcategory). This is the
    /// Revit-native classification strategy. A future CAD-import strategy should produce the
    /// same output shape (List&lt;CurveTypeGroup&gt;), keyed by layer, so downstream
    /// chaining/generation code does not need to change.
    ///
    /// Wall.Create's location-line workflow only accepts Line or Arc as a wall's centerline -
    /// there is no native ellipse-wall or spline-wall element in Revit. Any source curve that is
    /// not a Line or Arc (Ellipse, NurbSpline, HermiteSpline, etc.) is faceted here into a chain
    /// of straight Line segments via Curve.Tessellate() BEFORE it becomes a ClassifiableCurve, so
    /// every downstream consumer (CurveChainBuilder, WallFaceOffsetPreviewHandler,
    /// WallGenerationService) only ever sees Line/Arc geometry and never needs to know an
    /// approximation happened. All facets produced from one source curve share that curve's
    /// SourceElementId, so group-level source-line deletion in WallGenerationService still maps
    /// back to the single original element, not to each individual facet.
    /// </summary>
    public class DetailLineClassificationService
    {
        public List<CurveTypeGroup> ClassifyByLineStyle(Document doc, IList<ElementId> curveElementIds) // <- CHANGED (param renamed, was detailCurveIds)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (curveElementIds == null) throw new ArgumentNullException(nameof(curveElementIds));

            double shortCurveTolerance = doc.Application.ShortCurveTolerance; // <- NEW, used to reject degenerate facets
            var groups = new Dictionary<ElementId, CurveTypeGroup>();

            foreach (ElementId id in curveElementIds)
            {
                Element element = doc.GetElement(id);
                if (!(element is CurveElement curveElement)) // <- CHANGED (was DetailCurve detailCurve)
                {
                    AppLogger.LogInfo($"[CurveToElement] Skipped element {id.Value} - not a DetailCurve or ModelCurve.");
                    continue;
                }

                Curve sourceCurve = curveElement.GeometryCurve; // <- CHANGED (was detailCurve.GeometryCurve)
                if (sourceCurve == null)
                {
                    AppLogger.LogInfo($"[CurveToElement] Skipped curve element {id.Value} - null geometry curve.");
                    continue;
                }

                GraphicsStyle style = (GraphicsStyle)curveElement.LineStyle; // <- CHANGED (was detailCurve.LineStyle)
                ElementId styleId = style?.Id ?? ElementId.InvalidElementId;
                string styleName = style?.Name ?? "<Unnamed Line Style>";

                if (!groups.TryGetValue(styleId, out CurveTypeGroup group))
                {
                    group = new CurveTypeGroup(styleId, styleName);
                    groups[styleId] = group;
                }

                // <- NEW block: facet anything Wall.Create can't accept directly, then add one
                // ClassifiableCurve per facet, all sharing this element's SourceElementId.
                List<Curve> wallCompatibleSegments = FacetToWallCompatibleSegments(sourceCurve, shortCurveTolerance, id);
                foreach (Curve segment in wallCompatibleSegments)
                {
                    group.Curves.Add(new ClassifiableCurve(id, segment, styleName, styleId));
                }
            }

            return groups.Values
                .OrderBy(g => g.StyleName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Line and Arc pass through unchanged - both are natively supported as a Wall
        /// centerline. Anything else (Ellipse, NurbSpline, HermiteSpline, ...) is approximated
        /// as a polyline via Curve.Tessellate(), the same tessellation strategy
        /// CurveChain.ComputeNewellNormal already relies on elsewhere in this pipeline. Facets
        /// shorter than the document's short-curve tolerance are dropped rather than passed to
        /// Line.CreateBound, which throws ArgumentException for near-coincident endpoints.
        /// </summary>
        private static List<Curve> FacetToWallCompatibleSegments(Curve sourceCurve, double shortCurveTolerance, ElementId sourceElementId) // <- NEW
        {
            if (sourceCurve is Line || sourceCurve is Arc)
                return new List<Curve> { sourceCurve };

            var segments = new List<Curve>();
            IList<XYZ> tessellated = sourceCurve.Tessellate();

            for (int i = 0; i < tessellated.Count - 1; i++)
            {
                XYZ start = tessellated[i];
                XYZ end = tessellated[i + 1];

                if (start.DistanceTo(end) <= shortCurveTolerance)
                    continue; // degenerate facet, skip rather than throw

                segments.Add(Line.CreateBound(start, end));
            }

            if (segments.Count == 0)
            {
                AppLogger.LogInfo(
                    $"[CurveToElement] Element {sourceElementId.Value}: faceting a {sourceCurve.GetType().Name} " +
                    "produced no usable segments (curve shorter than the document's short-curve tolerance).");
            }

            return segments;
        }
    }
}
// File: BA/Core/CurveToElement/Services/DetailLineClassificationService.cs
// Action: CREATE NEW

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.Core.CurveToElement.Models;

namespace BA.Core.CurveToElement.Services
{
    /// <summary>
    /// Groups selected DetailCurve elements by their GraphicsStyle (line style/subcategory).
    /// This is the Revit-detail-line-specific classification strategy. A future CAD-import
    /// strategy should produce the same output shape (List&lt;CurveTypeGroup&gt;), keyed by layer,
    /// so downstream chaining/generation code does not need to change.
    /// </summary>
    public class DetailLineClassificationService
    {
        public List<CurveTypeGroup> ClassifyByLineStyle(Document doc, IList<ElementId> detailCurveIds)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (detailCurveIds == null) throw new ArgumentNullException(nameof(detailCurveIds));

            var groups = new Dictionary<ElementId, CurveTypeGroup>();

            foreach (ElementId id in detailCurveIds)
            {
                Element element = doc.GetElement(id);
                if (!(element is DetailCurve detailCurve))
                {
                    AppLogger.LogInfo($"[CurveToElement] Skipped element {id.Value} - not a DetailCurve.");
                    continue;
                }

                Curve curve = detailCurve.GeometryCurve;
                if (curve == null)
                {
                    AppLogger.LogInfo($"[CurveToElement] Skipped DetailCurve {id.Value} - null geometry curve.");
                    continue;
                }

                GraphicsStyle style = (GraphicsStyle)detailCurve.LineStyle;
                ElementId styleId = style?.Id ?? ElementId.InvalidElementId;
                string styleName = style?.Name ?? "<Unnamed Line Style>";

                if (!groups.TryGetValue(styleId, out CurveTypeGroup group))
                {
                    group = new CurveTypeGroup(styleId, styleName);
                    groups[styleId] = group;
                }

                group.Curves.Add(new ClassifiableCurve(id, curve, styleName, styleId));
            }

            return groups.Values
                .OrderBy(g => g.StyleName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
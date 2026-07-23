// File: BA/Core/CurveToElement/Models/ClassifiableCurve.cs
// Action: CREATE NEW

using System;
using Autodesk.Revit.DB;

namespace BA.Core.CurveToElement.Models
{
    /// <summary>
    /// Wraps a single source curve (Revit DetailCurve today, CAD import geometry later)
    /// with a classification key so the classification/chaining pipeline is source-agnostic.
    /// </summary>
    public class ClassifiableCurve
    {
        public ElementId SourceElementId { get; }
        public Curve Curve { get; }
        public string StyleKey { get; }
        public ElementId GraphicsStyleId { get; }

        public ClassifiableCurve(ElementId sourceElementId, Curve curve, string styleKey, ElementId graphicsStyleId)
        {
            SourceElementId = sourceElementId ?? throw new ArgumentNullException(nameof(sourceElementId));
            Curve = curve ?? throw new ArgumentNullException(nameof(curve));
            StyleKey = styleKey ?? string.Empty;
            GraphicsStyleId = graphicsStyleId ?? ElementId.InvalidElementId;
        }
    }
}
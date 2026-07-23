// File: BA/Core/CurveToElement/Models/CurveTypeGroup.cs
// Action: CREATE NEW

using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BA.Core.CurveToElement.Models
{
    /// <summary>
    /// One classification bucket (all detail lines sharing a GraphicsStyle) plus the
    /// wall-generation settings the user configures for that bucket in the UI.
    /// </summary>
    public class CurveTypeGroup
    {
        public ElementId GraphicsStyleId { get; }
        public string StyleName { get; }
        public List<ClassifiableCurve> Curves { get; }
        public WallGroupSettings Settings { get; set; }

        public CurveTypeGroup(ElementId graphicsStyleId, string styleName)
        {
            GraphicsStyleId = graphicsStyleId ?? ElementId.InvalidElementId;
            StyleName = string.IsNullOrWhiteSpace(styleName) ? "<Unnamed Line Style>" : styleName;
            Curves = new List<ClassifiableCurve>();
            Settings = new WallGroupSettings();
        }
    }
}
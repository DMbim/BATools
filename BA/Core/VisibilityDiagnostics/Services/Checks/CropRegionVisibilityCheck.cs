// File: BA_Tools/VisibilityDiagnostics/Services/Checks/CropRegionVisibilityCheck.cs
using System;
using Autodesk.Revit.DB;
using BA.VisibilityDiagnostics.Models;

namespace BA.VisibilityDiagnostics.Services.Checks
{
    /// <summary>
    /// Checks whether the element's model-space bounding box falls entirely outside
    /// the view's active Crop Region (which a Scope Box, if assigned, drives).
    ///
    /// Two disclosed approximations, same spirit as ViewRangeVisibilityCheck:
    /// 1. Tests the X/Y extents of the element's bounding box against
    ///    View.CropBox, transformed into the crop's own local coordinate system via
    ///    CropBox.Transform.Inverse (confirmed pattern - CropBox is defined in a
    ///    local, potentially-rotated coordinate system, not model coordinates
    ///    directly). Z (Far Clip Offset) is a related-but-separate setting and is
    ///    NOT tested here.
    /// 2. Since Revit 2022, "Edit Crop" can reshape a crop region into a
    ///    non-rectangular polygon. This check only compares against the crop's
    ///    rectangular bounding extents, so a non-rectangular crop can still exclude
    ///    an element that this check reports as visible. // <- KNOWN LIMITATION
    /// </summary>
    public sealed class CropRegionVisibilityCheck : IVisibilityCheck
    {
        public string CheckName => "Crop Region / Scope Box";

        public VisibilityCheckItem Evaluate(Document document, View view, Element element)
        {
            bool cropActive;
            try
            {
                cropActive = view.CropBoxActive;
            }
            catch (Exception ex)
            {
                return VisibilityCheckItem.Error(CheckName, $"Could not read Crop Box state for view '{view.Name}': {ex.Message}");
            }

            if (!cropActive)
            {
                return VisibilityCheckItem.NotApplicable(CheckName, $"Crop Region is not active for view '{view.Name}'.");
            }

            BoundingBoxXYZ cropBox = view.CropBox;
            if (cropBox is null)
            {
                return VisibilityCheckItem.Warning(CheckName, "Crop Region is active but returned no geometry.");
            }

            BoundingBoxXYZ? elementBox = element.get_BoundingBox(null);
            if (elementBox is null)
            {
                return VisibilityCheckItem.Warning(CheckName,
                    "Element has no model-space bounding box. Crop Region comparison cannot be evaluated for it.");
            }

            Transform inverse = cropBox.Transform.Inverse;
            XYZ[] corners =
            {
                new XYZ(elementBox.Min.X, elementBox.Min.Y, elementBox.Min.Z),
                new XYZ(elementBox.Max.X, elementBox.Min.Y, elementBox.Min.Z),
                new XYZ(elementBox.Min.X, elementBox.Max.Y, elementBox.Min.Z),
                new XYZ(elementBox.Max.X, elementBox.Max.Y, elementBox.Min.Z),
                new XYZ(elementBox.Min.X, elementBox.Min.Y, elementBox.Max.Z),
                new XYZ(elementBox.Max.X, elementBox.Min.Y, elementBox.Max.Z),
                new XYZ(elementBox.Min.X, elementBox.Max.Y, elementBox.Max.Z),
                new XYZ(elementBox.Max.X, elementBox.Max.Y, elementBox.Max.Z),
            };

            double localMinX = double.MaxValue, localMaxX = double.MinValue;
            double localMinY = double.MaxValue, localMaxY = double.MinValue;
            foreach (XYZ corner in corners)
            {
                XYZ local = inverse.OfPoint(corner);
                localMinX = Math.Min(localMinX, local.X);
                localMaxX = Math.Max(localMaxX, local.X);
                localMinY = Math.Min(localMinY, local.Y);
                localMaxY = Math.Max(localMaxY, local.Y);
            }

            bool outsideX = localMaxX < cropBox.Min.X || localMinX > cropBox.Max.X;
            bool outsideY = localMaxY < cropBox.Min.Y || localMinY > cropBox.Max.Y;

            string scopeBoxNote = string.Empty;
            Parameter? scopeBoxParam = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            if (scopeBoxParam is not null)
            {
                ElementId scopeBoxId = scopeBoxParam.AsElementId();
                if (scopeBoxId != ElementId.InvalidElementId && document.GetElement(scopeBoxId) is Element scopeBoxElement)
                {
                    scopeBoxNote = $" (Scope Box '{scopeBoxElement.Name}' drives this crop.)";
                }
            }

            if (outsideX || outsideY)
            {
                return VisibilityCheckItem.Hidden(CheckName,
                    $"Element's bounding box falls entirely outside the view's Crop Region extents in view " +
                    $"'{view.Name}'.{scopeBoxNote} (Rectangular-extent approximation - a non-rectangular 'Edit Crop' " +
                    "shape or Far Clip Offset are not tested by this check.)",
                    recommendation: "Resize/reposition the Crop Region (or the assigned Scope Box) so it includes this " +
                        "element, or deactivate Crop Region for this view.");
            }

            return VisibilityCheckItem.Ok(CheckName,
                $"Element's bounding box falls within the view's Crop Region extents in view '{view.Name}'.{scopeBoxNote} " +
                "Crop Region is not hiding this element (rectangular-extent approximation).");
        }
    }
}

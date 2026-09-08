using System;
using Autodesk.Revit.DB;

namespace BA.Zoom.Helpers
{
    /// <summary>
    /// Ensures a target rectangle (already computed in host document model space, Z = 0,
    /// consistent with ZoomGeometryHelper's convention) is not clipped by the active view's
    /// crop box before a UIView.ZoomAndCenterRectangle call. UIView.ZoomAndCenterRectangle is
    /// constrained by an active crop box: it will not pan or zoom outside it, so a target
    /// outside the crop produces a call that reports success while the view shows only the
    /// (possibly empty) crop area. This helper detects that condition and disables CropBoxActive
    /// so the subsequent zoom is not clipped.
    ///
    /// A Scope Box assigned to the view (BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP) also
    /// controls the crop and locks the "Crop View" toggle in the Revit UI. Setting
    /// CropBoxActive = false alone does not throw and does not necessarily free the view from the
    /// Scope Box's control; the Scope Box assignment must be cleared first for the crop to
    /// actually open.
    /// </summary>
    internal static class ZoomCropHelper
    {
        /// <summary>
        /// If the view's crop is active and the given rectangle falls outside the current crop
        /// extents, clears any assigned Scope Box and disables CropBoxActive in its own transaction
        /// so the caller's subsequent ZoomAndCenterRectangle call is not clipped.
        /// Returns true if crop was disabled (caller should mention this to the user, using
        /// adjustmentNote).
        /// Returns false if no change was made, either because crop was already inactive or
        /// already large enough, or because the crop is controlled by a View Template and could
        /// not be changed. In that last case cropLockedMessage is set and the caller should
        /// surface it; the zoom call should still proceed as a best effort attempt since the
        /// crop cannot be forced open from here.
        /// Requires the caller's command to run under TransactionMode.Manual. This method opens
        /// and commits or rolls back its own transaction and does not expect one to already be open.
        /// </summary>
        public static bool EnsureRectangleVisible(
            Document doc,
            View view,
            XYZ minXY,
            XYZ maxXY,
            out string cropLockedMessage,
            out string adjustmentNote
        )
        {
            cropLockedMessage = null;
            adjustmentNote = null;

            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (minXY == null) throw new ArgumentNullException(nameof(minXY));
            if (maxXY == null) throw new ArgumentNullException(nameof(maxXY));

            if (!view.CropBoxActive)
                return false;

            if (IsRectangleInsideCropBox(view, minXY, maxXY))
                return false;

            using (var t = new Transaction(doc, "Zoom: Disable View Crop"))
            {
                t.Start();
                try
                {
                    string clearedScopeBoxName = null; // <- NEW

                    var scopeBoxParam = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP); // <- NEW
                    if (scopeBoxParam != null && !scopeBoxParam.IsReadOnly) // <- NEW
                    {
                        var scopeBoxId = scopeBoxParam.AsElementId(); // <- NEW
                        if (scopeBoxId != null && scopeBoxId != ElementId.InvalidElementId) // <- NEW
                        {
                            clearedScopeBoxName = doc.GetElement(scopeBoxId)?.Name; // <- NEW
                            scopeBoxParam.Set(ElementId.InvalidElementId); // <- NEW
                        }
                    }

                    view.CropBoxActive = false;

                    adjustmentNote = clearedScopeBoxName != null // <- NEW, was never assigned before
                        ? $"Crop View was turned off and this view's Scope Box ('{clearedScopeBoxName}') was cleared, because the target was outside it."
                        : "Crop View was turned off because the target was outside it.";
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    cropLockedMessage =
                        "The active view's crop region is controlled by a View Template and could not be " +
                        "disabled automatically (" + ex.Message + "). The view may still appear clipped. " +
                        "Detach the view's template or turn off crop control in the template, then try again.";
                    return false;
                }
                t.Commit();
            }

            return true;
        }

        /// <summary>
        /// Tests whether all four corners of the target rectangle fall within the view's crop box,
        /// evaluated in the crop box's own local coordinate system. Only local X/Y are checked;
        /// local Z corresponds to the view range (top/bottom cut planes), not the pannable/zoomable
        /// extent, and is irrelevant here. Z = 0 is used for every test corner, matching
        /// ZoomGeometryHelper's convention. This is safe for plan like views (FloorPlan, CeilingPlan,
        /// EngineeringPlan, AreaPlan) because their crop box transform is axis aligned in XY, so the
        /// world Z chosen for the test point does not leak into the computed local X/Y.
        /// </summary>
        private static bool IsRectangleInsideCropBox(View view, XYZ minXY, XYZ maxXY)
        {
            var cropBox = view.CropBox;
            if (cropBox == null) return true;

            Transform inv;
            try
            {
                inv = cropBox.Transform.Inverse;
            }
            catch
            {
                return true;
            }

            XYZ[] corners =
            {
                new XYZ(minXY.X, minXY.Y, 0),
                new XYZ(maxXY.X, minXY.Y, 0),
                new XYZ(minXY.X, maxXY.Y, 0),
                new XYZ(maxXY.X, maxXY.Y, 0)
            };

            foreach (var c in corners)
            {
                XYZ local = inv.OfPoint(c);
                if (local.X < cropBox.Min.X || local.X > cropBox.Max.X ||
                    local.Y < cropBox.Min.Y || local.Y > cropBox.Max.Y)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
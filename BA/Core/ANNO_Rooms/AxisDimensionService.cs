using System;
using Autodesk.Revit.DB;
using View = Autodesk.Revit.DB.View;
using BA.Core.Dim;

namespace BA.Core.Rooms
{
    /// <summary>
    /// Creates two-segment linear Dimension elements off a placed BA_Axis instance,
    /// using the family's named references: Start_X/Mid_X/End_X and Start_Y/Mid_Y/End_Y.
    /// Requires those six reference planes (Is Reference = Weak Reference, named exactly
    /// as above) to exist in the family -- see project notes on BA_Axis.rfa authoring.
    ///
    /// The dimension LINE geometry (where the string is drawn) is built directly from
    /// the room's host-XY bounding box, the same min/max DetailPlacer used to size and
    /// position the instance -- NOT re-derived from the references. The references only
    /// supply the witness points (what gets measured); they don't need to be resolved
    /// back to coordinates for this to work correctly.
    /// </summary>
    public static class AxisDimensionService
    {
        private const string RefStartX = "Start_X";
        private const string RefMidX = "Mid_X";
        private const string RefEndX = "End_X";
        private const string RefStartY = "Start_Y";
        private const string RefMidY = "Mid_Y";
        private const string RefEndY = "End_Y";

        public sealed class Result
        {
            public Autodesk.Revit.DB.Dimension? DimensionX;
            public Autodesk.Revit.DB.Dimension? DimensionY;
        }

        /// <summary>
        /// Creates the X and Y dimension strings for a placed axis instance.
        /// offsetXInternal pushes the horizontal (X) string above the room's top edge;
        /// offsetYInternal pushes the vertical (Y) string left of the room's left edge --
        /// both in internal (feet) units. Convert from millimetres with
        /// UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters) before calling.
        ///
        /// Throws InvalidOperationException if the instance is missing any of the six
        /// named references -- this only runs when dimensioning was explicitly requested,
        /// so a silent no-op would hide a real family-authoring problem rather than
        /// surfacing it to whoever's placing axes.
        /// </summary>
        public static Result CreateAxisDimensions(
            Document doc,
            View view,
            FamilyInstance axisInstance,
            XYZ roomMin,
            XYZ roomMax,
            double offsetXInternal,
            double offsetYInternal)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (axisInstance == null) throw new ArgumentNullException(nameof(axisInstance));

            var result = new Result();
            var z = roomMin.Z;

            // ---- Horizontal (X) string, drawn above the room's top edge ----
            var refStartX = GetRequiredReference(axisInstance, RefStartX);
            var refMidX = GetRequiredReference(axisInstance, RefMidX);
            var refEndX = GetRequiredReference(axisInstance, RefEndX);

            var yLine = roomMax.Y + offsetXInternal;
            var lineX = Line.CreateBound(
                new XYZ(roomMin.X, yLine, z),
                new XYZ(roomMax.X, yLine, z));

            var refArrayX = new ReferenceArray();
            refArrayX.Append(refStartX);
            refArrayX.Append(refMidX);
            refArrayX.Append(refEndX);

            result.DimensionX = new CreateDimension().CreateNewDimension(doc, lineX, refArrayX);

            // ---- Vertical (Y) string, drawn left of the room's left edge ----
            var refStartY = GetRequiredReference(axisInstance, RefStartY);
            var refMidY = GetRequiredReference(axisInstance, RefMidY);
            var refEndY = GetRequiredReference(axisInstance, RefEndY);

            var xLine = roomMin.X - offsetYInternal;
            var lineY = Line.CreateBound(
                new XYZ(xLine, roomMin.Y, z),
                new XYZ(xLine, roomMax.Y, z));

            var refArrayY = new ReferenceArray();
            refArrayY.Append(refStartY);
            refArrayY.Append(refMidY);
            refArrayY.Append(refEndY);

            result.DimensionY = new CreateDimension().CreateNewDimension(doc, lineY, refArrayY);

            return result;
        }

        private static Reference GetRequiredReference(FamilyInstance instance, string name)
        {
            var reference = instance.GetReferenceByName(name);
            if (reference == null)
                throw new InvalidOperationException(
                    $"Family instance '{instance.Symbol?.FamilyName}' (id {instance.Id}) is missing the " +
                    $"named reference '{name}'. BA_Axis.rfa must have Start_X/Mid_X/End_X and " +
                    "Start_Y/Mid_Y/End_Y reference planes with Is Reference = Weak Reference for " +
                    "dimensioning to work -- check the family was reloaded after adding them.");
            return reference;
        }
    }
}

using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace BA.UI.Core.Finishes
{
    public sealed class ApplyFinishesOptions
    {
        public IReadOnlyList<ElementId> RoomIds { get; }

        public bool ApplyWalls { get; }
        public bool ApplyFloors { get; }
        public bool ApplyCeilings { get; }

        public ElementId WallTypeId { get; }
        public ElementId FloorTypeId { get; }
        public ElementId CeilingTypeId { get; }

        public bool UseTopOffset { get; }
        public double TopOffsetFt { get; }
        public double BaseOffsetFt { get; }

        public ApplyFinishesOptions(
            IReadOnlyList<ElementId> roomIds,
            bool applyWalls,
            bool applyFloors,
            bool applyCeilings,
            ElementId wallTypeId,
            ElementId floorTypeId,
            ElementId ceilingTypeId,
            bool useTopOffset,
            double topOffsetFt,
            double baseOffsetFt)
        {
            RoomIds = roomIds ?? throw new ArgumentNullException(nameof(roomIds));
            ApplyWalls = applyWalls;
            ApplyFloors = applyFloors;
            ApplyCeilings = applyCeilings;
            WallTypeId = wallTypeId ?? ElementId.InvalidElementId;
            FloorTypeId = floorTypeId ?? ElementId.InvalidElementId;
            CeilingTypeId = ceilingTypeId ?? ElementId.InvalidElementId;
            UseTopOffset = useTopOffset;
            TopOffsetFt = topOffsetFt;
            BaseOffsetFt = baseOffsetFt;
        }
    }
}
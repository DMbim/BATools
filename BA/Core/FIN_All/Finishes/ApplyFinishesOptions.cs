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

        /// <summary>
        /// Used when UseRoomDefinedFinishTypes is false. Ignored (may be InvalidElementId)
        /// when UseRoomDefinedFinishTypes is true.
        /// </summary>
        public ElementId WallTypeId { get; }
        public ElementId FloorTypeId { get; }
        public ElementId CeilingTypeId { get; }

        /// <summary>
        /// If true, the finish type for each room/category is resolved per-room from the
        /// room's BA.Tls_RoomFinish_Wall / BA.Tls_RoomFinish_Floor / BA.Tls_RoomFinish_Ceiling
        /// parameter (a Type Name string), instead of using the fixed WallTypeId/FloorTypeId/
        /// CeilingTypeId above. If a room has no value, or the value doesn't match any loaded
        /// type name, that category is skipped for that room (reported, not a fallback to the
        /// fixed type).
        /// </summary>
        public bool UseRoomDefinedFinishTypes { get; }

        public bool UseTopOffset { get; }
        public double TopOffsetFt { get; }
        public double BaseOffsetFt { get; }

        /// <summary>
        /// If true, ceiling height above level = room unbounded height minus CeilingTopOffsetFt
        /// (mirrors the wall top-offset behavior). If false, ceiling height above level is the
        /// absolute CeilingHeightAboveLevelFt value. Independent of BaseOffsetFt, which is a
        /// wall-only setting.
        /// </summary>
        public bool CeilingUseRoomHeightOffset { get; }
        public double CeilingTopOffsetFt { get; }
        public double CeilingHeightAboveLevelFt { get; }

        public ApplyFinishesOptions(
            IReadOnlyList<ElementId> roomIds,
            bool applyWalls,
            bool applyFloors,
            bool applyCeilings,
            ElementId wallTypeId,
            ElementId floorTypeId,
            ElementId ceilingTypeId,
            bool useRoomDefinedFinishTypes,
            bool useTopOffset,
            double topOffsetFt,
            double baseOffsetFt,
            bool ceilingUseRoomHeightOffset,
            double ceilingTopOffsetFt,
            double ceilingHeightAboveLevelFt)
        {
            RoomIds = roomIds ?? throw new ArgumentNullException(nameof(roomIds));
            ApplyWalls = applyWalls;
            ApplyFloors = applyFloors;
            ApplyCeilings = applyCeilings;
            WallTypeId = wallTypeId ?? ElementId.InvalidElementId;
            FloorTypeId = floorTypeId ?? ElementId.InvalidElementId;
            CeilingTypeId = ceilingTypeId ?? ElementId.InvalidElementId;
            UseRoomDefinedFinishTypes = useRoomDefinedFinishTypes;
            UseTopOffset = useTopOffset;
            TopOffsetFt = topOffsetFt;
            BaseOffsetFt = baseOffsetFt;
            CeilingUseRoomHeightOffset = ceilingUseRoomHeightOffset;
            CeilingTopOffsetFt = ceilingTopOffsetFt;
            CeilingHeightAboveLevelFt = ceilingHeightAboveLevelFt;
        }
    }
}
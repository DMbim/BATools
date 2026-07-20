using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace BA.Core.Rooms
{
    public static class AxisToRoomService
    {
        /// <summary>
        /// Vertical probe offset above a level's elevation, used only for the
        /// linked-room geometric fallback where RoomTag.Room cannot resolve directly.
        /// Room.IsPointInRoom is a full 3D containment test (Level elevation + offsets),
        /// so probing with the tag's own Location.Z is unreliable whenever the room's
        /// level is not at elevation 0. Always probe at LevelElevation + this offset.
        /// </summary>
        private const double LevelZProbeOffsetFeet = 1.0;

        /// <summary>
        /// Given selected RoomTag references (tags placed in host, tagging rooms in link),
        /// resolve linked rooms geometrically. RoomTag.Room is null for tags into a link,
        /// so this is the only viable resolution path here.
        /// </summary>
        public static List<Room> GetLinkedRoomsFromHostRoomTags(
            Document hostDoc,
            IList<Reference> hostRoomTagRefs,
            RevitLinkInstance linkInstance)
        {
            var linkedDoc = linkInstance.GetLinkDocument();
            if (linkedDoc == null) return new List<Room>();

            var inv = linkInstance.GetTransform().Inverse;

            var linkedRooms = new FilteredElementCollector(linkedDoc)
                .OfClass(typeof(SpatialElement))
                .OfType<Room>()
                .Where(r => r.Area > 0)
                .ToList();

            var roomsByLevel = linkedRooms
                .Where(r => r.LevelId != ElementId.InvalidElementId)
                .GroupBy(r => r.LevelId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var levelElevations = new Dictionary<ElementId, double>();
            foreach (var levelId in roomsByLevel.Keys)
            {
                if (linkedDoc.GetElement(levelId) is Level lvl)
                {
                    levelElevations[levelId] = lvl.Elevation;
                }
            }

            var result = new List<Room>();

            foreach (var r in hostRoomTagRefs)
            {
                if (hostDoc.GetElement(r) is not RoomTag tag) continue;
                if (tag.Location is not LocationPoint lp || lp.Point == null) continue;

                var ptLinkedXY = inv.OfPoint(lp.Point);

                Room match = null;
                foreach (var kvp in roomsByLevel)
                {
                    if (!levelElevations.TryGetValue(kvp.Key, out var elevation)) continue;

                    var probe = new XYZ(ptLinkedXY.X, ptLinkedXY.Y, elevation + LevelZProbeOffsetFeet);
                    match = kvp.Value.FirstOrDefault(x => x.IsPointInRoom(probe));
                    if (match != null) break;
                }

                if (match != null) result.Add(match);
            }

            return result
                .GroupBy(x => x.Id.Value)
                .Select(g => g.First())
                .ToList();
        }

        /// <summary>
        /// Local mode: host tags -> host rooms.
        /// Uses RoomTag.Room directly, which is the authoritative, non-geometric
        /// resolution for tags that tag a room in the same document. This avoids
        /// the Z-elevation containment bug entirely and eliminates the need to
        /// collect or scan every Room in the document.
        /// </summary>
        public static List<Room> GetLocalRoomsFromRoomTags(Document doc, IList<Reference> hostRoomTagRefs)
        {
            var result = new List<Room>();

            foreach (var r in hostRoomTagRefs)
            {
                if (doc.GetElement(r) is not RoomTag tag) continue;

                // RoomTag.Room is null only when the tag targets a room inside a link.
                // For local tags this is direct and reliable, no geometry involved.
                if (tag.Room is Room room)
                {
                    result.Add(room);
                }
            }

            return result
                .GroupBy(x => x.Id.Value)
                .Select(g => g.First())
                .ToList();
        }
    }
}
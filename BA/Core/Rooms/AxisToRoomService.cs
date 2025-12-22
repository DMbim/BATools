using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace BA.Core.Rooms
{
    public static class AxisToRoomService
    {
        /// <summary>
        /// Given selected RoomTag references (tags placed in host, tagging rooms in link),
        /// resolve linked rooms by tag point.
        /// </summary>
        public static List<Room> GetLinkedRoomsFromHostRoomTags(
            Document hostDoc,
            IList<Reference> hostRoomTagRefs,
            RevitLinkInstance linkInstance)
        {
            var linkedDoc = linkInstance.GetLinkDocument();
            if (linkedDoc == null) return new List<Room>();

            var inv = linkInstance.GetTransform().Inverse;

            // Collect linked rooms once
            var linkedRooms = new FilteredElementCollector(linkedDoc)
                .OfClass(typeof(SpatialElement))
                .OfType<Room>()
                .ToList();

            var result = new List<Room>();

            foreach (var r in hostRoomTagRefs)
            {
                if (hostDoc.GetElement(r) is not RoomTag tag) continue;
                if (tag.Location is not LocationPoint lp || lp.Point == null) continue;

                // Host point -> linked coordinates
                var ptLinked = inv.OfPoint(lp.Point);

                var room = linkedRooms.FirstOrDefault(x => x.IsPointInRoom(ptLinked));
                if (room != null) result.Add(room);
            }

            return result
            .GroupBy(x => x.Id.Value)   // use .Value (works in newer Revit); if your Revit has IntegerValue, swap it.
            .Select(g => g.First())
            .ToList();
        }

        /// <summary>Local mode: host tags -> host rooms</summary>
        public static List<Room> GetLocalRoomsFromRoomTags(Document doc, IList<Reference> hostRoomTagRefs)
        {
            var rooms = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .OfType<Room>()
                .ToList();

            var result = new List<Room>();

            foreach (var r in hostRoomTagRefs)
            {
                if (doc.GetElement(r) is not RoomTag tag) continue;
                if (tag.Location is not LocationPoint lp || lp.Point == null) continue;

                var room = rooms.FirstOrDefault(x => x.IsPointInRoom(lp.Point));
                if (room != null) result.Add(room);
            }

            return result
            .GroupBy(x => x.Id.Value)   // use .Value (works in newer Revit); if your Revit has IntegerValue, swap it.
            .Select(g => g.First())
            .ToList();
        }

        private sealed class ElementIdComparer : IEqualityComparer<Element>
        {
            public bool Equals(Element? x, Element? y) => x?.Id == y?.Id;
            public int GetHashCode(Element obj) => obj.Id.GetHashCode();
        }
    }
}

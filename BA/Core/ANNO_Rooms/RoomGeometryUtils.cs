using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace BA.Core.Rooms
{
    public static class RoomGeometryUtils
    {
        public static XYZ? GetRoomCenter(Room room)
        {
            if (room?.Location is LocationPoint lp && lp.Point != null)
                return lp.Point;

            var bb = room?.get_BoundingBox(null);
            if (bb == null) return null;

            return (bb.Min + bb.Max) * 0.5;
        }
    }
}

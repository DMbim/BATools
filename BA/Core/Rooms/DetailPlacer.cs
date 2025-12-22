using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace BA.Core.Rooms
{
    public static class DetailPlacer
    {
        public static FamilyInstance? PlaceInLinkedRoomCenter(
            Document hostDoc,
            View view,
            FamilySymbol symbol,
            Room linkedRoom,
            RevitLinkInstance linkInstance)
        {
            var centerLinked = RoomGeometryUtils.GetRoomCenter(linkedRoom);
            if (centerLinked == null) return null;

            var ptHost = linkInstance.GetTransform().OfPoint(centerLinked);

            if (!symbol.IsActive)
            {
                symbol.Activate();
                hostDoc.Regenerate();
            }

            // Detail component: view-specific
            return hostDoc.Create.NewFamilyInstance(ptHost, symbol, view);
        }

        public static FamilyInstance? PlaceInLocalRoomCenter(
            Document doc,
            View view,
            FamilySymbol symbol,
            Room room)
        {
            var center = RoomGeometryUtils.GetRoomCenter(room);
            if (center == null) return null;

            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            return doc.Create.NewFamilyInstance(center, symbol, view);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace BA.Core.Rooms
{
    public static class ElementToRoomService
    {
        public sealed class ResultStats
        {
            public int ElementsConsidered;
            public int ElementsWritten;
            public int ElementsNoPoint;
            public int ElementsNoRoom;
            public int ElementsNoParams;
        }

        public static ResultStats AssignFromLinkedRooms(
            Document hostDoc,
            RevitLinkInstance linkInstance,
            Category category,
            string sourceParamName,
            string destParamName)
        {
            var stats = new ResultStats();

            var linkedDoc = linkInstance.GetLinkDocument();
            if (linkedDoc == null) return stats;

            var rooms = new FilteredElementCollector(linkedDoc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .ToList();

            var inv = linkInstance.GetTransform().Inverse;

            var elems = new FilteredElementCollector(hostDoc)
                .OfCategoryId(category.Id)
                .WhereElementIsNotElementType()
                .ToList();

            foreach (var e in elems)
            {
                stats.ElementsConsidered++;

                var ptHost = ElementLocationUtils.GetBestPoint(e, hostDoc.ActiveView);
                if (ptHost == null) { stats.ElementsNoPoint++; continue; }

                var ptLinked = inv.OfPoint(ptHost);

                var room = rooms.FirstOrDefault(r => r.IsPointInRoom(ptLinked));
                if (room == null) { stats.ElementsNoRoom++; continue; }

                var pSrc = room.LookupParameter(sourceParamName);
                var pDst = e.LookupParameter(destParamName);

                if (pSrc == null || pDst == null || pDst.IsReadOnly)
                {
                    stats.ElementsNoParams++;
                    continue;
                }

                var srcVal = pSrc.AsString();
                if (string.IsNullOrWhiteSpace(srcVal)) { stats.ElementsNoParams++; continue; }

                if (pDst.StorageType == StorageType.String)
                    pDst.Set(srcVal);
                else
                    pDst.Set(srcVal); // tolerant

                stats.ElementsWritten++;
            }

            return stats;
        }

        public static ResultStats AssignFromLocalRooms(
            Document doc,
            Category category,
            string sourceParamName,
            string destParamName)
        {
            var stats = new ResultStats();

            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .ToList();

            var elems = new FilteredElementCollector(doc)
                .OfCategoryId(category.Id)
                .WhereElementIsNotElementType()
                .ToList();

            foreach (var e in elems)
            {
                stats.ElementsConsidered++;

                var pt = ElementLocationUtils.GetBestPoint(e, doc.ActiveView);
                if (pt == null) { stats.ElementsNoPoint++; continue; }

                var room = rooms.FirstOrDefault(r => r.IsPointInRoom(pt));
                if (room == null) { stats.ElementsNoRoom++; continue; }

                var pSrc = room.LookupParameter(sourceParamName);
                var pDst = e.LookupParameter(destParamName);

                if (pSrc == null || pDst == null || pDst.IsReadOnly)
                {
                    stats.ElementsNoParams++;
                    continue;
                }

                var srcVal = pSrc.AsString();
                if (string.IsNullOrWhiteSpace(srcVal)) { stats.ElementsNoParams++; continue; }

                if (pDst.StorageType == StorageType.String)
                    pDst.Set(srcVal);
                else
                    pDst.Set(srcVal);

                stats.ElementsWritten++;
            }

            return stats;
        }
    }
}

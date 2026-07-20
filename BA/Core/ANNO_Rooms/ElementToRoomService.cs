using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace BA.Core.Rooms
{
    public static class ElementToRoomService
    {
        // Fallback candidates tried after the configured parameter name fails to
        // resolve (not found, or found but read-only for the destination side).
        // Source and destination fallbacks are independent of each other.
        //
        // Source fallback uses BuiltInParameter.ROOM_NUMBER directly rather than
        // matching the display string "Number" -- this is locale-independent and
        // survives a UI language change or a project renaming the parameter's
        // display label, neither of which a LookupParameter(name) match would.
        //
        // Destination fallback: "R.N." is assumed to be a custom/shared parameter
        // (no built-in equivalent), so it's matched by name. "Comments" is a
        // built-in (BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS) so it's resolved
        // the same locale-independent way as the source fallback. Note per project
        // history: this built-in does not exist on OST_Lines elements -- for that
        // category get_Parameter returns null here, which the existing null-check
        // handles correctly (falls through to ElementsNoParams).
        private static readonly string[] DestinationNameFallbacks = { "R.N." };

        public sealed class ResultStats
        {
            public int ElementsConsidered;
            public int ElementsWritten;
            public int ElementsNoPoint;
            public int ElementsNoRoom;
            public int ElementsNoParams;

            // How many of the ElementsWritten above went through a fallback name
            // rather than the configured Source/Destination parameter. Nonzero
            // values here are worth a look -- they mean the configured parameter
            // name isn't matching for at least some elements/rooms.
            public int ElementsWrittenViaSourceFallback;
            public int ElementsWrittenViaDestinationFallback;
        }

        private static Parameter? ResolveSourceParameter(Room room, string primaryName, out bool usedFallback)
        {
            usedFallback = false;

            var p = room.LookupParameter(primaryName);
            if (p != null)
                return p;

            p = room.get_Parameter(BuiltInParameter.ROOM_NUMBER);
            if (p != null)
            {
                usedFallback = true;
                return p;
            }

            return null;
        }

        private static Parameter? ResolveDestinationParameter(Element e, string primaryName, out bool usedFallback)
        {
            usedFallback = false;

            var p = e.LookupParameter(primaryName);
            if (p != null && !p.IsReadOnly)
                return p;

            foreach (var fallbackName in DestinationNameFallbacks)
            {
                p = e.LookupParameter(fallbackName);
                if (p != null && !p.IsReadOnly)
                {
                    usedFallback = true;
                    return p;
                }
            }

            p = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (p != null && !p.IsReadOnly)
            {
                usedFallback = true;
                return p;
            }

            return null;
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

                var pSrc = ResolveSourceParameter(room, sourceParamName, out bool srcFallback);
                var pDst = ResolveDestinationParameter(e, destParamName, out bool dstFallback);

                if (pSrc == null || pDst == null)
                {
                    stats.ElementsNoParams++;
                    continue;
                }

                var srcVal = pSrc.AsString();
                if (string.IsNullOrWhiteSpace(srcVal)) { stats.ElementsNoParams++; continue; }

                pDst.Set(srcVal);

                stats.ElementsWritten++;
                if (srcFallback) stats.ElementsWrittenViaSourceFallback++;
                if (dstFallback) stats.ElementsWrittenViaDestinationFallback++;
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

                var pSrc = ResolveSourceParameter(room, sourceParamName, out bool srcFallback);
                var pDst = ResolveDestinationParameter(e, destParamName, out bool dstFallback);

                if (pSrc == null || pDst == null)
                {
                    stats.ElementsNoParams++;
                    continue;
                }

                var srcVal = pSrc.AsString();
                if (string.IsNullOrWhiteSpace(srcVal)) { stats.ElementsNoParams++; continue; }

                pDst.Set(srcVal);

                stats.ElementsWritten++;
                if (srcFallback) stats.ElementsWrittenViaSourceFallback++;
                if (dstFallback) stats.ElementsWrittenViaDestinationFallback++;
            }

            return stats;
        }
    }
}

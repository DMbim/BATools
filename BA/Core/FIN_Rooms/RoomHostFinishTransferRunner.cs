using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using BA.Core.Rooms;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Commands.Rooms
{
    public class RoomHostFinishTransferRunner
    {
        // Max ray distance: ~50m (in feet)
        private static readonly double MaxRayDistFt = UnitUtils.ConvertToInternalUnits(50000, UnitTypeId.Millimeters);

        // Sample offset in XY: ~200mm
        private static readonly double SampleInsetFt = UnitUtils.ConvertToInternalUnits(200, UnitTypeId.Millimeters);

        /// <summary>
        /// Runs the transfer against an explicit set of room ids, resolved by the caller
        /// (the window's room picker list). No longer reads UIDocument.Selection.
        /// </summary>
        public RoomHostFinishTransferResult Run(Document doc, RoomHostFinishTransferSettings settings, IReadOnlyCollection<ElementId> roomIds)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var mappings = (settings.Mappings ?? new List<RoomHostParamMapping>())
                .Where(m => !string.IsNullOrWhiteSpace(m.SourceCategory)
                         && !string.IsNullOrWhiteSpace(m.SourceParameterName)
                         && !string.IsNullOrWhiteSpace(m.TargetRoomParameterName))
                .ToList();

            if (mappings.Count == 0)
                throw new InvalidOperationException("No valid mappings defined.");

            var rooms = GetRooms(doc, roomIds);

            if (rooms.Count == 0)
                throw new InvalidOperationException("No rooms to process. Check at least one room in the list and try again.");

            var stats = new TransferDebugStats();

            // Ensure we have a 3D view for raycasting. Creating requires transaction.
            View3D view3d = null;

            using (var tg = new TransactionGroup(doc, "Room Host Finish Transfer"))
            {
                tg.Start();

                // Create 3D view if needed
                using (var t0 = new Transaction(doc, "Prepare raycast view"))
                {
                    t0.Start();
                    view3d = RaycastUtil.GetOrCreate3DView(doc, createIfMissing: true);
                    view3d.IsSectionBoxActive = false;
                    t0.Commit();

                }

                using (var t = new Transaction(doc, "Write room parameters"))
                {
                    t.Start();

                    foreach (var room in rooms)
                    {
                        stats.RoomsSeen++;

                        if (room == null || room.Area <= 0)
                        {
                            stats.Skipped_NoRoomSolidOrBBox++;
                            continue;
                        }

                        var bb = room.get_BoundingBox(null);
                        if (bb == null)
                        {
                            stats.Skipped_NoRoomSolidOrBBox++;
                            continue;
                        }

                        // Sample points inside room (fast + robust)
                        var samplePts = RaycastUtil.GetSamplePointsInRoom(room, maxPoints: 9, xyInsetFeet: SampleInsetFt);
                        if (samplePts.Count == 0)
                        {
                            stats.Skipped_NoSamplePoint++;
                            continue;
                        }

                        // Raycast at multiple points; choose nearest hit by majority vote then proximity
                        var bestFloor = FindBestHit(doc, view3d, samplePts, BuiltInCategory.OST_Floors, XYZ.BasisZ.Negate(), MaxRayDistFt);
                        var bestCeiling = FindBestHit(doc, view3d, samplePts, BuiltInCategory.OST_Ceilings, XYZ.BasisZ, MaxRayDistFt);

                        // Apply mappings (Ceiling/Floor)
                        bool wroteSomethingInThisRoom = false;

                        foreach (var map in mappings)
                        {
                            var cat = map.SourceCategory.Trim().ToLowerInvariant();
                            Element src = null;

                            if (cat == "floor") src = bestFloor;
                            else if (cat == "ceiling") src = bestCeiling;
                            else
                            {
                                stats.Skipped_InvalidMapping++;
                                continue;
                            }

                            if (src == null)
                            {
                                if (cat == "floor") stats.Skipped_NoFloorHit++;
                                else stats.Skipped_NoCeilingHit++;
                                continue;
                            }

                            // READ SOURCE (INSTANCE then TYPE fallback) — critical for your case
                            var srcVal = ParameterUtil.ReadAsString(doc, src, map.SourceParameterName, allowTypeFallback: true);
                            if (string.IsNullOrWhiteSpace(srcVal))
                            {
                                stats.Skipped_SourceEmpty++;
                                continue;
                            }

                            // WRITE TARGET (supports builtin fallback for "Ceiling Finish"/"Floor Finish")
                            bool ok = ParameterUtil.WriteToRoom(room, map.TargetRoomParameterName, srcVal, map.WriteOnlyIfEmpty);
                            if (ok)
                            {
                                stats.Written++;
                                wroteSomethingInThisRoom = true;
                            }
                            else
                            {
                                stats.Skipped_TargetFail++;
                            }
                        }

                        if (wroteSomethingInThisRoom)
                            stats.RoomsProcessed++;
                    }

                    t.Commit();
                }

                tg.Assimilate();
            }

            // Show debug summary (this is the workflow: always diagnose by bucket)
            TaskDialog.Show("BA",
                    $@"Rooms seen: {stats.RoomsSeen}
                    Rooms written (at least 1 write): {stats.RoomsProcessed}
                    Values written: {stats.Written}

                    Skipped buckets:
                    - No room bbox: {stats.Skipped_NoRoomSolidOrBBox}
                    - No sample point inside room: {stats.Skipped_NoSamplePoint}
                    - No floor hit: {stats.Skipped_NoFloorHit}
                    - No ceiling hit: {stats.Skipped_NoCeilingHit}
                    - Source param empty/missing: {stats.Skipped_SourceEmpty}
                    - Target write failed: {stats.Skipped_TargetFail}
                    - Invalid mapping: {stats.Skipped_InvalidMapping}"
                    );

            return new RoomHostFinishTransferResult
            {
                RoomsProcessed = stats.RoomsProcessed,
                ValuesWritten = stats.Written,
                Skipped = stats.Skipped_NoRoomSolidOrBBox
                        + stats.Skipped_NoSamplePoint
                        + stats.Skipped_NoFloorHit
                        + stats.Skipped_NoCeilingHit
                        + stats.Skipped_SourceEmpty
                        + stats.Skipped_TargetFail
                        + stats.Skipped_InvalidMapping
            };
        }

        private static Element FindBestHit(Document doc, View3D view3d, List<XYZ> samplePts, BuiltInCategory bic, XYZ dir, double maxDistFt)
        {
            // Collect hits (elementId + proximity) for each sample point
            var hits = new List<(Element e, double d)>();

            foreach (var p in samplePts)
            {
                var e = RaycastUtil.FindNearestByCategory(doc, view3d, p, dir, bic, maxDistFt);
                if (e == null) continue;

                // Get proximity again (ReferenceIntersector API only returns it inside util, so approximate by vertical distance)
                // For up/down rays, vertical diff is adequate.
                double d = Math.Abs(e.get_BoundingBox(null)?.Min.Z - p.Z ?? 0);
                hits.Add((e, d));
            }

            if (hits.Count == 0) return null;

            // Majority vote by element id
            var bestGroup = hits
                .GroupBy(x => x.e.Id.Value)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Min(x => x.d))
                .FirstOrDefault();

            return bestGroup?.First().e;
        }

        /// <summary>
        /// Resolves the explicit id list (from the window's room picker) to live Room elements.
        /// No Selection access, no RoomTag resolution: the window is the single source of scope.
        /// </summary>
        private static List<Room> GetRooms(Document doc, IReadOnlyCollection<ElementId> roomIds)
        {
            if (roomIds == null || roomIds.Count == 0)
                return new List<Room>();

            var result = new List<Room>();

            foreach (var id in roomIds)
            {
                if (doc.GetElement(id) is Room r && r.Area > 0)
                    result.Add(r);
            }

            return result
                .GroupBy(x => x.Id.Value)
                .Select(g => g.First())
                .ToList();
        }
    }
}
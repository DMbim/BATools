using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Floor = Autodesk.Revit.DB.Floor;
using Level = Autodesk.Revit.DB.Level;

namespace BA.UI.Core.Finishes
{
    public sealed class FinishesReport
    {
        public int RoomsProcessed { get; set; }
        public int RoomsSkipped { get; set; }

        public int WallsCreated { get; set; }
        public int FloorsCreated { get; set; }
        public int CeilingsCreated { get; set; }

        public List<string> SkippedReasons { get; } = new();

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Rooms processed: {RoomsProcessed}");
            sb.AppendLine($"Rooms skipped: {RoomsSkipped}");
            sb.AppendLine($"Finish walls created: {WallsCreated}");
            sb.AppendLine($"Finish floors created: {FloorsCreated}");
            sb.AppendLine($"Finish ceilings created: {CeilingsCreated}");

            if (SkippedReasons.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Skipped / warnings:");
                foreach (var s in SkippedReasons.Distinct()) sb.AppendLine($"- {s}");
            }

            return sb.ToString();
        }
    }

    public static class FinishesByRoomService
    {
        // -----------------------------
        // Public entry
        // -----------------------------
        public static FinishesReport Execute(Document doc, ApplyFinishesOptions opt)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (opt == null) throw new ArgumentNullException(nameof(opt));

            var report = new FinishesReport();

            var rooms = opt.RoomIds
                .Select(id => doc.GetElement(id))
                .OfType<Room>()
                .ToList();

            if (rooms.Count == 0)
            {
                report.SkippedReasons.Add("No valid rooms found from selection.");
                report.RoomsSkipped = opt.RoomIds.Count;
                return report;
            }

            using (var tx = new Transaction(doc, "BA - Apply finishes by rooms"))
            {
                tx.Start();

                // Global de-dup of finish walls (avoid overlap between adjacent rooms)
                var createdWallSides = new HashSet<WallSideKey>();

                foreach (var room in rooms)
                {
                    if (!IsUsableRoom(room))
                    {
                        report.RoomsSkipped++;
                        report.SkippedReasons.Add($"Room skipped (not placed / not enclosed): {SafeRoomLabel(room)}");
                        continue;
                    }

                    var level = doc.GetElement(room.LevelId) as Level;
                    if (level == null)
                    {
                        report.RoomsSkipped++;
                        report.SkippedReasons.Add($"Room skipped (no level): {SafeRoomLabel(room)}");
                        continue;
                    }

                    // For floors/ceilings we need loops as curves
                    var boundaries = GetRoomBoundaryLoops(room);
                    if (boundaries.Count == 0)
                    {
                        report.RoomsSkipped++;
                        report.SkippedReasons.Add($"Room skipped (no boundary): {SafeRoomLabel(room)}");
                        continue;
                    }

                    double roomHeight = GetRoomUnboundedHeight(room);
                    if (roomHeight <= 1e-6)
                    {
                        report.RoomsSkipped++;
                        report.SkippedReasons.Add($"Room skipped (invalid height): {SafeRoomLabel(room)}");
                        continue;
                    }

                    if (opt.ApplyWalls)
                    {
                        double height = roomHeight;
                        if (opt.UseTopOffset)
                            height = Math.Max(0, roomHeight - opt.TopOffsetFt);

                        if (height <= 1e-6)
                        {
                            report.SkippedReasons.Add($"Wall finish height <= 0 after top offset for room: {SafeRoomLabel(room)}");
                        }
                        else
                        {
                            int created = CreateFinishWallsForRoom_HostDriven(
                                doc,
                                room,
                                level,
                                opt.WallTypeId,
                                height,
                                opt.BaseOffsetFt,
                                createdWallSides);

                            report.WallsCreated += created;
                        }
                    }

                    if (opt.ApplyFloors)
                    {
                        int created = CreateFinishFloorForRoom(doc, room, level, boundaries, opt.FloorTypeId);
                        report.FloorsCreated += created;
                    }

                    if (opt.ApplyCeilings)
                    {
                        int created = CreateFinishCeilingForRoom(doc, room, level, boundaries, opt.CeilingTypeId, opt.BaseOffsetFt);
                        report.CeilingsCreated += created;
                    }

                    report.RoomsProcessed++;
                }

                tx.Commit();
            }

            return report;
        }

        // -----------------------------
        // Host-driven walls (NO overlaps)
        // -----------------------------
        private enum WallSide
        {
            Exterior = 0,
            Interior = 1
        }

        private readonly struct WallSideKey : IEquatable<WallSideKey>
        {
            public readonly long WallId; // Revit 2026: ElementId.Value is long
            public readonly WallSide Side;

            public WallSideKey(long wallId, WallSide side)
            {
                WallId = wallId;
                Side = side;
            }

            public bool Equals(WallSideKey other) => WallId == other.WallId && Side == other.Side;
            public override bool Equals(object obj) => obj is WallSideKey k && Equals(k);
            public override int GetHashCode() => HashCode.Combine(WallId, (int)Side);
            public override string ToString() => $"{WallId}:{Side}";
        }

        private static int CreateFinishWallsForRoom_HostDriven(
            Document doc,
            Room room,
            Level level,
            ElementId finishWallTypeId,
            double height,
            double baseOffsetFt,
            HashSet<WallSideKey> createdWallSides)
        {
            int created = 0;

            var finishType = doc.GetElement(finishWallTypeId) as WallType;
            if (finishType == null) return 0;

            // We need boundary segments to read seg.ElementId (host element)
            var bopt = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };

            var loops = room.GetBoundarySegments(bopt);
            if (loops == null) return 0;

            XYZ roomPoint = GetRoomCentroid(room);

            foreach (var loop in loops)
            {
                foreach (var seg in loop)
                {
                    ElementId hostId = seg.ElementId;
                    if (hostId == ElementId.InvalidElementId) continue;

                    var hostWall = doc.GetElement(hostId) as Wall;
                    if (hostWall == null) continue; // only walls here

                    var hostLc = hostWall.Location as LocationCurve;
                    if (hostLc?.Curve == null) continue;

                    WallSide side = GetRoomSideOfWall(hostWall, roomPoint);

                    var key = new WallSideKey(hostWall.Id.Value, side);
                    if (createdWallSides.Contains(key))
                        continue;

                    double hostHalf = SafeWallWidth(hostWall) * 0.5;
                    double finishHalf = SafeWallTypeWidth(finishType) * 0.5;
                    double offset = hostHalf + finishHalf;

                    // hostWall.Orientation is exterior normal
                    XYZ exteriorNormal = hostWall.Orientation;
                    XYZ dir = (side == WallSide.Exterior) ? exteriorNormal : exteriorNormal.Negate();

                    Curve baseCurve = hostLc.Curve;
                    Curve finishCurve = baseCurve.CreateTransformed(Transform.CreateTranslation(dir.Multiply(offset)));

                    Wall w = Wall.Create(doc, finishCurve, finishType.Id, level.Id, height, baseOffsetFt, false, false);

                    StampElement(w, room);
                    created++;
                    createdWallSides.Add(key);
                }
            }

            return created;
        }

        private static WallSide GetRoomSideOfWall(Wall hostWall, XYZ roomPoint)
        {
            var lc = hostWall.Location as LocationCurve;
            Curve c = lc.Curve;
            XYZ mid = c.Evaluate(0.5, true);

            XYZ v = roomPoint - mid;
            v = new XYZ(v.X, v.Y, 0);

            XYZ n = hostWall.Orientation;
            n = new XYZ(n.X, n.Y, 0);

            double d = v.DotProduct(n);
            return d >= 0 ? WallSide.Exterior : WallSide.Interior;
        }

        private static double SafeWallWidth(Wall w)
        {
            try
            {
                var wt = w.WallType;
                return wt != null ? wt.Width : 0.0;
            }
            catch { return 0.0; }
        }

        private static double SafeWallTypeWidth(WallType wt)
        {
            try { return wt.Width; } catch { return 0.0; }
        }

        // -----------------------------
        // Floors / Ceilings from loops
        // -----------------------------
        private static int CreateFinishFloorForRoom(Document doc, Room room, Level level, List<List<Curve>> boundaryLoops, ElementId floorTypeId)
        {
            var floorType = doc.GetElement(floorTypeId) as FloorType;
            if (floorType == null) return 0;

            var curveLoops = new List<CurveLoop>();
            foreach (var loop in boundaryLoops)
            {
                var cl = new CurveLoop();
                foreach (var c in loop) cl.Append(c);
                if (cl.Count() >= 3) curveLoops.Add(cl);
            }

            if (curveLoops.Count == 0) return 0;

            Floor f = Floor.Create(doc, curveLoops, floorType.Id, level.Id);
            StampElement(f, room);
            return 1;
        }

        private static int CreateFinishCeilingForRoom(Document doc, Room room, Level level, List<List<Curve>> boundaryLoops, ElementId ceilingTypeId, double offsetFt)
        {
            var ceilingType = doc.GetElement(ceilingTypeId) as CeilingType;
            if (ceilingType == null) return 0;

            var curveLoops = new List<CurveLoop>();
            foreach (var loop in boundaryLoops)
            {
                var cl = new CurveLoop();
                foreach (var c in loop) cl.Append(c);
                if (cl.Count() >= 3) curveLoops.Add(cl);
            }

            if (curveLoops.Count == 0) return 0;

            Ceiling ceil = Ceiling.Create(doc, curveLoops, ceilingType.Id, level.Id);
            if (ceil == null) return 0;

            TrySetDouble(ceil, BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM, offsetFt);
            StampElement(ceil, room);

            return 1;
        }

        // -----------------------------
        // Room helpers
        // -----------------------------
        private static bool IsUsableRoom(Room r)
        {
            if (r == null) return false;
            if (r.Location == null) return false;
            if (r.Area <= 1e-6) return false;
            return true;
        }

        private static string SafeRoomLabel(Room r)
        {
            try { return $"{r.Number} - {r.Name}"; }
            catch { return r?.Id.Value.ToString() ?? "<null>"; }
        }

        private static double GetRoomUnboundedHeight(Room room)
        {
            try
            {
                double h = room.UnboundedHeight;
                if (h > 1e-6) return h;
            }
            catch { }

            var p = room.get_Parameter(BuiltInParameter.ROOM_HEIGHT);
            if (p != null && p.StorageType == StorageType.Double)
                return p.AsDouble();

            return 0;
        }

        private static List<List<Curve>> GetRoomBoundaryLoops(Room room)
        {
            var opt = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };

            var segLoops = room.GetBoundarySegments(opt);
            var loops = new List<List<Curve>>();

            if (segLoops == null) return loops;

            foreach (var segLoop in segLoops)
            {
                var curves = new List<Curve>();
                foreach (var seg in segLoop)
                {
                    var c = seg.GetCurve();
                    if (c == null) continue;
                    if (c.IsBound && c.Length > 1e-4) curves.Add(c);
                }

                if (curves.Count >= 3) loops.Add(curves);
            }

            return loops;
        }

        private static XYZ GetRoomCentroid(Room room)
        {
            if (room.Location is LocationPoint lp && lp.Point != null)
                return lp.Point;

            var bb = room.get_BoundingBox(null);
            if (bb != null)
                return (bb.Min + bb.Max) * 0.5;

            return XYZ.Zero;
        }

        // -----------------------------
        // Stamping + parameter writing
        // -----------------------------
        private static void StampElement(Element e, Room room)
        {
            if (e == null || room == null) return;

            TrySetString(e, BuiltInParameter.ALL_MODEL_MARK, room.Number ?? "");
            TrySetString(e, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, $"Finish by room: {room.Number} - {room.Name}");
        }

        private static bool TrySetString(Element e, BuiltInParameter bip, string val)
        {
            try
            {
                var p = e.get_Parameter(bip);
                if (p == null || p.IsReadOnly) return false;
                return p.Set(val ?? "");
            }
            catch { return false; }
        }

        private static bool TrySetDouble(Element e, BuiltInParameter bip, double val)
        {
            try
            {
                var p = e.get_Parameter(bip);
                if (p == null || p.IsReadOnly) return false;
                return p.Set(val);
            }
            catch { return false; }
        }
    }
}
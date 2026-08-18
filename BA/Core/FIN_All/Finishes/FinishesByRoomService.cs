using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using BA.Core.Parameters;
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
        // BA_ shared parameter wiring
        // -----------------------------
        // TODO verify exact path/extension against the actual network file.
        private const string SharedParamFilePath =
            @"S:\CAD\Autodesk Revit\BA_Resources\BA_Shared parameters\BA_SharedParametersWIP2";

        private const string SharedParamGroupName = "Spaces";
        private const string ParamRoomNumber = "BA_Room_Number";
        private const string ParamRoomName = "BA_Room_Name";

        private static readonly BuiltInCategory[] FinishStampCategories =
        {
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Ceilings
        };

        // -----------------------------
        // Room-defined finish type parameter names (existing Room instance parameters,
        // hold a Type Name string, e.g. "BA_GEN_150 2"). Not shared-parameter-bound by this
        // service, they're expected to already exist on rooms.
        // -----------------------------
        private const string ParamRoomWallFinishType = "BA.Tls_RoomFinish_Wall";
        private const string ParamRoomFloorFinishType = "BA.Tls_RoomFinish_Floor";
        private const string ParamRoomCeilingFinishType = "BA.Tls_RoomFinish_Ceiling";

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

            // Built once, only if room-defined type resolution is active. Case-insensitive
            // lookup by Type Name across all loaded types of each category.
            Dictionary<string, WallType> wallTypesByName = null;
            Dictionary<string, FloorType> floorTypesByName = null;
            Dictionary<string, CeilingType> ceilingTypesByName = null;

            if (opt.UseRoomDefinedFinishTypes)
            {
                wallTypesByName = BuildTypeNameLookup<WallType>(doc);
                floorTypesByName = BuildTypeNameLookup<FloorType>(doc);
                ceilingTypesByName = BuildTypeNameLookup<CeilingType>(doc);
            }

            using (var tx = new Transaction(doc, "BA - Apply finishes by rooms"))
            {
                tx.Start();

                // Bind BA_Room_Number / BA_Room_Name to Walls, Floors, Ceilings up front,
                // regardless of which of those are being created this run. No-op if already
                // correctly bound. Must run inside this transaction.
                EnsureBaSharedParamsBound(doc);

                // Global de-dup of finish walls, keyed on host wall + side + the actual
                // segment span (not just host wall id), see MakeWallSideKey below.
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
                        ElementId wallTypeId = opt.UseRoomDefinedFinishTypes
                            ? ResolveRoomDefinedTypeId(room, ParamRoomWallFinishType, wallTypesByName, "Wall", report)
                            : opt.WallTypeId;

                        if (wallTypeId != ElementId.InvalidElementId)
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
                                    wallTypeId,
                                    height,
                                    opt.BaseOffsetFt,
                                    createdWallSides);

                                report.WallsCreated += created;
                            }
                        }
                    }

                    if (opt.ApplyFloors)
                    {
                        ElementId floorTypeId = opt.UseRoomDefinedFinishTypes
                            ? ResolveRoomDefinedTypeId(room, ParamRoomFloorFinishType, floorTypesByName, "Floor", report)
                            : opt.FloorTypeId;

                        if (floorTypeId != ElementId.InvalidElementId)
                        {
                            int created = CreateFinishFloorForRoom(doc, room, level, boundaries, floorTypeId);
                            report.FloorsCreated += created;
                        }
                    }

                    if (opt.ApplyCeilings)
                    {
                        ElementId ceilingTypeId = opt.UseRoomDefinedFinishTypes
                            ? ResolveRoomDefinedTypeId(room, ParamRoomCeilingFinishType, ceilingTypesByName, "Ceiling", report)
                            : opt.CeilingTypeId;

                        if (ceilingTypeId != ElementId.InvalidElementId)
                        {
                            int created = CreateFinishCeilingForRoom(doc, room, level, boundaries, ceilingTypeId, roomHeight, opt);
                            report.CeilingsCreated += created;
                        }
                    }

                    report.RoomsProcessed++;
                }

                tx.Commit();
            }

            return report;
        }

        // -----------------------------
        // Room-defined finish type resolution
        // -----------------------------
        private static Dictionary<string, T> BuildTypeNameLookup<T>(Document doc) where T : ElementType
        {
            var dict = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);

            foreach (var t in new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<T>())
            {
                var name = t.Name;
                if (string.IsNullOrWhiteSpace(name)) continue;

                // First match wins if duplicate names exist across different families,
                // ambiguity here isn't something this tool can resolve automatically.
                if (!dict.ContainsKey(name))
                    dict[name] = t;
            }

            return dict;
        }

        private static ElementId ResolveRoomDefinedTypeId<T>(
            Room room,
            string paramName,
            Dictionary<string, T> typesByName,
            string categoryLabel,
            FinishesReport report) where T : ElementType
        {
            string typeName = ReadRoomParamString(room, paramName);

            if (string.IsNullOrWhiteSpace(typeName))
            {
                report.SkippedReasons.Add(
                    $"{categoryLabel} finish skipped (no '{paramName}' value on room): {SafeRoomLabel(room)}");
                return ElementId.InvalidElementId;
            }

            typeName = typeName.Trim();

            if (typesByName == null || !typesByName.TryGetValue(typeName, out var type))
            {
                report.SkippedReasons.Add(
                    $"{categoryLabel} finish skipped (type '{typeName}' not found in project): {SafeRoomLabel(room)}");
                return ElementId.InvalidElementId;
            }

            return type.Id;
        }

        private static string ReadRoomParamString(Room room, string paramName)
        {
            var p = room.LookupParameter(paramName);
            if (p == null) return "";

            try
            {
                if (p.StorageType == StorageType.String)
                    return p.AsString() ?? "";

                var vs = p.AsValueString();
                return vs ?? "";
            }
            catch
            {
                return "";
            }
        }

        // -----------------------------
        // Shared parameter binding
        // -----------------------------
        private static void EnsureBaSharedParamsBound(Document doc)
        {
            foreach (var cat in FinishStampCategories)
            {
                SharedParameterBindingService.EnsureBound(
                    doc, SharedParamFilePath, SharedParamGroupName, ParamRoomNumber, cat, instanceBinding: true);

                SharedParameterBindingService.EnsureBound(
                    doc, SharedParamFilePath, SharedParamGroupName, ParamRoomName, cat, instanceBinding: true);
            }
        }

        // -----------------------------
        // Host-driven walls (NO overlaps, correctly length-bounded)
        // -----------------------------
        private enum WallSide
        {
            Exterior = 0,
            Interior = 1
        }

        /// <summary>
        /// Dedup key for created finish wall segments. Keyed on host wall id, side (relative
        /// to the room), AND the segment's own (rounded, direction-normalized) endpoints, not
        /// just wall id + side. A single host wall can legitimately produce multiple, separate
        /// finish wall segments: it may border more than one room, or a single room's boundary
        /// against it may be split by a door/opening. Keying on wall id + side alone would
        /// wrongly treat all of those as duplicates and skip everything after the first.
        /// </summary>
        private readonly struct WallSideKey : IEquatable<WallSideKey>
        {
            private const double Tol = 1.0 / 16.0 / 12.0; // 1/16" in feet

            public readonly long WallId;
            public readonly WallSide Side;
            public readonly long P0X, P0Y, P0Z, P1X, P1Y, P1Z;

            public WallSideKey(long wallId, WallSide side, XYZ p0, XYZ p1)
            {
                WallId = wallId;
                Side = side;

                long ax = Round(p0.X), ay = Round(p0.Y), az = Round(p0.Z);
                long bx = Round(p1.X), by = Round(p1.Y), bz = Round(p1.Z);

                // Normalize direction so the same physical segment hashes identically
                // regardless of which end the boundary loop walked first.
                if (Compare(ax, ay, az, bx, by, bz) > 0)
                {
                    (ax, bx) = (bx, ax);
                    (ay, by) = (by, ay);
                    (az, bz) = (bz, az);
                }

                P0X = ax; P0Y = ay; P0Z = az;
                P1X = bx; P1Y = by; P1Z = bz;
            }

            private static long Round(double v) => (long)Math.Round(v / Tol);

            private static int Compare(long ax, long ay, long az, long bx, long by, long bz)
            {
                if (ax != bx) return ax.CompareTo(bx);
                if (ay != by) return ay.CompareTo(by);
                return az.CompareTo(bz);
            }

            public bool Equals(WallSideKey other) =>
                WallId == other.WallId && Side == other.Side &&
                P0X == other.P0X && P0Y == other.P0Y && P0Z == other.P0Z &&
                P1X == other.P1X && P1Y == other.P1Y && P1Z == other.P1Z;

            public override bool Equals(object obj) => obj is WallSideKey k && Equals(k);

            public override int GetHashCode() =>
                HashCode.Combine(HashCode.Combine(WallId, (int)Side, P0X, P0Y, P0Z), P1X, P1Y, P1Z);

            public override string ToString() =>
                $"{WallId}:{Side}:({P0X},{P0Y},{P0Z})-({P1X},{P1Y},{P1Z})";
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

            // We need boundary segments to read seg.ElementId (host element) and seg.GetCurve()
            // (the actual room-bounded span of that host element).
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

                    // This is the fix: seg.GetCurve() is the actual portion of the host wall's
                    // Finish-location face that borders THIS room. Previously the code used
                    // hostLc.Curve (the host wall's full location curve), so any host wall
                    // running past the room into unrelated space produced an oversized finish
                    // wall extending well beyond the room.
                    Curve segCurve = seg.GetCurve();
                    if (segCurve == null || !segCurve.IsBound || segCurve.Length <= 1e-4) continue;

                    WallSide side = GetRoomSideOfWall(hostWall, roomPoint);

                    var key = new WallSideKey(hostWall.Id.Value, side, segCurve.GetEndPoint(0), segCurve.GetEndPoint(1));
                    if (createdWallSides.Contains(key))
                        continue;

                    double finishHalf = SafeWallTypeWidth(finishType) * 0.5;

                    // segCurve (Finish boundary location) already sits on the host wall's
                    // room-facing face, i.e. offset from the host centerline toward the room
                    // by whatever the host wall's actual layer geometry puts there (not
                    // assumed to be exactly hostWidth/2, this works correctly even for
                    // asymmetric/compound host walls). We only need to push further inward
                    // by finishHalf, in the same direction, to center the new finish wall
                    // against that face.
                    XYZ exteriorNormal = hostWall.Orientation;
                    XYZ dir = (side == WallSide.Exterior) ? exteriorNormal : exteriorNormal.Negate();

                    Curve finishCurve = segCurve.CreateTransformed(Transform.CreateTranslation(dir.Multiply(finishHalf)));

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

        private static int CreateFinishCeilingForRoom(
            Document doc,
            Room room,
            Level level,
            List<List<Curve>> boundaryLoops,
            ElementId ceilingTypeId,
            double roomHeight,
            ApplyFinishesOptions opt)
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

            // Independent of wall BaseOffsetFt. Two modes: room height minus a top offset
            // (mirrors wall behavior), or an absolute height above the level.
            double heightAboveLevel = opt.CeilingUseRoomHeightOffset
                ? Math.Max(0, roomHeight - opt.CeilingTopOffsetFt)
                : opt.CeilingHeightAboveLevelFt;

            TrySetDouble(ceil, BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM, heightAboveLevel);
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
        // Stamping (BA_Room_Number / BA_Room_Name shared parameters)
        // -----------------------------
        private static void StampElement(Element e, Room room)
        {
            if (e == null || room == null) return;

            TrySetStringByName(e, ParamRoomNumber, room.Number ?? "");
            TrySetStringByName(e, ParamRoomName, room.Name ?? "");
        }

        private static bool TrySetStringByName(Element e, string paramName, string val)
        {
            try
            {
                var p = e.LookupParameter(paramName);
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
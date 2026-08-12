using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using BA.Settings.Rooms;

namespace BATools.Rooms.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TransferAreaValuesToRoomsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            var settings = AreaTransferSettings.Load();

            // --- 1. Collect Rooms with a valid location point ---
            List<Room> rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .ToList();

            if (rooms.Count == 0)
            {
                TaskDialog.Show("Area Transfer", "No valid placed rooms found in the project.");
                return Result.Cancelled;
            }

            // --- 2. Collect Areas, classify by Area Scheme suffix ---
            List<Area> allAreas = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Areas)
                .WhereElementIsNotElementType()
                .Cast<Area>()
                .Where(a => a.Area > 0)
                .ToList();

            if (allAreas.Count == 0)
            {
                TaskDialog.Show("Area Transfer", "No valid placed Area elements found in the project.");
                return Result.Cancelled;
            }

            var upAreas = new List<Area>();
            var ppAreas = new List<Area>();
            var unclassifiedSchemeLog = new List<string>();

            foreach (Area area in allAreas)
            {
                AreaScheme scheme = area.AreaScheme;
                if (scheme == null)
                {
                    unclassifiedSchemeLog.Add($"Area id={area.Id.Value}: no AreaScheme resolved");
                    continue;
                }

                string schemeName = scheme.Name ?? string.Empty;

                bool isUp = schemeName.EndsWith(settings.AreaSchemeSuffixUp, StringComparison.OrdinalIgnoreCase);
                bool isPp = schemeName.EndsWith(settings.AreaSchemeSuffixPp, StringComparison.OrdinalIgnoreCase);

                if (isUp && isPp)
                {
                    // Configured suffixes overlap in a way that both match one scheme name.
                    // Treat as a configuration error rather than silently picking one.
                    unclassifiedSchemeLog.Add($"Area id={area.Id.Value}: scheme '{schemeName}' matches BOTH UP and PP suffixes — check AreaTransferSettings");
                    continue;
                }

                if (isUp)
                    upAreas.Add(area);
                else if (isPp)
                    ppAreas.Add(area);
                // else: area belongs to an irrelevant scheme (e.g. Gross Building), skip silently
            }

            if (upAreas.Count == 0 && ppAreas.Count == 0)
            {
                TaskDialog.Show("Area Transfer",
                    $"No Areas matched the configured scheme suffixes '{settings.AreaSchemeSuffixUp}' / '{settings.AreaSchemeSuffixPp}'.");
                return Result.Cancelled;
            }

            // --- 3. Group UP/PP areas by LevelId for cheap per-room candidate lookup ---
            var upByLevel = upAreas
                .GroupBy(a => a.LevelId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var ppByLevel = ppAreas
                .GroupBy(a => a.LevelId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var boundaryOptions = new SpatialElementBoundaryOptions();

            // --- 4. Match each Room by point-in-polygon against same-level UP/PP areas, write results ---
            int writtenRoomCount = 0;
            int noLocationCount = 0;
            int noMatchCount = 0;
            int missingParamCount = 0;
            var noLocationLog = new List<string>();
            var noMatchLog = new List<string>();
            var missingParamLog = new List<string>();
            var degenerateBoundaryLog = new List<string>();

            using (Transaction tx = new Transaction(doc, "BA: Transfer Area Values to Rooms"))
            {
                tx.Start();

                foreach (Room room in rooms)
                {
                    if (!(room.Location is LocationPoint locPoint))
                    {
                        noLocationCount++;
                        noLocationLog.Add($"Room id={room.Id.Value}: Location is not a LocationPoint, skipped");
                        continue;
                    }

                    UV roomPoint = new UV(locPoint.Point.X, locPoint.Point.Y);
                    ElementId levelId = room.LevelId;

                    double upSum = 0.0;
                    double ppSum = 0.0;
                    bool matchedAny = false;

                    if (upByLevel.TryGetValue(levelId, out List<Area> upCandidates))
                    {
                        foreach (Area area in upCandidates)
                        {
                            if (!TryGetAreaLoops(area, boundaryOptions, out List<List<UV>> loops, out string boundaryError))
                            {
                                degenerateBoundaryLog.Add($"Area id={area.Id.Value} (UP): {boundaryError}");
                                continue;
                            }

                            if (IsPointInLoops(roomPoint, loops))
                            {
                                upSum += area.Area;
                                matchedAny = true;
                            }
                        }
                    }

                    if (ppByLevel.TryGetValue(levelId, out List<Area> ppCandidates))
                    {
                        foreach (Area area in ppCandidates)
                        {
                            if (!TryGetAreaLoops(area, boundaryOptions, out List<List<UV>> loops, out string boundaryError))
                            {
                                degenerateBoundaryLog.Add($"Area id={area.Id.Value} (PP): {boundaryError}");
                                continue;
                            }

                            if (IsPointInLoops(roomPoint, loops))
                            {
                                ppSum += area.Area;
                                matchedAny = true;
                            }
                        }
                    }

                    if (!matchedAny)
                    {
                        noMatchCount++;
                        noMatchLog.Add($"Room id={room.Id.Value} (Number={room.Number}): no UP or PP Area contains its location point on the same level");
                        continue;
                    }

                    bool upOk = TrySetAreaParam(room, settings.RoomAreaUpParam, upSum, out string upError);
                    bool ppOk = TrySetAreaParam(room, settings.RoomAreaPpParam, ppSum, out string ppError);

                    if (!upOk || !ppOk)
                    {
                        missingParamCount++;
                        if (!upOk) missingParamLog.Add($"Room id={room.Id.Value} ({room.Number}): {upError}");
                        if (!ppOk) missingParamLog.Add($"Room id={room.Id.Value} ({room.Number}): {ppError}");
                    }
                    else
                    {
                        writtenRoomCount++;
                    }
                }

                tx.Commit();
            }

            ShowReport(
                rooms.Count, upAreas.Count, ppAreas.Count,
                writtenRoomCount, noLocationCount, noMatchCount, missingParamCount,
                noLocationLog, noMatchLog, missingParamLog, unclassifiedSchemeLog, degenerateBoundaryLog);

            return Result.Succeeded;
        }

        // Builds 2D (X/Y) polygon loops from an Area's boundary segments.
        // Each loop is tessellated (handles arcs/curves, not just straight lines).
        // Multiple loops are expected when the Area has holes (e.g. excluded shafts);
        // point-containment across holes is handled via XOR in IsPointInLoops, not here.
        private static bool TryGetAreaLoops(
            Area area,
            SpatialElementBoundaryOptions options,
            out List<List<UV>> loops,
            out string error)
        {
            loops = new List<List<UV>>();

            IList<IList<BoundarySegment>> segmentLoops;
            try
            {
                segmentLoops = area.GetBoundarySegments(options);
            }
            catch (Exception ex)
            {
                error = $"GetBoundarySegments threw: {ex.Message}";
                return false;
            }

            if (segmentLoops == null || segmentLoops.Count == 0)
            {
                error = "No boundary segments (unenclosed or degenerate area)";
                return false;
            }

            foreach (IList<BoundarySegment> segLoop in segmentLoops)
            {
                var pts = new List<UV>();

                foreach (BoundarySegment seg in segLoop)
                {
                    Curve curve = seg.GetCurve();
                    if (curve == null)
                        continue;

                    IList<XYZ> tess;
                    try
                    {
                        tess = curve.Tessellate();
                    }
                    catch
                    {
                        continue;
                    }

                    // Skip the final point of each curve to avoid duplicating the
                    // first point of the next curve in the loop.
                    for (int i = 0; i < tess.Count - 1; i++)
                        pts.Add(new UV(tess[i].X, tess[i].Y));
                }

                if (pts.Count >= 3)
                    loops.Add(pts);
            }

            if (loops.Count == 0)
            {
                error = "Boundary loops tessellated to fewer than 3 points, geometry too degenerate to test";
                return false;
            }

            error = null;
            return true;
        }

        // Even-odd (XOR) point-in-polygon across all loops of an Area.
        // A point inside an odd number of loops is inside the Area; this
        // correctly excludes points falling inside a hole loop.
        private static bool IsPointInLoops(UV point, List<List<UV>> loops)
        {
            bool inside = false;
            foreach (List<UV> loop in loops)
            {
                if (IsPointInSingleLoop(point, loop))
                    inside = !inside;
            }
            return inside;
        }

        // Standard ray-casting point-in-polygon test for one closed loop.
        private static bool IsPointInSingleLoop(UV point, List<UV> loop)
        {
            bool inside = false;
            int n = loop.Count;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                UV pi = loop[i];
                UV pj = loop[j];

                bool crosses = ((pi.V > point.V) != (pj.V > point.V)) &&
                    (point.U < (pj.U - pi.U) * (point.V - pi.V) / (pj.V - pi.V) + pi.U);

                if (crosses)
                    inside = !inside;
            }

            return inside;
        }

        // Sets an Area-typed shared parameter on a room.
        // Area SPs have StorageType.Double; Revit expects internal units (sq ft)
        // when calling Parameter.Set(double).
        private static bool TrySetAreaParam(Room room, string paramName, double valueInternalSqFt, out string error)
        {
            Parameter p = room.LookupParameter(paramName);
            if (p == null)
            {
                error = $"Parameter '{paramName}' not found on element";
                return false;
            }

            if (p.IsReadOnly)
            {
                error = $"Parameter '{paramName}' is read-only";
                return false;
            }

            if (p.StorageType != StorageType.Double)
            {
                error = $"Parameter '{paramName}' has unexpected StorageType {p.StorageType} (expected Double)";
                return false;
            }

            bool result = p.Set(valueInternalSqFt);
            if (!result)
            {
                error = $"Parameter.Set failed for '{paramName}' — value may be out of range";
                return false;
            }

            error = null;
            return true;
        }

        private static void ShowReport(
            int totalRooms, int totalUpAreas, int totalPpAreas,
            int writtenRoomCount, int noLocationCount, int noMatchCount, int missingParamCount,
            List<string> noLocationLog, List<string> noMatchLog, List<string> missingParamLog,
            List<string> unclassifiedSchemeLog, List<string> degenerateBoundaryLog)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Transfer complete (geometric point-in-polygon matching).");
            sb.AppendLine($"  Rooms scanned:           {totalRooms}");
            sb.AppendLine($"  UP areas matched scheme: {totalUpAreas}");
            sb.AppendLine($"  PP areas matched scheme: {totalPpAreas}");
            sb.AppendLine($"  Rooms written:           {writtenRoomCount}");
            sb.AppendLine($"  Rooms with no location:  {noLocationCount}");
            sb.AppendLine($"  Rooms with no geometric match: {noMatchCount}");
            sb.AppendLine($"  Rooms with missing/read-only param: {missingParamCount}");
            sb.AppendLine($"  Areas skipped (scheme/boundary issues): {unclassifiedSchemeLog.Count + degenerateBoundaryLog.Count}");

            AppendSection(sb, "Rooms with no geometric match:", noMatchLog);
            AppendSection(sb, "Rooms with missing/read-only parameters:", missingParamLog);
            AppendSection(sb, "Rooms with no LocationPoint:", noLocationLog);
            AppendSection(sb, "Areas with unclassified/ambiguous scheme:", unclassifiedSchemeLog);
            AppendSection(sb, "Areas with degenerate boundary geometry:", degenerateBoundaryLog);

            TaskDialog.Show("BA: Area Transfer", sb.ToString());
        }

        private static void AppendSection(StringBuilder sb, string header, List<string> log)
        {
            if (log.Count == 0)
                return;

            sb.AppendLine();
            sb.AppendLine(header);
            foreach (string s in log.Take(10))
                sb.AppendLine("  " + s);
            if (log.Count > 10)
                sb.AppendLine($"  ... and {log.Count - 10} more");
        }
    }
}
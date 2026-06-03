using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace BATools.Rooms.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TransferAreaValuesToRoomsCommand : IExternalCommand
    {
        private const string ParamRoomNumber = "BA_Number";
        private const string ParamAreaNumber = "BA_Number";
        private const string ParamAreaType = "BA_Area_Type";
        private const string ParamRoomAreaUp = "BA_Area_UP";
        private const string ParamRoomAreaPp = "BA_Area_PP";
        private const string AreaTypeUp = "UP";
        private const string AreaTypePp = "PP";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // --- 1. Collect Rooms ---
            List<Room> rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0) // skip unplaced / not-enclosed rooms
                .ToList();

            if (rooms.Count == 0)
            {
                TaskDialog.Show("Area Transfer", "No valid placed rooms found in the project.");
                return Result.Cancelled;
            }

            // --- 2. Build room lookup: BA_Number string -> Room ---
            // Multiple rooms can share the same BA_Number (e.g. same apartment on multiple levels).
            // We write to all matching rooms.
            var roomLookup = new Dictionary<string, List<Room>>(StringComparer.OrdinalIgnoreCase);

            foreach (Room room in rooms)
            {
                string roomNumber = GetStringParam(room, ParamRoomNumber);
                if (string.IsNullOrWhiteSpace(roomNumber))
                    continue;

                roomNumber = roomNumber.Trim();
                if (!roomLookup.TryGetValue(roomNumber, out List<Room> bucket))
                {
                    bucket = new List<Room>();
                    roomLookup[roomNumber] = bucket;
                }
                bucket.Add(room);
            }

            if (roomLookup.Count == 0)
            {
                TaskDialog.Show("Area Transfer", $"No rooms have a valid '{ParamRoomNumber}' parameter value.");
                return Result.Cancelled;
            }

            // --- 3. Collect Areas (SpatialElement subtype, not Room) ---
            List<Area> areas = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Areas)
                .WhereElementIsNotElementType()
                .Cast<Area>()
                .Where(a => a.Area > 0) // skip unplaced / degenerate areas
                .ToList();

            if (areas.Count == 0)
            {
                TaskDialog.Show("Area Transfer", "No valid placed Area elements found in the project.");
                return Result.Cancelled;
            }

            // --- 4. Aggregate: roomKey -> (UP sum, PP sum) ---
            // Key: room BA_Number string (prefix before first dot in area BA_Number)
            var aggregated = new Dictionary<string, (double Up, double Pp)>(StringComparer.OrdinalIgnoreCase);
            var skippedAreas = new List<string>();

            foreach (Area area in areas)
            {
                string areaNumber = GetStringParam(area, ParamAreaNumber);
                string areaType = GetStringParam(area, ParamAreaType);

                if (string.IsNullOrWhiteSpace(areaNumber))
                {
                    skippedAreas.Add($"Area id={area.Id.Value}: missing {ParamAreaNumber}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(areaType))
                {
                    skippedAreas.Add($"Area id={area.Id.Value} ({areaNumber}): missing {ParamAreaType}");
                    continue;
                }

                areaType = areaType.Trim();

                // Only process UP and PP; silently skip other types
                if (!areaType.Equals(AreaTypeUp, StringComparison.OrdinalIgnoreCase) &&
                    !areaType.Equals(AreaTypePp, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Extract prefix before first dot
                string roomKey = ExtractRoomKeyFromAreaNumber(areaNumber.Trim());
                if (string.IsNullOrWhiteSpace(roomKey))
                {
                    skippedAreas.Add($"Area id={area.Id.Value}: could not parse room key from '{areaNumber}'");
                    continue;
                }

                // Native Area value is in Revit internal units (square feet).
                // We keep internal units throughout and only convert when writing,
                // but since we are writing to a shared parameter of type Area,
                // Revit expects internal units when using Parameter.Set(double).
                double areaValue = area.Area; // internal square feet

                if (!aggregated.TryGetValue(roomKey, out (double Up, double Pp) sums))
                    sums = (0.0, 0.0);

                if (areaType.Equals(AreaTypeUp, StringComparison.OrdinalIgnoreCase))
                    sums.Up += areaValue;
                else
                    sums.Pp += areaValue;

                aggregated[roomKey] = sums;
            }

            // --- 5. Write to Rooms ---
            int writtenRoomCount = 0;
            int missingParamCount = 0;
            int noMatchCount = 0;
            var missingParamLog = new List<string>();
            var noMatchLog = new List<string>();

            using (Transaction tx = new Transaction(doc, "BA: Transfer Area Values to Rooms"))
            {
                tx.Start();

                foreach (KeyValuePair<string, (double Up, double Pp)> entry in aggregated)
                {
                    string roomKey = entry.Key;
                    (double upSum, double ppSum) = entry.Value;

                    if (!roomLookup.TryGetValue(roomKey, out List<Room> matchedRooms))
                    {
                        noMatchCount++;
                        noMatchLog.Add($"Room key '{roomKey}': no room with this {ParamRoomNumber} found");
                        continue;
                    }

                    foreach (Room room in matchedRooms)
                    {
                        bool upOk = TrySetAreaParam(room, ParamRoomAreaUp, upSum, out string upError);
                        bool ppOk = TrySetAreaParam(room, ParamRoomAreaPp, ppSum, out string ppError);

                        if (!upOk || !ppOk)
                        {
                            missingParamCount++;
                            if (!upOk) missingParamLog.Add($"Room id={room.Id.Value} ({roomKey}): {upError}");
                            if (!ppOk) missingParamLog.Add($"Room id={room.Id.Value} ({roomKey}): {ppError}");
                        }
                        else
                        {
                            writtenRoomCount++;
                        }
                    }
                }

                tx.Commit();
            }

            // --- 6. Report ---
            ShowReport(writtenRoomCount, missingParamCount, noMatchCount,
                       skippedAreas, missingParamLog, noMatchLog);

            return Result.Succeeded;
        }

        // Extracts the room key from an area BA_Number.
        // "1.2"  -> "1"
        // "1.12" -> "1"
        // "1"    -> "1"
        // "A1.2" -> "A1"  (handles alphanumeric prefixes)
        // "1.2.3"-> "1"   (only first segment)
        private static string ExtractRoomKeyFromAreaNumber(string areaNumber)
        {
            if (string.IsNullOrWhiteSpace(areaNumber))
                return null;

            int dotIndex = areaNumber.IndexOf('.');
            return dotIndex >= 0
                ? areaNumber.Substring(0, dotIndex).Trim()
                : areaNumber.Trim();
        }

        // Reads a string-typed shared parameter from any element.
        private static string GetStringParam(Element element, string paramName)
        {
            Parameter p = element.LookupParameter(paramName);
            if (p == null || p.StorageType != StorageType.String)
                return null;
            return p.AsString();
        }

        // Sets an Area-typed shared parameter on a room.
        // Area SPs have StorageType.Double and ForgeTypeId of area spec.
        // Revit expects internal units (sq ft) when calling Parameter.Set(double).
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
            int writtenRoomCount,
            int missingParamCount,
            int noMatchCount,
            List<string> skippedAreas,
            List<string> missingParamLog,
            List<string> noMatchLog)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Transfer complete.");
            sb.AppendLine($"  Rooms written:         {writtenRoomCount}");
            sb.AppendLine($"  Rooms with missing SP: {missingParamCount}");
            sb.AppendLine($"  Unmatched area keys:   {noMatchCount}");
            sb.AppendLine($"  Skipped areas:         {skippedAreas.Count}");

            if (noMatchLog.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Unmatched area keys (no room found):");
                foreach (string s in noMatchLog.Take(10))
                    sb.AppendLine("  " + s);
                if (noMatchLog.Count > 10)
                    sb.AppendLine($"  ... and {noMatchLog.Count - 10} more");
            }

            if (missingParamLog.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Missing/read-only parameters:");
                foreach (string s in missingParamLog.Take(10))
                    sb.AppendLine("  " + s);
                if (missingParamLog.Count > 10)
                    sb.AppendLine($"  ... and {missingParamLog.Count - 10} more");
            }

            if (skippedAreas.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Skipped areas (missing BA_Number or BA_Area_Type):");
                foreach (string s in skippedAreas.Take(10))
                    sb.AppendLine("  " + s);
                if (skippedAreas.Count > 10)
                    sb.AppendLine($"  ... and {skippedAreas.Count - 10} more");
            }

            TaskDialog.Show("BA: Area Transfer", sb.ToString());
        }
    }
}
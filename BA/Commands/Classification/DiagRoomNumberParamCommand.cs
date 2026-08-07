using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace BA.Commands.Diagnostics
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DiagRoomNumberParamCommand : IExternalCommand
    {
        private const string ParamName = "BA_Room_Number";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            List<Room> rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .OrderBy(r => r.Id.Value)
                .ToList();

            List<Area> areas = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Areas)
                .WhereElementIsNotElementType()
                .Cast<Area>()
                .Where(a => a.Area > 0)
                .OrderBy(a => a.Id.Value)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("BA_Room_Number diagnostic dump");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Document: {doc.Title}");
            sb.AppendLine(new string('=', 100));

            int roomMissingParam = 0;
            int roomBadGuid = 0;
            int roomSuspectString = 0;
            int areaMissingParam = 0;
            int areaBadGuid = 0;
            int areaSuspectString = 0;

            Guid? expectedGuid = FindSharedParamGuid(doc, ParamName);

            sb.AppendLine();
            sb.AppendLine($"Expected shared parameter GUID (from doc.ParameterBindings lookup): {(expectedGuid.HasValue ? expectedGuid.Value.ToString() : "NOT FOUND IN BINDINGS TABLE")}");
            sb.AppendLine(new string('=', 100));

            sb.AppendLine();
            sb.AppendLine("ROOMS");
            sb.AppendLine(new string('-', 100));

            foreach (Room room in rooms)
            {
                DumpElement(room, room.Id.Value, sb, expectedGuid,
                    ref roomMissingParam, ref roomBadGuid, ref roomSuspectString);
            }

            sb.AppendLine();
            sb.AppendLine("AREAS");
            sb.AppendLine(new string('-', 100));

            foreach (Area area in areas)
            {
                DumpElement(area, area.Id.Value, sb, expectedGuid,
                    ref areaMissingParam, ref areaBadGuid, ref areaSuspectString);
            }

            sb.AppendLine();
            sb.AppendLine(new string('=', 100));
            sb.AppendLine("SUMMARY");
            sb.AppendLine($"Rooms total: {rooms.Count}  | missing param: {roomMissingParam}  | GUID mismatch: {roomBadGuid}  | suspect chars: {roomSuspectString}");
            sb.AppendLine($"Areas total: {areas.Count}  | missing param: {areaMissingParam}  | GUID mismatch: {areaBadGuid}  | suspect chars: {areaSuspectString}");

            string outputPath = Path.Combine(Path.GetTempPath(), $"BA_RoomNumberDiag_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);

            TaskDialog td = new TaskDialog("BA: Room Number Diagnostic")
            {
                MainInstruction = "Diagnostic complete",
                MainContent =
                    $"Rooms: {rooms.Count} total, {roomMissingParam} missing param, {roomBadGuid} GUID mismatch, {roomSuspectString} suspect strings\n" +
                    $"Areas: {areas.Count} total, {areaMissingParam} missing param, {areaBadGuid} GUID mismatch, {areaSuspectString} suspect strings\n\n" +
                    $"Full dump written to:\n{outputPath}",
                CommonButtons = TaskDialogCommonButtons.Ok
            };
            td.Show();

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = outputPath,
                    UseShellExecute = true
                });
            }
            catch
            {
                // If no default text editor associated, user still has the path from the dialog.
            }

            return Result.Succeeded;
        }

        private static void DumpElement(
            Element element,
            long id,
            StringBuilder sb,
            Guid? expectedGuid,
            ref int missingParamCount,
            ref int badGuidCount,
            ref int suspectStringCount)
        {
            Parameter p = element.LookupParameter(ParamName);

            if (p == null)
            {
                missingParamCount++;
                sb.AppendLine($"[Id={id}] LookupParameter returned NULL for '{ParamName}'");
                return;
            }

            Guid actualGuid = Guid.Empty;
            bool guidResolved = false;

            if (p.Definition is InternalDefinition internalDef)
            {
                try
                {
                    SharedParameterElement spe = SharedParameterElement.Lookup(element.Document, GetSharedParamGuidFromInternalDef(element.Document, internalDef));
                    if (spe != null)
                    {
                        actualGuid = spe.GuidValue;
                        guidResolved = true;
                    }
                }
                catch
                {
                    // fall through, guidResolved stays false
                }
            }

            bool guidMismatch = expectedGuid.HasValue && guidResolved && actualGuid != expectedGuid.Value;
            if (guidMismatch) badGuidCount++;

            string raw = p.StorageType == StorageType.String ? p.AsString() : $"<StorageType={p.StorageType}, not String>";
            string storageWarning = p.StorageType != StorageType.String ? "  *** UNEXPECTED STORAGE TYPE ***" : "";

            bool suspect = false;
            string charDump = "(null)";

            if (raw != null)
            {
                var codes = new StringBuilder();
                foreach (char c in raw)
                {
                    codes.Append($"U+{(int)c:X4} ");
                    if (c > 126 || (c < 32 && c != 9))
                        suspect = true;
                }
                charDump = codes.ToString().TrimEnd();

                if (raw.Length != raw.Trim().Length)
                    suspect = true;
            }

            if (suspect) suspectStringCount++;

            sb.AppendLine($"[Id={id}] Value=\"{raw}\"{storageWarning}");
            sb.AppendLine($"          Length={raw?.Length ?? 0}  GuidResolved={guidResolved}  Guid={(guidResolved ? actualGuid.ToString() : "N/A")}{(guidMismatch ? "  *** GUID MISMATCH ***" : "")}");
            sb.AppendLine($"          CharCodes: {charDump}{(suspect ? "  *** SUSPECT CHARACTERS OR WHITESPACE ***" : "")}");
        }

        // Walks doc.ParameterBindings to find the GUID for a shared parameter by display name.
        // Used as the "expected" GUID to compare each element's resolved parameter against.
        private static Guid? FindSharedParamGuid(Document doc, string paramName)
        {
            BindingMap map = doc.ParameterBindings;
            DefinitionBindingMapIterator it = map.ForwardIterator();
            it.Reset();

            while (it.MoveNext())
            {
                Definition def = it.Key;
                if (def == null || !string.Equals(def.Name, paramName, StringComparison.Ordinal))
                    continue;

                if (def is InternalDefinition internalDef)
                {
                    ElementId paramId = internalDef.Id;
                    if (paramId.Value > 0) // shared/project parameters have positive ids; built-ins are negative
                    {
                        Element paramElem = doc.GetElement(paramId);
                        if (paramElem is SharedParameterElement spe)
                            return spe.GuidValue;
                    }
                }
            }

            return null;
        }

        // Resolves the GUID for a given InternalDefinition on a specific element by matching
        // its ElementId back to a SharedParameterElement in the document.
        private static Guid GetSharedParamGuidFromInternalDef(Document doc, InternalDefinition internalDef)
        {
            ElementId paramId = internalDef.Id;
            if (paramId.Value <= 0)
                return Guid.Empty;

            Element paramElem = doc.GetElement(paramId);
            if (paramElem is SharedParameterElement spe)
                return spe.GuidValue;

            return Guid.Empty;
        }
    }
}
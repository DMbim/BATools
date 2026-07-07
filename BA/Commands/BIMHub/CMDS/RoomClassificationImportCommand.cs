using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BA.RoomClassification
{
    [Transaction(TransactionMode.Manual)]
    public sealed class RoomClassificationImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Run(commandData.Application, ref message);
        }

        public static Result Run(UIApplication uiApp, ref string message)
        {
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc?.Document;

            if (doc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            if (doc.IsFamilyDocument)
            {
                message = "This command must run in a project document, not in the family editor.";
                return Result.Failed;
            }

            string filePath = PickExcelFile();
            if (string.IsNullOrWhiteSpace(filePath))
                return Result.Cancelled;

            IReadOnlyList<RoomClassificationRecord> records;
            try
            {
                records = RoomClassificationExcelReader.Read(filePath);
            }
            catch (Exception ex)
            {
                message = "Failed to read Excel file.\n\n" + ex.Message;
                return Result.Failed;
            }

            RoomClassificationValidationResult validation = RoomClassificationValidator.Validate(records);
            if (!validation.IsValid)
            {
                message = validation.BuildMessage();
                return Result.Failed;
            }

            using (TransactionGroup tg = new TransactionGroup(doc, "Import Room Classification"))
            {
                tg.Start();

                IList<RoomClassificationParameterDefinition> parameterDefinitions =
                    RoomClassificationParameterCatalog.BuildDefault();

                using (Transaction t1 = new Transaction(doc, "Ensure room shared parameters"))
                {
                    t1.Start();
                    RoomClassificationSharedParameterService.EnsureRoomParameters(
                        uiApp.Application, doc, parameterDefinitions);
                    t1.Commit();
                }

                ViewSchedule keySchedule;
                using (Transaction t2 = new Transaction(doc, "Ensure room key schedule"))
                {
                    t2.Start();
                    keySchedule = RoomClassificationScheduleService.EnsureRoomKeySchedule(
                        doc, parameterDefinitions);
                    t2.Commit();
                }

                RoomClassificationSyncResult syncResult;
                using (Transaction t3 = new Transaction(doc, "Sync room classification data"))
                {
                    t3.Start();
                    syncResult = RoomClassificationSyncService.UpsertRoomClassificationKeys(
                        doc, keySchedule, records);
                    t3.Commit();
                }

                tg.Assimilate();
                TaskDialog.Show("Room Classification Import", syncResult.BuildMessage());
            }

            return Result.Succeeded;
        }

        private static string PickExcelFile()
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Select Room Classification Excel File",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx|Excel Macro Workbook (*.xlsm)|*.xlsm",
                Multiselect = false,
                CheckFileExists = true
            };

            return dialog.ShowDialog() == true ? dialog.FileName : string.Empty;
        }
    }

    internal static class RoomClassificationParameterNames
    {
        public const string RoomKey = "BA_RoomKey";
        public const string ProgramType = "BA_ProgramType";
        public const string Department = "BA_Department";
        public const string RoomFunction = "BA_RoomFunction";
        public const string RoomCode = "BA_RoomCode";
        public const string RoomGroup = "BA_RoomGroup";
    }

    internal sealed class RoomClassificationRecord
    {
        public string RoomKey { get; set; } = string.Empty;
        public string ProgramType { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string RoomFunction { get; set; } = string.Empty;
        public string RoomCode { get; set; } = string.Empty;
        public string RoomGroup { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public string Notes { get; set; } = string.Empty;
        public int SourceRowNumber { get; set; }
    }

    internal sealed class RoomClassificationParameterDefinition
    {
        public RoomClassificationParameterDefinition(string name, Guid guid)
        {
            Name = name;
            Guid = guid;
        }

        public string Name { get; }
        public Guid Guid { get; }
    }

    internal static class RoomClassificationParameterCatalog
    {
        public static IList<RoomClassificationParameterDefinition> BuildDefault()
        {
            return new List<RoomClassificationParameterDefinition>
            {
                new RoomClassificationParameterDefinition(RoomClassificationParameterNames.RoomKey,      new Guid("11111111-1111-1111-1111-111111111111")),
                new RoomClassificationParameterDefinition(RoomClassificationParameterNames.ProgramType,  new Guid("22222222-2222-2222-2222-222222222222")),
                new RoomClassificationParameterDefinition(RoomClassificationParameterNames.Department,   new Guid("33333333-3333-3333-3333-333333333333")),
                new RoomClassificationParameterDefinition(RoomClassificationParameterNames.RoomFunction, new Guid("44444444-4444-4444-4444-444444444444")),
                new RoomClassificationParameterDefinition(RoomClassificationParameterNames.RoomCode,     new Guid("55555555-5555-5555-5555-555555555555")),
                new RoomClassificationParameterDefinition(RoomClassificationParameterNames.RoomGroup,    new Guid("66666666-6666-6666-6666-666666666666")),
            };
        }
    }

    internal static class RoomClassificationExcelReader
    {
        private const string SheetName = "Matrix";

        public static IReadOnlyList<RoomClassificationRecord> Read(string filePath)
        {
            using XLWorkbook workbook = new XLWorkbook(filePath);
            IXLWorksheet ws = workbook.Worksheet(SheetName);
            if (ws == null)
                throw new InvalidOperationException($"Worksheet '{SheetName}' was not found.");

            Dictionary<string, int> headerMap = BuildHeaderMap(ws);
            EnsureRequiredHeaders(headerMap);

            List<RoomClassificationRecord> result = new List<RoomClassificationRecord>();
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;

            for (int row = 2; row <= lastRow; row++)
            {
                if (IsEntireRowEmpty(ws, row, headerMap))
                    continue;

                RoomClassificationRecord record = new RoomClassificationRecord
                {
                    RoomKey = GetCellString(ws, row, headerMap, "BA_RoomKey"),
                    ProgramType = GetCellString(ws, row, headerMap, "BA_ProgramType"),
                    Department = GetCellString(ws, row, headerMap, "BA_Department"),
                    RoomFunction = GetCellString(ws, row, headerMap, "BA_RoomFunction"),
                    RoomCode = GetCellString(ws, row, headerMap, "BA_RoomCode"),
                    RoomGroup = GetCellString(ws, row, headerMap, "BA_RoomGroup"),
                    IsActive = ParseBooleanLike(GetCellString(ws, row, headerMap, "IsActive"), true),
                    Notes = GetCellString(ws, row, headerMap, "Notes"),
                    SourceRowNumber = row
                };

                if (record.IsActive)
                    result.Add(record);
            }

            return result;
        }

        private static Dictionary<string, int> BuildHeaderMap(IXLWorksheet ws)
        {
            Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

            for (int col = 1; col <= lastCol; col++)
            {
                string header = ws.Cell(1, col).GetString().Trim();
                if (!string.IsNullOrWhiteSpace(header))
                    map[header] = col;
            }

            return map;
        }

        private static void EnsureRequiredHeaders(Dictionary<string, int> headerMap)
        {
            string[] required =
            {
                "BA_RoomKey","BA_ProgramType","BA_Department","BA_RoomFunction",
                "BA_RoomCode","BA_RoomGroup","IsActive","Notes"
            };

            List<string> missing = required.Where(x => !headerMap.ContainsKey(x)).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException("Missing required columns: " + string.Join(", ", missing));
        }

        private static bool IsEntireRowEmpty(IXLWorksheet ws, int row, Dictionary<string, int> headerMap)
        {
            foreach (KeyValuePair<string, int> kvp in headerMap)
            {
                if (!string.IsNullOrWhiteSpace(ws.Cell(row, kvp.Value).GetString()))
                    return false;
            }
            return true;
        }

        private static string GetCellString(IXLWorksheet ws, int row, Dictionary<string, int> headerMap, string header)
        {
            return headerMap.TryGetValue(header, out int col)
                ? ws.Cell(row, col).GetString().Trim()
                : string.Empty;
        }

        private static bool ParseBooleanLike(string value, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            string n = value.Trim().ToLowerInvariant();
            if (n == "1" || n == "true" || n == "yes" || n == "y") return true;
            if (n == "0" || n == "false" || n == "no" || n == "n") return false;
            return defaultValue;
        }
    }

    internal sealed class RoomClassificationValidationResult
    {
        public List<string> Errors { get; } = new List<string>();
        public bool IsValid => Errors.Count == 0;

        public string BuildMessage()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Excel validation failed.");
            sb.AppendLine();
            foreach (string error in Errors)
                sb.AppendLine("• " + error);
            return sb.ToString();
        }
    }

    internal static class RoomClassificationValidator
    {
        public static RoomClassificationValidationResult Validate(IReadOnlyList<RoomClassificationRecord> records)
        {
            RoomClassificationValidationResult result = new RoomClassificationValidationResult();

            if (records == null || records.Count == 0)
            {
                result.Errors.Add("No active data rows were found in the Excel file.");
                return result;
            }

            foreach (RoomClassificationRecord r in records)
            {
                Require(r, "BA_RoomKey", r.RoomKey, result);
                Require(r, "BA_ProgramType", r.ProgramType, result);
                Require(r, "BA_Department", r.Department, result);
                Require(r, "BA_RoomFunction", r.RoomFunction, result);
                Require(r, "BA_RoomCode", r.RoomCode, result);
                Require(r, "BA_RoomGroup", r.RoomGroup, result);
            }

            foreach (IGrouping<string, RoomClassificationRecord> grp in
                records.GroupBy(x => x.RoomCode, StringComparer.OrdinalIgnoreCase))
            {
                if (grp.Count() > 1)
                    result.Errors.Add(
                        $"Duplicate BA_RoomCode '{grp.Key}' in rows " +
                        string.Join(", ", grp.Select(x => x.SourceRowNumber)) + ".");
            }

            foreach (IGrouping<string, RoomClassificationRecord> grp in
                records.GroupBy(x => x.RoomKey, StringComparer.OrdinalIgnoreCase))
            {
                if (grp.Count() > 1)
                    result.Errors.Add(
                        $"Duplicate BA_RoomKey '{grp.Key}' in rows " +
                        string.Join(", ", grp.Select(x => x.SourceRowNumber)) + ".");
            }

            return result;
        }

        private static void Require(
            RoomClassificationRecord record,
            string field, string value,
            RoomClassificationValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(value))
                result.Errors.Add($"Row {record.SourceRowNumber}: field '{field}' is empty.");
        }
    }

    internal static class RoomClassificationSharedParameterService
    {
        public static void EnsureRoomParameters(
            Autodesk.Revit.ApplicationServices.Application app,
            Document doc,
            IList<RoomClassificationParameterDefinition> parameterDefinitions)
        {
            string originalFile = app.SharedParametersFilename;
            string tempFile = BuildTemporarySharedParameterFilePath();

            try
            {
                WriteTemporarySharedParameterFile(tempFile, parameterDefinitions);
                app.SharedParametersFilename = tempFile;

                DefinitionFile definitionFile = app.OpenSharedParameterFile();
                if (definitionFile == null)
                    throw new InvalidOperationException("Revit could not open the temporary shared parameter file.");

                DefinitionGroup group = definitionFile.Groups.get_Item("BA_RoomClassification");
                if (group == null)
                    throw new InvalidOperationException("Shared parameter group 'BA_RoomClassification' was not found.");

                Category roomCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Rooms);
                CategorySet categorySet = app.Create.NewCategorySet();
                categorySet.Insert(roomCategory);

                InstanceBinding binding = app.Create.NewInstanceBinding(categorySet);

                foreach (RoomClassificationParameterDefinition p in parameterDefinitions)
                {
                    Definition def = group.Definitions.get_Item(p.Name);
                    if (def == null)
                        throw new InvalidOperationException($"Definition '{p.Name}' was not found.");

                    if (HasProjectParameterBinding(doc, p.Name))
                        continue;

                    bool inserted = doc.ParameterBindings.Insert(def, binding, GroupTypeId.Data);
                    if (!inserted && !doc.ParameterBindings.ReInsert(def, binding, GroupTypeId.Data))
                        throw new InvalidOperationException(
                            $"Failed to bind parameter '{p.Name}' to Rooms.");
                }
            }
            finally
            {
                app.SharedParametersFilename = originalFile;
                TryDeleteFile(tempFile);
            }
        }

        private static bool HasProjectParameterBinding(Document doc, string parameterName)
        {
            BindingMap map = doc.ParameterBindings;
            DefinitionBindingMapIterator it = map.ForwardIterator();
            it.Reset();

            while (it.MoveNext())
            {
                Definition def = it.Key;
                if (def != null &&
                    string.Equals(def.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string BuildTemporarySharedParameterFilePath()
        {
            string folder = Path.Combine(Path.GetTempPath(), "BA.RoomClassification");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "BA_RoomClassification_SharedParameters.txt");
        }

        private static void WriteTemporarySharedParameterFile(
            string filePath,
            IList<RoomClassificationParameterDefinition> parameterDefinitions)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# This is a Revit shared parameter file.");
            sb.AppendLine("*META\tVERSION\tMINVERSION");
            sb.AppendLine("META\t2\t1");
            sb.AppendLine("*GROUP\tID\tNAME");
            sb.AppendLine("GROUP\t1\tBA_RoomClassification");
            sb.AppendLine("*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE");

            foreach (RoomClassificationParameterDefinition p in parameterDefinitions)
                sb.AppendLine($"PARAM\t{p.Guid:D}\t{p.Name}\tTEXT\t\t1\t1\tRoom classification field\t1\t0");

            File.WriteAllText(filePath, sb.ToString(), Encoding.Unicode);
        }

        private static void TryDeleteFile(string filePath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch { }
        }
    }

    internal static class RoomClassificationScheduleService
    {
        private const string ScheduleName = "BA_RoomClassification_Keys";

        public static ViewSchedule EnsureRoomKeySchedule(
            Document doc,
            IList<RoomClassificationParameterDefinition> parameterDefinitions)
        {
            ViewSchedule existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(x =>
                    !x.IsTemplate &&
                    string.Equals(x.Name, ScheduleName, StringComparison.OrdinalIgnoreCase));

            ViewSchedule schedule = existing ??
                ViewSchedule.CreateKeySchedule(doc, new ElementId(BuiltInCategory.OST_Rooms));

            if (!string.Equals(schedule.Name, ScheduleName, StringComparison.OrdinalIgnoreCase))
                schedule.Name = ScheduleName;

            EnsureFields(doc, schedule, parameterDefinitions);
            return schedule;
        }

        private static void EnsureFields(
            Document doc,
            ViewSchedule schedule,
            IList<RoomClassificationParameterDefinition> parameterDefinitions)
        {
            ScheduleDefinition definition = schedule.Definition;
            IList<SchedulableField> schedulableFields = definition.GetSchedulableFields();

            foreach (RoomClassificationParameterDefinition p in parameterDefinitions)
            {
                bool fieldExists = definition.GetFieldOrder()
                    .Select(id => definition.GetField(id))
                    .Any(f => string.Equals(f.GetName(), p.Name, StringComparison.OrdinalIgnoreCase));

                if (fieldExists) continue;

                SchedulableField sf = schedulableFields.FirstOrDefault(x =>
                    string.Equals(x.GetName(doc), p.Name, StringComparison.OrdinalIgnoreCase));

                if (sf != null)
                    definition.AddField(sf);
            }
        }
    }

    internal sealed class RoomClassificationSyncResult
    {
        public int Updated { get; set; }
        public int Created { get; set; }
        public int ExistingExtraRows { get; set; }
        public List<string> ExtraCodes { get; } = new List<string>();

        public string BuildMessage()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Room classification sync completed.");
            sb.AppendLine();
            sb.AppendLine($"Created: {Created}");
            sb.AppendLine($"Updated: {Updated}");
            sb.AppendLine($"Extra rows already in Revit (not deleted): {ExistingExtraRows}");

            if (ExtraCodes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Extra BA_RoomCode values found only in Revit:");
                foreach (string code in ExtraCodes.OrderBy(x => x))
                    sb.AppendLine("• " + code);
            }

            return sb.ToString();
        }
    }

    internal static class RoomClassificationSyncService
    {
        public static RoomClassificationSyncResult UpsertRoomClassificationKeys(
            Document doc,
            ViewSchedule keySchedule,
            IReadOnlyList<RoomClassificationRecord> sourceRows)
        {
            RoomClassificationSyncResult result = new RoomClassificationSyncResult();
            Dictionary<string, Element> existingByCode =
                CollectExistingKeyElementsByCode(doc, keySchedule);

            foreach (RoomClassificationRecord row in sourceRows)
            {
                if (existingByCode.TryGetValue(row.RoomCode, out Element existing))
                {
                    WriteRecordToElement(existing, row);
                    result.Updated++;
                }
                else
                {
                    Element created = CreateNewKeyElement(doc, keySchedule);
                    WriteRecordToElement(created, row);
                    result.Created++;
                }
            }

            HashSet<string> importedCodes = new HashSet<string>(
                sourceRows.Select(x => x.RoomCode), StringComparer.OrdinalIgnoreCase);

            foreach (string existingCode in existingByCode.Keys.OrderBy(x => x))
            {
                if (!importedCodes.Contains(existingCode))
                {
                    result.ExistingExtraRows++;
                    result.ExtraCodes.Add(existingCode);
                }
            }

            return result;
        }

        private static Dictionary<string, Element> CollectExistingKeyElementsByCode(
            Document doc, ViewSchedule keySchedule)
        {
            Dictionary<string, Element> result =
                new Dictionary<string, Element>(StringComparer.OrdinalIgnoreCase);

            foreach (Element e in new FilteredElementCollector(doc, keySchedule.Id)
                .WhereElementIsNotElementType().ToElements())
            {
                string code = ParameterWriteUtil.GetString(e, RoomClassificationParameterNames.RoomCode);
                if (!string.IsNullOrWhiteSpace(code) && !result.ContainsKey(code))
                    result.Add(code, e);
            }

            return result;
        }

        private static Element CreateNewKeyElement(Document doc, ViewSchedule keySchedule)
        {
            ICollection<ElementId> beforeIds = new FilteredElementCollector(doc, keySchedule.Id)
                .WhereElementIsNotElementType().ToElementIds();

            TableSectionData body = keySchedule.GetTableData().GetSectionData(SectionType.Body);
            int insertAt = body.LastRowNumber + 1;
            if (insertAt < body.FirstRowNumber) insertAt = body.FirstRowNumber;
            body.InsertRow(insertAt);

            ICollection<ElementId> afterIds = new FilteredElementCollector(doc, keySchedule.Id)
                .WhereElementIsNotElementType().ToElementIds();

            ElementId newId = afterIds.Except(beforeIds).FirstOrDefault();
            if (newId == null || newId == ElementId.InvalidElementId)
                throw new InvalidOperationException(
                    "Could not resolve the new key element after inserting a row.");

            Element newElement = doc.GetElement(newId);
            if (newElement == null)
                throw new InvalidOperationException(
                    "The new key element could not be resolved from the document.");

            return newElement;
        }

        private static void WriteRecordToElement(Element element, RoomClassificationRecord row)
        {
            ParameterWriteUtil.SetString(element, RoomClassificationParameterNames.RoomKey, row.RoomKey);
            ParameterWriteUtil.SetString(element, RoomClassificationParameterNames.ProgramType, row.ProgramType);
            ParameterWriteUtil.SetString(element, RoomClassificationParameterNames.Department, row.Department);
            ParameterWriteUtil.SetString(element, RoomClassificationParameterNames.RoomFunction, row.RoomFunction);
            ParameterWriteUtil.SetString(element, RoomClassificationParameterNames.RoomCode, row.RoomCode);
            ParameterWriteUtil.SetString(element, RoomClassificationParameterNames.RoomGroup, row.RoomGroup);
        }
    }

    internal static class ParameterWriteUtil
    {
        public static string GetString(Element element, string parameterName)
        {
            Parameter p = FindParameter(element, parameterName);
            if (p == null) return string.Empty;
            return p.StorageType == StorageType.String
                ? p.AsString() ?? string.Empty
                : p.AsValueString() ?? string.Empty;
        }

        public static void SetString(Element element, string parameterName, string value)
        {
            Parameter p = FindParameter(element, parameterName)
                ?? throw new InvalidOperationException(
                    $"Parameter '{parameterName}' was not found on element {element.Id.Value}.");

            if (p.IsReadOnly)
                throw new InvalidOperationException(
                    $"Parameter '{parameterName}' is read-only on element {element.Id.Value}.");

            if (p.StorageType != StorageType.String)
                throw new InvalidOperationException(
                    $"Parameter '{parameterName}' is not a text parameter on element {element.Id.Value}.");

            p.Set(value ?? string.Empty);
        }

        private static Parameter FindParameter(Element element, string parameterName)
        {
            Parameter p = element.LookupParameter(parameterName);
            if (p != null) return p;
            return element.GetParameters(parameterName).FirstOrDefault();
        }
    }
}

using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using BA.RoomClassification.Models;
using BA.RoomClassification.Configuration;

namespace BA.RoomClassification.Services
{
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
            // All eleven fields are real BA_Tools shared parameters now - treated identically.
            // Blank source value writes empty string. A missing or read-only parameter throws
            // immediately via ParameterWriteUtil.SetString rather than being silently skipped,
            // per project decision.
            ParameterWriteUtil.SetString(element, RoomClassificationParameterNames.RoomKey, row.RoomKey);
            ParameterWriteUtil.SetString(element, RoomClassificationParameterNames.ProgramType, row.ProgramType);
            ParameterWriteUtil.SetString(element, RoomClassificationParameterNames.Department, row.Department);
            ParameterWriteUtil.SetString(element, RoomClassificationParameterNames.RoomFunction, row.RoomFunction);
            ParameterWriteUtil.SetString(element, RoomClassificationParameterNames.RoomCode, row.RoomCode);
            ParameterWriteUtil.SetString(element, RoomClassificationParameterNames.RoomGroup, row.RoomGroup);
            ParameterWriteUtil.SetString(element, RoomClassificationParameterNames.FinishTier,
                string.IsNullOrWhiteSpace(row.FinishTier) ? "Standard" : row.FinishTier.Trim()); // persists the effective tier, not just the raw cell
            ParameterWriteUtil.SetString(element, RoomClassificationParameterNames.RoomFinishFloor, row.FloorFinish);
            ParameterWriteUtil.SetString(element, RoomClassificationParameterNames.RoomFinishWall, row.WallFinish);
            ParameterWriteUtil.SetString(element, RoomClassificationParameterNames.RoomFinishCeiling, row.CeilingFinish);
            ParameterWriteUtil.SetString(element, RoomClassificationParameterNames.RoomFinishBase, row.BaseFinish);
        }
    }
}

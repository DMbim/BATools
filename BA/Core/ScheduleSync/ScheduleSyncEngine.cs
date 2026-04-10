using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace BA
{
    public static class ScheduleSyncEngine
    {
        public static void Execute(Document doc, ViewSchedule schedule, List<ScheduleMappingRow> mappings)
        {
            if (schedule == null || mappings == null || mappings.Count == 0) return;

            using (Transaction t = new Transaction(doc, "BA Schedule Sync"))
            {
                t.Start();

                var tableData = schedule.GetTableData();
                var body = tableData.GetSectionData(SectionType.Body);
                int rows = body.NumberOfRows;

                int idCol = GetColumnIndex(schedule, "Element ID");
                if (idCol < 0) { t.RollBack(); return; }

                foreach (var mapping in mappings)
                {
                    if (string.IsNullOrEmpty(mapping.SourceColumn)
                        || string.IsNullOrEmpty(mapping.DestinationParameter))
                        continue;

                    int sourceCol = GetColumnIndex(schedule, mapping.SourceColumn);
                    if (sourceCol < 0) continue;

                    for (int r = 0; r < rows; r++)
                    {
                        try
                        {
                            string idText = schedule.GetCellText(SectionType.Body, r, idCol);
                            if (!int.TryParse(idText, out int idInt)) continue;

                            Element el = doc.GetElement(new ElementId(idInt));
                            if (el == null) continue;

                            string value = schedule.GetCellText(SectionType.Body, r, sourceCol);

                            Parameter dest = el.LookupParameter(mapping.DestinationParameter);
                            if (dest == null || dest.IsReadOnly) continue;

                            SetValue(dest, value);
                        }
                        catch { }
                    }
                }

                t.Commit();
            }
        }

        private static int GetColumnIndex(ViewSchedule schedule, string name)
        {
            var def = schedule.Definition;
            for (int i = 0; i < def.GetFieldCount(); i++)
            {
                if (def.GetField(i).GetName() == name)
                    return i;
            }
            return -1;
        }

        private static void SetValue(Parameter p, string val)
        {
            switch (p.StorageType)
            {
                case StorageType.String:
                    p.Set(val);
                    break;
                case StorageType.Double:
                    if (double.TryParse(val, out double d))
                        p.Set(d);
                    break;
                case StorageType.Integer:
                    if (int.TryParse(val, out int i))
                        p.Set(i);
                    break;
            }
        }
    }
}

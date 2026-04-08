using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA
{
    public static class ScheduleSyncEngine
    {
        public static void Execute(Document doc, List<ScheduleMappingRow> mappings)
        {
            using (Transaction t = new Transaction(doc, "BA Schedule Sync"))
            {
                t.Start();

                foreach (var mapping in mappings)
                {
                    if (mapping.Schedule == null) continue;

                    var schedule = mapping.Schedule;
                    var tableData = schedule.GetTableData();
                    var body = tableData.GetSectionData(SectionType.Body);

                    int rows = body.NumberOfRows;

                    int sourceCol = GetColumnIndex(schedule, mapping.SourceColumn);
                    int idCol = GetColumnIndex(schedule, "Element ID");

                    if (sourceCol < 0 || idCol < 0) continue;

                    for (int r = 0; r < rows; r++)
                    {
                        try
                        {
                            string idText = schedule.GetCellText(SectionType.Body, r, idCol);

                            if (!int.TryParse(idText, out int idInt))
                                continue;

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

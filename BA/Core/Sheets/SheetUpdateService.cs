using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using BA.Settings;

namespace BA.Core.Sheets
{
    public static class SheetUpdateService
    {
        public static void Apply(
            Document doc,
            IList<SheetUpdateRow> selected,
            DateToolSettings settings,
            out SheetUpdateReport report)
        {
            report = new SheetUpdateReport();

            if (doc == null) { report.Errors.Add("Document is null."); return; }
            if (selected == null || selected.Count == 0) { report.Errors.Add("No selected rows."); return; }

            string nowText = GetNowText(settings.SelectedFormat);

            // Map by sheet number (fast)
            var map = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Sheets)
                .WhereElementIsNotElementType()
                .OfType<ViewSheet>()
                .GroupBy(s => s.SheetNumber ?? string.Empty)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var row in selected)
            {
                report.Requested++;

                if (string.IsNullOrWhiteSpace(row.SheetNumber) || !map.TryGetValue(row.SheetNumber, out var sheet))
                {
                    report.SkippedMissingSheet++;
                    continue;
                }

                bool doDate = row.UpdateBoth || row.UpdateDate;
                bool doRev = row.UpdateBoth || row.UpdateRevision;

                if (doDate)
                {
                    if (TrySetText(sheet.LookupParameter(settings.SelectedDateParam), nowText))
                        report.UpdatedDate++;
                    else
                        report.SkippedDateParam++;
                }

                if (doRev)
                {
                    if (TryIncrement(sheet.LookupParameter(settings.SelectedRevParam)))
                        report.UpdatedRevision++;
                    else
                        report.SkippedRevisionParam++;
                }
            }
        }

        public static string GetNowText(string? format)
        {
            try
            {
                var f = (format ?? "yy/MM/dd").Trim();
                if (string.IsNullOrWhiteSpace(f)) f = "yy/MM/dd";
                return DateTime.Now.ToString(f, CultureInfo.InvariantCulture);
            }
            catch
            {
                return DateTime.Now.ToString("yy/MM/dd", CultureInfo.InvariantCulture);
            }
        }

        private static bool TrySetText(Parameter? p, string value)
        {
            if (p == null || p.IsReadOnly) return false;

            try
            {
                if (p.StorageType == StorageType.String)
                    return p.Set(value ?? string.Empty);

                // For non-string parameters, try value-string (best effort)
                return p.SetValueString(value ?? string.Empty);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryIncrement(Parameter? p)
        {
            if (p == null || p.IsReadOnly) return false;

            try
            {
                if (p.StorageType == StorageType.Integer)
                {
                    p.Set(p.AsInteger() + 1);
                    return true;
                }

                // string / other => parse or start at 1
                var s = (p.AsString() ?? p.AsValueString() ?? "").Trim();
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cur))
                {
                    if (p.StorageType == StorageType.String) p.Set((cur + 1).ToString(CultureInfo.InvariantCulture));
                    else p.SetValueString((cur + 1).ToString(CultureInfo.InvariantCulture));
                    return true;
                }

                if (p.StorageType == StorageType.String) p.Set("1");
                else p.SetValueString("1");
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public sealed class SheetUpdateRow
    {
        public string SheetNumber { get; init; } = "";
        public bool UpdateDate { get; init; }
        public bool UpdateRevision { get; init; }
        public bool UpdateBoth { get; init; }
    }

    public sealed class SheetUpdateReport
    {
        public int Requested { get; set; }

        public int UpdatedDate { get; set; }
        public int UpdatedRevision { get; set; }

        public int SkippedMissingSheet { get; set; }
        public int SkippedDateParam { get; set; }
        public int SkippedRevisionParam { get; set; }

        public List<string> Errors { get; } = new();

        public string ToDialogText()
        {
            var lines = new List<string>
            {
                $"Sheets processed: {Requested}",
                $"Updated dates: {UpdatedDate}",
                $"Updated revisions: {UpdatedRevision}",
                "",
                $"Skipped (sheet not found): {SkippedMissingSheet}",
                $"Skipped (date param missing/read-only/type): {SkippedDateParam}",
                $"Skipped (rev param missing/read-only/type): {SkippedRevisionParam}",
            };

            if (Errors.Count > 0)
            {
                lines.Add("");
                lines.Add("Errors:");
                lines.AddRange(Errors.Select(e => " - " + e));
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}

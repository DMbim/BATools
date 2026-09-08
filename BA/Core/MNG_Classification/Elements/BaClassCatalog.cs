// File: BA.Classification/BaClassCatalog.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using ClosedXML.Excel;

namespace BA.Classification
{
    public sealed class BaClassItem
    {
        public string Domain { get; init; } = "";
        public string Group { get; init; } = "";
        public string Subcode { get; init; } = "";
        public string LevelCode { get; init; } = "";      // e.g. "I.POD-150"
        public string LabelEn { get; init; } = "";
        public string LabelCz { get; init; } = "";
        public string Notes { get; init; } = "";
    }

    public sealed class BaClassCatalog
    {
        private readonly Dictionary<string, BaClassItem> _byLevelCode;

        private BaClassCatalog(Dictionary<string, BaClassItem> byLevelCode)
        {
            _byLevelCode = byLevelCode;
        }

        public bool TryGet(string targetLevelCode, out BaClassItem item)
        {
            item = null!;
            if (string.IsNullOrWhiteSpace(targetLevelCode)) return false;
            return _byLevelCode.TryGetValue(targetLevelCode.Trim(), out item);
        }

        public static BaClassCatalog Load(string excelPath, string sheetName = "BAClass")
        {
            var dict = new Dictionary<string, BaClassItem>(StringComparer.OrdinalIgnoreCase);

            using var wb = new XLWorkbook(excelPath);
            var ws = wb.Worksheet(sheetName);
            var used = ws.RangeUsed();
            if (used == null) return new BaClassCatalog(dict);

            // Expected columns in your file (based on BAClass_Main.xlsx):
            // 1 = Domain (header is weird "uiiiiiui" in your file)
            // 2 = Group
            // 3 = SPEC.NUM.
            // 4 = LEVEL
            // 5 = Label_EN
            // 6 = Label_CZ
            // 7 = Notes
            foreach (var row in used.RowsUsed().Skip(1))
            {
                var domain = (row.Cell(1).GetString() ?? "").Trim();
                var group = (row.Cell(2).GetString() ?? "").Trim();

                // SPEC.NUM can be numeric or blank
                string subcode = "";
                var specNumCell = row.Cell(3);
                if (!specNumCell.IsEmpty())
                {
                    if (specNumCell.TryGetValue<double>(out var d))
                        subcode = ((int)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
                    else
                        subcode = (specNumCell.GetString() ?? "").Trim();
                }

                var level = (row.Cell(4).GetString() ?? "").Trim();
                if (string.IsNullOrWhiteSpace(level)) continue;

                var item = new BaClassItem
                {
                    Domain = domain,
                    Group = group,
                    Subcode = subcode,
                    LevelCode = level,
                    LabelEn = (row.Cell(5).GetString() ?? "").Trim(),
                    LabelCz = (row.Cell(6).GetString() ?? "").Trim(),
                    Notes = (row.Cell(7).GetString() ?? "").Trim()
                };

                dict[level] = item;
            }

            return new BaClassCatalog(dict);
        }
    }
}

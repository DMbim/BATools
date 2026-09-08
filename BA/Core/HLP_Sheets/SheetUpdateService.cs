using System;
using System.Globalization;
using Autodesk.Revit.DB;

namespace BA.Core.Sheets
{
    /// <summary>
    /// Low level parameter write helpers shared by the export job date and
    /// revision bump (BA.Core.Export.Services.DateRevisionBumpService).
    /// The bulk Apply()/SheetUpdateRow/SheetUpdateReport orchestration
    /// that used to live here was retired along with
    /// Cmd_SheetDateAndRevision, export jobs are now the only path that
    /// bumps date/revision.
    /// </summary>
    public static class SheetUpdateService
    {
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

        internal static bool TrySetText(Parameter? p, string value)
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

        internal static bool TryIncrement(Parameter? p)
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
}
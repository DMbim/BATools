using Autodesk.Revit.DB;
using System;

namespace BA.Keyplan
{
    public static class KeyplanParameterUtils
    {
        public static string GetSheetZoneValue(ViewSheet sheet, string parameterName)
        {
            if (sheet == null) return string.Empty;
            if (string.IsNullOrWhiteSpace(parameterName)) return string.Empty;

            Parameter p = sheet.LookupParameter(parameterName);
            if (p == null) return string.Empty;
            if (p.StorageType != StorageType.String) return string.Empty;

            return (p.AsString() ?? string.Empty).Trim();
        }

        public static void SetSheetZoneValue(ViewSheet sheet, string parameterName, string value)
        {
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));
            if (string.IsNullOrWhiteSpace(parameterName)) throw new ArgumentException("Parameter name is required.", nameof(parameterName));

            Parameter p = sheet.LookupParameter(parameterName);
            if (p == null)
                throw new InvalidOperationException($"Parameter '{parameterName}' was not found on sheet '{sheet.SheetNumber}'.");

            if (p.StorageType != StorageType.String)
                throw new InvalidOperationException($"Parameter '{parameterName}' is not a text parameter.");

            p.Set(value ?? string.Empty);
        }
    }
}
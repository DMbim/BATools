using System;
using System.Globalization;
using Autodesk.Revit.DB;

namespace BA_Tools.ScheduleExporter.Helpers
{
    /// <summary>
    /// Converts Revit Parameter values to Excel-safe types for export, and
    /// parses Excel string values back to Revit parameter values for import.
    ///
    /// Export strategy:
    ///   String   -> string as-is
    ///   Integer  -> int (Yes/No mapped to "Yes"/"No" string for round-trip clarity)
    ///   Double   -> formatted display string via AsValueString() to preserve project units
    ///   ElementId -> display name of referenced element (read-only, never reimported)
    ///
    /// Import strategy for Double:
    ///   UnitFormatUtils.TryParse converts display-unit strings back to internal (feet) values.
    ///   Fallback: invariant culture double parse for unitless numeric entries.
    /// </summary>
    public static class ParameterValueConverter
    {
        /// <summary>
        /// Converts a Revit Parameter value to an object suitable for storage in an Excel cell.
        /// Returns string.Empty for null, unset, or unsupported parameters.
        /// </summary>
        public static object ToExcelValue(Parameter param, Document doc)
        {
            if (param == null || !param.HasValue)
                return string.Empty;

            switch (param.StorageType)
            {
                case StorageType.String:
                    return param.AsString() ?? string.Empty;

                case StorageType.Integer:
                {
                    // Detect Yes/No parameters by their display string
                    string display = param.AsValueString();
                    if (display == "Yes" || display == "No")
                        return display;
                    return param.AsInteger();
                }

                case StorageType.Double:
                    // AsValueString() returns the value formatted in project units (e.g. "3.500 m")
                    // This is what the user sees in the schedule and what we parse back on import
                    return param.AsValueString() ?? string.Empty;

                case StorageType.ElementId:
                {
                    ElementId elemId = param.AsElementId();
                    if (elemId == ElementId.InvalidElementId)
                        return string.Empty;
                    Element elem = doc.GetElement(elemId);
                    return elem?.Name ?? elemId.Value.ToString(CultureInfo.InvariantCulture);
                }

                case StorageType.None:
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Attempts to write a string value from Excel back to a Revit Parameter.
        /// Returns false and sets error if the write fails for any reason.
        /// Does NOT open a transaction; caller is responsible for transaction context.
        /// </summary>
        public static bool TrySetValue(
            Parameter param,
            string cellValue,
            Document doc,
            out string error)
        {
            error = null;

            if (param == null)
            {
                error = "Parameter not found on element.";
                return false;
            }
            if (param.IsReadOnly)
            {
                error = "Parameter is read-only (built-in constraint or element type lock).";
                return false;
            }

            cellValue = cellValue ?? string.Empty;

            try
            {
                switch (param.StorageType)
                {
                    case StorageType.String:
                        if (!param.Set(cellValue))
                        {
                            error = "Parameter.Set() returned false for string value.";
                            return false;
                        }
                        return true;

                    case StorageType.Integer:
                    {
                        string lower = cellValue.Trim().ToLowerInvariant();

                        // Handle Yes/No booleans
                        if (lower == "yes" || lower == "true" || lower == "1")
                            return SetIntegerOrError(param, 1, out error);
                        if (lower == "no" || lower == "false" || lower == "0")
                            return SetIntegerOrError(param, 0, out error);

                        if (int.TryParse(cellValue, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out int intVal))
                            return SetIntegerOrError(param, intVal, out error);

                        error = $"Cannot parse '{cellValue}' as an integer.";
                        return false;
                    }

                    case StorageType.Double:
                    {
                        // Primary path: parse display-unit string using project units
                        ForgeTypeId specTypeId = param.Definition.GetDataType();
                        if (UnitFormatUtils.TryParse(
                            doc.GetUnits(), specTypeId, cellValue, out double dblVal))
                        {
                            if (!param.Set(dblVal))
                            {
                                error = "Parameter.Set() returned false for double value.";
                                return false;
                            }
                            return true;
                        }

                        // Fallback: plain numeric parse (handles unitless or already-internal values)
                        if (double.TryParse(cellValue, NumberStyles.Any,
                            CultureInfo.InvariantCulture, out double rawDbl))
                        {
                            if (!param.Set(rawDbl))
                            {
                                error = "Parameter.Set() returned false for raw double value.";
                                return false;
                            }
                            return true;
                        }

                        error = $"Cannot parse '{cellValue}' as a numeric value. " +
                                $"Expected format matching project units (e.g. '{param.AsValueString()}').";
                        return false;
                    }

                    case StorageType.ElementId:
                        error = "ElementId parameters cannot be set via text import. Column should have been locked.";
                        return false;

                    default:
                        error = $"Unsupported storage type: {param.StorageType}.";
                        return false;
                }
            }
            catch (Exception ex)
            {
                error = $"Exception during parameter write: {ex.Message}";
                return false;
            }
        }

        private static bool SetIntegerOrError(Parameter param, int value, out string error)
        {
            error = null;
            if (!param.Set(value))
            {
                error = $"Parameter.Set() returned false for integer value {value}.";
                return false;
            }
            return true;
        }
    }
}

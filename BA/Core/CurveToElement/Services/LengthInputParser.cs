// File: BA/Core/CurveToElement/Services/LengthInputParser.cs
// Action: CREATE NEW

using System;
using System.Globalization;
using Autodesk.Revit.DB;

namespace BA.Core.CurveToElement.Services
{
    /// <summary>
    /// Parses user-typed length strings (settings panel offset/height fields) into internal
    /// units (feet), honoring the document's current unit display format (e.g. "10' 6"", "3200"
    /// for mm projects, "3.5 m"). Mirrors the two-stage parse strategy used elsewhere in the
    /// solution for Double-storage parameters: primary path via UnitFormatUtils against the
    /// project's length spec, fallback to a raw invariant-culture double for unitless entries.
    /// </summary>
    public static class LengthInputParser
    {
        public static bool TryParse(Units units, string text, out double feet)
        {
            feet = 0.0;

            if (units == null || string.IsNullOrWhiteSpace(text))
                return false;

            if (UnitFormatUtils.TryParse(units, SpecTypeId.Length, text, out feet))
                return true;

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double rawFeet))
            {
                feet = rawFeet;
                return true;
            }

            return false;
        }
    }
}
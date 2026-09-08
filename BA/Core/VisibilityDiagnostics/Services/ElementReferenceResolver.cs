// File: BA_Tools/VisibilityDiagnostics/Services/ElementReferenceResolver.cs
using System;
using System.Globalization;
using Autodesk.Revit.DB;

namespace BA.VisibilityDiagnostics.Services
{
    /// <summary>
    /// Resolves a user-typed string (numeric Element Id or Unique Id) to an
    /// Element. Must be called from a valid Revit API context.
    /// </summary>
    public static class ElementReferenceResolver
    {
        public static bool TryResolve(Document document, string input, out Element? element, out string? error)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));

            element = null;
            error = null;

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "No Element Id or Unique Id was provided.";
                return false;
            }

            string trimmed = input.Trim();

            // Numeric Element Id. Revit 2024+ backs ElementId with a 64-bit value and
            // removed the old ElementId(int) constructor - long is required here, not int.
            if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long idValue))
            {
                Element? byId = document.GetElement(new ElementId(idValue));
                if (byId is not null)
                {
                    element = byId;
                    return true;
                }

                error = $"No element with Id {idValue} exists in the current document.";
                return false;
            }

            // Unique Id (GUID + trailing element-counter suffix).
            Element? byUniqueId = document.GetElement(trimmed);
            if (byUniqueId is not null)
            {
                element = byUniqueId;
                return true;
            }

            error = $"'{trimmed}' is neither a valid numeric Element Id nor a known Unique Id in the current document.";
            return false;
        }
    }
}

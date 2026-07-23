using System;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BA.Core
{
    public static partial class FamilyParamUtils
    {
        /// <summary>
        /// Safely removes a family parameter, logging the result.
        /// </summary>
        /// <param name="doc">The Revit document.</param>
        /// <param name="fm">The FamilyManager instance.</param>
        /// <param name="fp">The FamilyParameter to remove.</param>
        /// <param name="log">A StringBuilder for logging.</param>
        /// <returns>True if removed, false otherwise.</returns>
        public static bool RemoveParameterSafe(Document doc, FamilyManager fm, FamilyParameter fp, StringBuilder log)
        {
            if (fp == null)
            {
                log?.AppendLine("RemoveParameterSafe: Parameter is null.");
                return false;
            }

            try
            {
                fm.RemoveParameter(fp);
                log?.AppendLine($"Removed parameter '{fp.Definition.Name}'.");
                return true;
            }
            catch (Exception ex)
            {
                log?.AppendLine($"Failed to remove parameter '{fp.Definition.Name}': {ex.Message}");
                return false;
            }
        }
    }
}
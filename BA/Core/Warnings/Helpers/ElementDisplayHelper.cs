// FILE: BA_Tools/Warnings/Helpers/ElementDisplayHelper.cs
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BA.Warnings.Helpers
{
    // Shared element-description formatting, factored out so
    // WarningElementDetailsHandler and DuplicateInstanceReviewHandler don't
    // duplicate the same ElementType lookup and parameter-scanning logic.
    public static class ElementDisplayHelper
    {
        public static string GetFamilyAndTypeName(Document doc, Element el)
        {
            ElementId typeId = el.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                if (doc.GetElement(typeId) is ElementType et)
                {
                    return string.IsNullOrEmpty(et.FamilyName) ? et.Name : $"{et.FamilyName} : {et.Name}";
                }
            }
            return el.Name;
        }

        public static int CountFilledParameters(Element el)
        {
            int count = 0;
            foreach (Parameter p in el.Parameters)
            {
                if (p == null || !p.HasValue) continue;
                if (!string.IsNullOrWhiteSpace(SafeValueString(p))) count++;
            }
            return count;
        }

        public static List<KeyValuePair<string, string>> GetParameterSnapshot(Element el)
        {
            var result = new List<KeyValuePair<string, string>>();
            foreach (Parameter p in el.Parameters)
            {
                if (p == null || p.Definition == null) continue;
                string display = SafeValueString(p);
                if (string.IsNullOrWhiteSpace(display)) continue;
                result.Add(new KeyValuePair<string, string>(p.Definition.Name, display));
            }
            return result;
        }

        private static string SafeValueString(Parameter p)
        {
            try { return p.AsValueString() ?? p.AsString(); }
            catch { return null; }
        }
    }
}
using Autodesk.Revit.DB;
using System;

namespace BA.Core.Parameters
{
    public static class ParameterBindingRemover
    {
        public static bool TryRemoveBinding(Document doc, Definition def)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (def == null) return false;

            var map = doc.ParameterBindings;
            if (map == null) return false;

            try
            {
                return map.Remove(def);
            }
            catch
            {
                return false;
            }
        }
    }
}

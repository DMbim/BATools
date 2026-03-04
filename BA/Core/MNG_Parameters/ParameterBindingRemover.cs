using Autodesk.Revit.DB;
using System;

namespace BA.Core.Parameters
{
    public static class ParameterBindingRemover
    {
        public static bool TryRemoveBinding(Document doc, string name, string guid = "")
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(name)) return false;

            var map = doc.ParameterBindings;
            var it = map.ForwardIterator();
            it.Reset();

            while (it.MoveNext())
            {
                var def = it.Key;
                if (def == null) continue;

                if (!def.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;

                // If GUID provided, ensure exact shared definition match
                if (!string.IsNullOrWhiteSpace(guid))
                {
                    if (def is not ExternalDefinition ext) continue;
                    if (!ext.GUID.ToString().Equals(guid, StringComparison.OrdinalIgnoreCase)) continue;
                }

                map.Remove(def);
                return true;
            }

            return false;
        }
    }
}

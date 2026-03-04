using Autodesk.Revit.DB;
using Autodesk.Revit.ApplicationServices;
using System;
using System.Collections.Generic;

namespace BA.Core.Parameters
{
    public static class ParameterBindingEditor
    {
        public static void Rebind(
            Application app,
            Document doc,
            string paramName,
            string paramGuid,
            ForgeTypeId groupId,
            bool isInstance,
            IList<long> categoryIdValues)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(paramName)) throw new ArgumentNullException(nameof(paramName));
            if (categoryIdValues == null || categoryIdValues.Count == 0) throw new ArgumentNullException(nameof(categoryIdValues));

            var def = ParameterBindingFinder.FindDefinition(doc, paramName, paramGuid);
            if (def == null)
                throw new InvalidOperationException($"Parameter binding not found: '{paramName}' ({paramGuid}).");

            var catSet = app.Create.NewCategorySet();
            foreach (var idVal in categoryIdValues)
            {
                var cat = Category.GetCategory(doc, new ElementId(idVal));
                if (cat != null) catSet.Insert(cat);
            }

            if (catSet.IsEmpty)
                throw new InvalidOperationException("No valid categories to bind.");

            ElementBinding binding = isInstance
                ? app.Create.NewInstanceBinding(catSet)
                : app.Create.NewTypeBinding(catSet);

            var map = doc.ParameterBindings;

            // Insert returns false if it exists
            bool ok = map.Insert(def, binding, groupId) || map.ReInsert(def, binding, groupId);
            if (!ok)
                throw new InvalidOperationException("Failed to update parameter binding (Insert/ReInsert returned false).");
        }
    }
}

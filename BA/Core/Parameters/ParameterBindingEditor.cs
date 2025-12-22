using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.Parameters
{
    public static class ParameterBindingEditor
    {
        public static void Rebind(
            Application app,
            Document doc,
            Definition def,
            ForgeTypeId groupId,
            bool isInstance,
            IList<Category> categories)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (categories == null || categories.Count == 0) throw new ArgumentException("Pick at least one category.", nameof(categories));

            var set = app.Create.NewCategorySet();
            foreach (var c in categories.Where(x => x != null))
                set.Insert(c);

            ElementBinding binding = isInstance
                ? app.Create.NewInstanceBinding(set)
                : app.Create.NewTypeBinding(set);

            bool ok = RevitApiCompat.InsertOrReinsert(doc, def, binding, groupId);
            if (!ok)
                throw new InvalidOperationException("Rebind failed (Insert/ReInsert returned false).");
        }
    }
}

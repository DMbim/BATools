using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.Parameters
{
    public static class SharedParameterBinder
    {
        public static void BindSharedParameter(
            Application app,
            Document doc,
            string sharedParamFilePath,
            string defName,
            Guid guidHint,
            ForgeTypeId groupId,
            bool isInstance,
            IList<Category> categories,
            bool createIfMissing)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(defName)) throw new ArgumentException("Definition name is required.", nameof(defName));
            if (categories == null || categories.Count == 0) throw new ArgumentException("At least one category is required.", nameof(categories));

            // Locate in shared parameter file
            var extDef = SharedParameterFileReader.FindExternalDefinitionByName(app, sharedParamFilePath, defName);

            if (extDef == null && createIfMissing)
            {
                extDef = CreateExternalDefinition(app, sharedParamFilePath, defName);
            }

            if (extDef == null)
                throw new InvalidOperationException($"Shared parameter '{defName}' not found in the shared parameter file.");

            // If GUID hint exists and doesn't match, warn by exception (keeps your data strict)
            if (guidHint != Guid.Empty && extDef.GUID != guidHint)
            {
                // Don’t silently bind wrong parameter
                throw new InvalidOperationException($"GUID mismatch for '{defName}'. File GUID={extDef.GUID} vs expected={guidHint}");
            }

            var catSet = CreateCategorySet(app, categories);

            ElementBinding binding = isInstance
                ? app.Create.NewInstanceBinding(catSet)
                : app.Create.NewTypeBinding(catSet);

            // Insert or reinsert with compat
            bool ok = RevitApiCompat.InsertOrReinsert(doc, extDef, binding, groupId);
            if (!ok)
                throw new InvalidOperationException("Failed to bind the shared parameter (Insert/ReInsert failed).");
        }

        private static ExternalDefinition CreateExternalDefinition(Application app, string sharedParamFilePath, string name)
        {
            if (!string.IsNullOrWhiteSpace(sharedParamFilePath))
                app.SharedParametersFilename = sharedParamFilePath;

            var spf = app.OpenSharedParameterFile();
            if (spf == null)
                throw new InvalidOperationException("Shared parameter file not available (check Revit Options and path).");

            // Put new defs into group "BA" (change to your preference)
            var group = spf.Groups.get_Item("BA") ?? spf.Groups.Create("BA");

            // Default spec = Text (safe). If you want user to choose spec, extend the UI.
            var opts = new ExternalDefinitionCreationOptions(name, SpecTypeId.String.Text)
            {
                Description = "Created via BA Project Tools"
            };

            var def = group.Definitions.Create(opts) as ExternalDefinition;
            return def;
        }

        private static CategorySet CreateCategorySet(Application app, IEnumerable<Category> categories)
        {
            var set = app.Create.NewCategorySet();
            foreach (var c in categories.Where(x => x != null))
                set.Insert(c);
            return set;
        }
    }
}

using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using Application = Autodesk.Revit.ApplicationServices.Application;

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

            var extDef = SharedParameterFileReader.FindExternalDefinitionByName(app, sharedParamFilePath, defName);

            if (extDef == null && createIfMissing)
                extDef = CreateExternalDefinition(app, sharedParamFilePath, defName);

            if (extDef == null)
                throw new InvalidOperationException($"Shared parameter '{defName}' not found in the shared parameter file.");

            if (guidHint != Guid.Empty && extDef.GUID != guidHint)
                throw new InvalidOperationException($"GUID mismatch for '{defName}'. File GUID={extDef.GUID} vs expected={guidHint}");

            var catSet = CreateCategorySet(app, categories);

            ElementBinding binding = isInstance
                ? app.Create.NewInstanceBinding(catSet)
                : app.Create.NewTypeBinding(catSet);

            var ok = RevitApiCompat.InsertOrReinsert(doc, extDef, binding, groupId);
            if (!ok)
                throw new InvalidOperationException("Failed to bind the shared parameter (Insert/ReInsert failed).");
        }

        internal static ExternalDefinition CreateExternalDefinition(Application app, string sharedParamFilePath, string name)
        {
            if (!string.IsNullOrWhiteSpace(sharedParamFilePath))
                app.SharedParametersFilename = sharedParamFilePath;

            var spf = app.OpenSharedParameterFile();
            if (spf == null)
                throw new InvalidOperationException("Shared parameter file not available (check Revit Options and path).");

            var group = spf.Groups.get_Item("BA") ?? spf.Groups.Create("BA");

            var opts = new ExternalDefinitionCreationOptions(name, SpecTypeId.String.Text)
            {
                Description = "Created via BA Project Tools"
            };

            return group.Definitions.Create(opts) as ExternalDefinition;
        }
        public static void BindSharedParameterByGuid(
                    Application app,
                    Document doc,
                    string sharedParamFilePath,
                    Guid guid,
                    string nameHint,
                    ForgeTypeId groupId,
                    bool isInstance,
                    IList<Category> categories,
                    bool createIfMissing)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (guid == Guid.Empty) throw new ArgumentException("GUID is required.", nameof(guid));
            if (categories == null || categories.Count == 0) throw new ArgumentException("At least one category is required.", nameof(categories));

            var extDef = SharedParameterFileReader.FindExternalDefinitionByGuid(app, sharedParamFilePath, guid);

            // Optional fallback: name-based lookup (if the SP file changed and GUID missing)
            if (extDef == null && !string.IsNullOrWhiteSpace(nameHint))
                extDef = SharedParameterFileReader.FindExternalDefinitionByName(app, sharedParamFilePath, nameHint);

            if (extDef == null && createIfMissing && !string.IsNullOrWhiteSpace(nameHint))
                extDef = CreateExternalDefinition(app, sharedParamFilePath, nameHint);

            if (extDef == null)
                throw new InvalidOperationException($"Shared parameter not found in SP file. GUID={guid}, NameHint='{nameHint}'");

            if (extDef.GUID != guid)
                throw new InvalidOperationException($"GUID mismatch. Expected={guid}, Found={extDef.GUID} (Name='{extDef.Name}')");

            var catSet = CreateCategorySet(app, categories);

            ElementBinding binding = isInstance
                ? app.Create.NewInstanceBinding(catSet)
                : app.Create.NewTypeBinding(catSet);

            var ok = RevitApiCompat.InsertOrReinsert(doc, extDef, binding, groupId);
            if (!ok)
                throw new InvalidOperationException($"Failed to bind shared parameter '{extDef.Name}' (Insert/ReInsert returned false).");
        }
        private static CategorySet CreateCategorySet(Application app, IEnumerable<Category> categories)
        {
            var set = app.Create.NewCategorySet();
            foreach (var c in categories.Where(x => x != null))
                set.Insert(c);
            return set;
        }

        internal static void LoadSharedParameter(Application application, Document d, string path, string name, Guid guid, ForgeTypeId group)
        {
            throw new NotImplementedException();
        }
    }
}

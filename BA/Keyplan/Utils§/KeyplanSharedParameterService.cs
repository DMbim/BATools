using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Linq;

namespace BA.Keyplan
{
    public static class KeyplanSharedParameterService
    {
        public static void EnsureSheetTextSharedParameter(
            UIApplication uiApp,
            Document doc,
            string sharedParameterFilePath,
            string parameterName)
        {
            if (uiApp == null) throw new ArgumentNullException(nameof(uiApp));
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(parameterName)) throw new ArgumentException("Parameter name is required.", nameof(parameterName));

            if (ParameterAlreadyBoundToSheets(doc, parameterName))
                return;

            if (string.IsNullOrWhiteSpace(sharedParameterFilePath))
                throw new InvalidOperationException("Shared parameter file path is empty.");

            if (!File.Exists(sharedParameterFilePath))
                throw new FileNotFoundException("Shared parameter file was not found.", sharedParameterFilePath);

            Application app = uiApp.Application;
            string originalPath = app.SharedParametersFilename;

            try
            {
                app.SharedParametersFilename = sharedParameterFilePath;

                DefinitionFile defFile = app.OpenSharedParameterFile();
                if (defFile == null)
                    throw new InvalidOperationException("Failed to open shared parameter file.");

                DefinitionGroup group = defFile.Groups.get_Item("BA_Sheets");
                if (group == null)
                    group = defFile.Groups.Create("BA_Sheets");

                Definition definition = group.Definitions.get_Item(parameterName);
                if (definition == null)
                {
                    ExternalDefinitionCreationOptions createOptions =
                        new ExternalDefinitionCreationOptions(parameterName, SpecTypeId.String.Text);

                    createOptions.Visible = true;
                    createOptions.Description = "Keyplan zone code assigned to sheet.";

                    definition = group.Definitions.Create(createOptions);
                }

                Category sheetsCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Sheets);
                if (sheetsCategory == null)
                    throw new InvalidOperationException("Could not resolve OST_Sheets category.");

                CategorySet catSet = app.Create.NewCategorySet();
                catSet.Insert(sheetsCategory);

                InstanceBinding binding = app.Create.NewInstanceBinding(catSet);

                BindingMap map = doc.ParameterBindings;

                bool inserted = map.Insert(definition, binding, GroupTypeId.IdentityData);
                if (!inserted)
                {
                    bool reinserted = map.ReInsert(definition, binding, GroupTypeId.IdentityData);
                    if (!reinserted)
                        throw new InvalidOperationException($"Failed to bind shared parameter '{parameterName}' to sheets.");
                }
            }
            finally
            {
                app.SharedParametersFilename = originalPath;
            }
        }

        private static bool ParameterAlreadyBoundToSheets(Document doc, string parameterName)
        {
            BindingMap map = doc.ParameterBindings;
            DefinitionBindingMapIterator it = map.ForwardIterator();
            it.Reset();

            while (it.MoveNext())
            {
                Definition def = it.Key;
                if (def == null) continue;

                if (!string.Equals(def.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                    continue;

                ElementBinding binding = it.Current as ElementBinding;
                if (binding == null) continue;

                foreach (Category c in binding.Categories)
                {
                    if (c == null) continue;
                    if (c.Id.Value == (int)BuiltInCategory.OST_Sheets)
                        return true;
                }
            }

            return false;
        }
    }
}
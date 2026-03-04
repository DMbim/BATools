using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.IO;

namespace BA.Core.Parameters
{
    public static class SharedParameterFileReader
    {
        public static List<SharedDefRow> ReadAll(Application app, string sharedParamFilePath)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            EnsureFile(app, sharedParamFilePath);

            var spf = app.OpenSharedParameterFile();
            if (spf == null) return new List<SharedDefRow>();

            var list = new List<SharedDefRow>();

            foreach (DefinitionGroup g in spf.Groups)
            {
                foreach (Definition d in g.Definitions)
                {
                    var guid = (d as ExternalDefinition)?.GUID ?? Guid.Empty;
                    list.Add(new SharedDefRow { Name = d.Name, GroupName = g.Name, Guid = guid });
                }
            }

            return list;
        }
        public static ExternalDefinition CreateExternalDefinition_String(
            Autodesk.Revit.ApplicationServices.Application app,
            string sharedParamFilePath,
            string definitionName,
            string groupName)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            EnsureFile(app, sharedParamFilePath);

            var file = app.OpenSharedParameterFile();
            if (file == null) return null;

            var group = file.Groups.get_Item(groupName) ?? file.Groups.Create(groupName);

            var opt = new ExternalDefinitionCreationOptions(definitionName, SpecTypeId.String.Text);
            var def = group.Definitions.Create(opt) as ExternalDefinition;
            return def;
        }

        public static ExternalDefinition FindExternalDefinitionByName(Application app, string sharedParamFilePath, string name)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (string.IsNullOrWhiteSpace(name)) return null;

            EnsureFile(app, sharedParamFilePath);

            var spf = app.OpenSharedParameterFile();
            if (spf == null) return null;

            foreach (DefinitionGroup g in spf.Groups)
            {
                foreach (Definition d in g.Definitions)
                {
                    if (d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return d as ExternalDefinition;
                }
            }

            return null;
        }

        public static ExternalDefinition FindExternalDefinitionByGuid(Application app, string sharedParamFilePath, Guid guid)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (guid == Guid.Empty) return null;

            EnsureFile(app, sharedParamFilePath);

            var spf = app.OpenSharedParameterFile();
            if (spf == null) return null;

            foreach (DefinitionGroup g in spf.Groups)
            {
                foreach (Definition d in g.Definitions)
                {
                    if (d is ExternalDefinition ext && ext.GUID == guid)
                        return ext;
                }
            }

            return null;
        }

        private static void EnsureFile(Application app, string sharedParamFilePath)
        {
            if (!string.IsNullOrWhiteSpace(sharedParamFilePath))
            {
                if (!File.Exists(sharedParamFilePath))
                    throw new FileNotFoundException("Shared parameter file not found.", sharedParamFilePath);

                app.SharedParametersFilename = sharedParamFilePath;
            }

            // OpenSharedParameterFile() must succeed later; we don't force open here.
        }
    }
}

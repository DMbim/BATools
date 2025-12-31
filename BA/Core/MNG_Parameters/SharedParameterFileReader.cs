using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BA.Core.Parameters
{
    public static class SharedParameterFileReader
    {
        public static List<SharedDefRow> ReadAll(Application app, string sharedParamFilePath)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            if (!string.IsNullOrWhiteSpace(sharedParamFilePath))
            {
                if (!File.Exists(sharedParamFilePath))
                    throw new FileNotFoundException("Shared parameter file not found.", sharedParamFilePath);

                app.SharedParametersFilename = sharedParamFilePath;
            }

            var spf = app.OpenSharedParameterFile();
            if (spf == null) return new List<SharedDefRow>();

            var list = new List<SharedDefRow>();

            foreach (DefinitionGroup g in spf.Groups)
            {
                foreach (Definition d in g.Definitions)
                {
                    var guid = (d as ExternalDefinition)?.GUID ?? Guid.Empty;
                    list.Add(new SharedDefRow
                    {
                        Name = d.Name,
                        GroupName = g.Name,
                        Guid = guid
                    });
                }
            }

            return list;
        }


        public static ExternalDefinition FindExternalDefinitionByName(Application app, string sharedParamFilePath, string name)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (string.IsNullOrWhiteSpace(name)) return null;

            ReadAll(app, sharedParamFilePath); // sets filename + ensures file openable

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
    }
}

// BA/Core/Parameters/SharedParameterFileReader.cs
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.IO;
using Application = Autodesk.Revit.ApplicationServices.Application;

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
            Application app,
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

        /// <summary>
        /// Looks up a definition by exact group name and parameter name, rather than scanning
        /// every group. Used by SharedParameterBindingService.EnsureBound(doc, path, groupName,
        /// paramName, category, ...), which needs a specific group and treats a missing group or
        /// missing definition as a distinct, actionable error rather than "not found anywhere".
        /// Throws (never returns null) since the caller in that path always expects the
        /// definition to exist already, auto-creation is not this method's job.
        /// </summary>
        public static Definition FindExternalDefinitionInGroup(
            Application app,
            string sharedParamFilePath,
            string groupName,
            string paramName)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            string originalFile;
            try
            {
                originalFile = app.SharedParametersFilename;
            }
            catch
            {
                originalFile = string.Empty;
            }

            try
            {
                app.SharedParametersFilename = sharedParamFilePath;

                DefinitionFile defFile = app.OpenSharedParameterFile()
                    ?? throw new InvalidOperationException(
                        $"Could not open the shared parameter file at '{sharedParamFilePath}'. " +
                        "Verify the file exists and is accessible on the network.");

                DefinitionGroup group = defFile.Groups.get_Item(groupName)
                    ?? throw new InvalidOperationException(
                        $"Shared parameter group '{groupName}' was not found in " +
                        $"'{sharedParamFilePath}'.");

                Definition definition = group.Definitions.get_Item(paramName)
                    ?? throw new InvalidOperationException(
                        $"Shared parameter '{paramName}' is not defined in group " +
                        $"'{groupName}' of '{sharedParamFilePath}'. The definition itself is " +
                        "missing from the shared parameter file, this cannot be auto-fixed. " +
                        "Contact your BIM admin to add it before this feature can be used.");

                return definition;
            }
            finally
            {
                try
                {
                    app.SharedParametersFilename = originalFile;
                }
                catch
                {
                    // Best-effort restore of the session's shared parameter file pointer.
                }
            }
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
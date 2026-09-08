using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using BA.RoomClassification.Models;
using BA.RoomClassification.Configuration;
using BA.Core.Parameters;

namespace BA.RoomClassification.Services
{
    internal static class RoomClassificationSharedParameterService
    {
        public static void EnsureRoomParameters(
            Autodesk.Revit.ApplicationServices.Application app,
            Document doc,
            IList<RoomClassificationParameterDefinition> parameterDefinitions)
        {
            string originalFile = app.SharedParametersFilename;
            try
            {
                // Swap to the real network shared parameter file for the duration of the
                // binding pass, then restore whatever the user had configured. Does not
                // assume BA_SharedParametersWIP2 is already every workstation's permanent file.
                app.SharedParametersFilename = SharedParamPaths.WIP2;
                DefinitionFile definitionFile = app.OpenSharedParameterFile();
                if (definitionFile == null)
                    throw new InvalidOperationException(
                        $"Revit could not open the shared parameter file at " +
                        $"'{SharedParamPaths.WIP2}'. " +
                        "Verify the network path is reachable and the file is not locked or malformed.");

                DefinitionGroup group = definitionFile.Groups.get_Item(RoomClassificationSharedParameterFileConfig.GroupName);
                if (group == null)
                    throw new InvalidOperationException(
                        $"Shared parameter group '{RoomClassificationSharedParameterFileConfig.GroupName}' " +
                        $"was not found in '{SharedParamPaths.WIP2}'.");

                Category roomCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Rooms);
                CategorySet categorySet = app.Create.NewCategorySet();
                categorySet.Insert(roomCategory);
                InstanceBinding binding = app.Create.NewInstanceBinding(categorySet);

                foreach (RoomClassificationParameterDefinition p in parameterDefinitions)
                {
                    Definition def = group.Definitions.get_Item(p.Name);
                    if (def == null)
                        throw new InvalidOperationException(
                            $"Shared parameter '{p.Name}' was not found in group " +
                            $"'{RoomClassificationSharedParameterFileConfig.GroupName}'. " +
                            "Verify the name matches the shared parameter file exactly, including case and punctuation. " +
                            "If this is BA.Tls_FinishTier or BA.Tls_RoomFinishBase, you still need to create it in the SP file.");

                    // Defensive GUID check: confirms the definition Revit resolved by name is
                    // actually the one this add-in expects, catching a stale/duplicate copy of
                    // the shared parameter file being pointed to instead of the real network file.
                    if (def is ExternalDefinition externalDef && externalDef.GUID != p.Guid)
                        throw new InvalidOperationException(
                            $"Shared parameter '{p.Name}' resolved to GUID {externalDef.GUID:D}, " +
                            $"but the add-in expects {p.Guid:D}. The shared parameter file being read " +
                            $"is not the expected BA_Tools master file, or contains a duplicate definition under this name, " +
                            $"or (if the expected GUID shown is all zeros) you have not replaced the Guid.Empty placeholder yet.");

                    if (HasProjectParameterBinding(doc, p.Name))
                        continue;

                    bool inserted = doc.ParameterBindings.Insert(def, binding, GroupTypeId.Data);
                    if (!inserted && !doc.ParameterBindings.ReInsert(def, binding, GroupTypeId.Data))
                        throw new InvalidOperationException(
                            $"Failed to bind parameter '{p.Name}' to Rooms. This usually means a " +
                            "non-shared project parameter with the same name already exists in this " +
                            "document - remove or rename it, then re-run the import.");
                }
            }
            finally
            {
                app.SharedParametersFilename = originalFile;
            }
        }

        private static bool HasProjectParameterBinding(Document doc, string parameterName)
        {
            BindingMap map = doc.ParameterBindings;
            DefinitionBindingMapIterator it = map.ForwardIterator();
            it.Reset();
            while (it.MoveNext())
            {
                Definition def = it.Key;
                if (def != null &&
                    string.Equals(def.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}

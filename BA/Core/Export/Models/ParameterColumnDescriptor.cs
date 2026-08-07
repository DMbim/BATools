using System;
using Autodesk.Revit.DB;

namespace BA.Core.Export.Models
{
    public enum ParameterColumnSource
    {
        BuiltIn,
        Project,
        Shared
    }

    public enum ParameterValueKind
    {
        Text,
        Integer,
        Number,
        YesNo,
        ElementReference,
        Unsupported
    }

    /// <summary>
    /// Stable, persistable identity for one dynamic parameter column. Never
    /// stores an ElementId, those are unstable across sessions. Shared
    /// parameters are identified by GUID, matching the same rule already
    /// established for the Type Data Ledger. Built-in parameters are
    /// identified by BuiltInParameter. Everything else falls back to name,
    /// same discipline NamingTemplateEngine already uses for arbitrary
    /// project parameters.
    /// </summary>
    public class ParameterColumnDescriptor
    {
        public string DisplayName { get; set; } = string.Empty;
        public ParameterColumnSource Source { get; set; }
        public bool IsInstance { get; set; } = true;
        public ParameterValueKind ValueKind { get; set; } = ParameterValueKind.Text;

        /// <summary>
        /// Populated only when Source is BuiltIn.
        /// </summary>
        public BuiltInParameter? BuiltInParameterId { get; set; }

        /// <summary>
        /// Populated only when Source is Shared.
        /// </summary>
        public Guid? SharedParamGuid { get; set; }

        /// <summary>
        /// Populated only when Source is Project. This is the only case
        /// where the column depends on a name match rather than a stable
        /// identifier, since Revit has no other stable handle on a project
        /// parameter definition.
        /// </summary>
        public string ProjectParameterName { get; set; } = string.Empty;

        /// <summary>
        /// Stable key used for row value dictionary lookups, DataGrid
        /// column binding paths, and duplicate detection. Deliberately not
        /// DisplayName, the user could rename how it's shown later without
        /// this key changing.
        /// </summary>
        public string ColumnKey
        {
            get
            {
                switch (Source)
                {
                    case ParameterColumnSource.Shared:
                        return $"SP:{SharedParamGuid:N}";
                    case ParameterColumnSource.BuiltIn:
                        return $"BIP:{BuiltInParameterId}";
                    default:
                        return $"PP:{ProjectParameterName}";
                }
            }
        }
    }
}

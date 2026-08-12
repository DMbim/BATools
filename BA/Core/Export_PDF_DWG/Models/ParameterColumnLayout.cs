using System.Collections.Generic;

namespace BA.Core.Export.Models
{
    /// <summary>
    /// Persisted set of dynamic parameter columns the user has configured
    /// for the sheet picker. Global to the user, not scoped to a single
    /// export job or a single project, the same column set is useful across
    /// every job and every project this user works in.
    /// </summary>
    public class ParameterColumnLayout
    {
        public int SchemaVersion { get; set; } = 1;

        public List<ParameterColumnDescriptor> Columns { get; set; } = new List<ParameterColumnDescriptor>();
    }
}

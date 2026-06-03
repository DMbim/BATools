using System.Collections.Generic;

namespace BATools.ParamCopy.Models
{
    public enum PairingMode
    {
        ByCommonParameter,
        ManualSelectAndPair,
        DragAndDrop
    }

    public class ListSettings
    {
        public string CategoryName { get; set; } = string.Empty;
        public List<FilterSet> FilterSets { get; set; } = new();

        /// <summary>
        /// Parameter names to resolve and display as columns in the element grid.
        /// Replaces the old single DisplayParameterName.
        /// </summary>
        public List<string> DisplayParameterNames { get; set; } = new();
    }

    public class ParamCopySettings
    {
        public ListSettings Source { get; set; } = new();
        public ListSettings Dest { get; set; } = new();
        public List<ParamMapping> Mappings { get; set; } = new();
        public PairingMode PairingMode { get; set; } = PairingMode.ByCommonParameter;
        public string PairingParameterName { get; set; } = string.Empty;
    }
}

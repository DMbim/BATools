// BA/Markup/Settings/MarkupSettings.cs
using BA.Settings;

namespace BA.Markup.Settings
{
    public sealed class MarkupSettings : AppSettingsBase
    {
        protected override string SubFolder => "Markup";
        protected override string FileName => "MarkupSettings.json";

        /// <summary>Offset added around the computed bounding rectangle, stored in millimetres.</summary>
        public double BoundingBoxOffsetMm { get; set; } = 300.0;

        /// <summary>Family file name for the detail item (no extension).</summary>
        public string DetailItemFamilyName { get; set; } = "BA_DetItem_Markup_RCP";

        /// <summary>Family file name for the tag (no extension).</summary>
        public string TagFamilyName { get; set; } = "BA_TAG_Markup";

        /// <summary>Root folder that contains the Revit families.</summary>
        public string FamilySearchRoot { get; set; } =
            @"S:\CAD\Autodesk Revit\BA_Families\BA_Families_v26\BATools";

        // <- CHANGED: added missing ".txt" extension. Confirmed against the actual file
        // on disk (BA_SharedParametersWIP2.txt) -- the old default pointed at a path that
        // never existed, which is why "Shared parameter file not found" always fired.
        /// <summary>Fallback shared-parameter file path.</summary>
        public string SharedParameterFilePath { get; set; } =
            @"S:\CAD\Autodesk Revit\BA_Resources\BA_Shared parameters\BA_SharedParametersWIP2.txt";

        /// <summary>Fixed horizontal tag offset from the top-right corner of the markup, in millimetres.</summary>
        public double TagOffsetXMm { get; set; } = 200.0;

        /// <summary>Fixed vertical tag offset from the top-right corner of the markup, in millimetres.</summary>
        public double TagOffsetYMm { get; set; } = 200.0;
    }
}

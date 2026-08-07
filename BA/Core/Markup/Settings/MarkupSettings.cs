// BA/Markup/Settings/MarkupSettings.cs
using BA.Settings;

namespace BA.Markup.Settings
{
    public sealed class MarkupSettings : AppSettingsBase
    {
        protected override string SubFolder => "Markup";
        protected override string FileName => "MarkupSettings.json";

        public double BoundingBoxOffsetMm { get; set; } = 300.0;

        public string DetailItemFamilyName { get; set; } = "BA_DetItem_Markup_RCP";

        public string TagFamilyName { get; set; } = "BA_TAG_Markup";

        // <- NEW: separate tag family for revision clouds.
        public string RevisionTagFamilyName { get; set; } = "BA_TAG_Revision";

        public string FamilySearchRoot { get; set; } =
            @"S:\CAD\Autodesk Revit\BA_Families\BA_Families_v26\BATools";

        public string SharedParameterFilePath { get; set; } =
            @"S:\CAD\Autodesk Revit\BA_Resources\BA_Shared parameters\BA_SharedParametersWIP2.txt";

        public double TagOffsetXMm { get; set; } = 200.0;
        public double TagOffsetYMm { get; set; } = 200.0;

        // <- NEW: root folder for the per-central markup assignee registry.
        //    Actual file path resolves to {MarkupUserRegistryRoot}\{ProjectSet}\{CentralHash}.json
        //    ProjectSet comes from BA.Core.Ledger.ProjectSetService.GetProjectSetName; if that
        //    returns null (not workshared, or path doesn't match the project number convention),
        //    MarkupUserRegistryService falls back to a folder literally named "_NoProjectSet".
        public string MarkupUserRegistryRoot { get; set; } =
            @"S:\CAD\Autodesk Revit\_admin\BA_tools\MarkupUsers";

        // <- NEW: entries in the markup assignee registry untouched for longer than this
        //    are considered inactive. MarkupCleanupCommand is the only thing that acts on
        //    this value; GetActiveUsers also filters by it when populating the assignee
        //    picker, so a stale-but-not-yet-purged entry never shows up as selectable
        //    even before someone runs cleanup.
        public int MarkupCleanupRetentionMonths { get; set; } = 2;
    }
}
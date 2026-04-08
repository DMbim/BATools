using Autodesk.Revit.DB;

namespace BA
{
    public class ScheduleMappingRow
    {
        public ViewSchedule Schedule { get; set; }
        public string ScheduleName => Schedule?.Name;

        public string SourceColumn { get; set; }
        public string DestinationParameter { get; set; }

        public BuiltInCategory Category { get; set; }
    }
}

using System;

namespace BA.Telemetry.Models
{
    public enum TelemetryEventType
    {
        CommandExecuted,
        DocumentOpened,
        DocumentClosed,
        DocumentSaved,
        DocumentSynced,
        ViewActivated,
        CustomButtonClicked
    }

    public class TelemetryEventModel
    {
        public string Timestamp { get; set; }
        public string WindowsUser { get; set; }
        public string ProjectName { get; set; }
        public string CentralModelPath { get; set; }
        public string DocumentPath { get; set; }
        public TelemetryEventType EventType { get; set; }
        public string CommandName { get; set; }
        public string ExecutionMethod { get; set; }
        public bool Success { get; set; }
        public string Notes { get; set; }

        public TelemetryEventModel()
        {
            Timestamp = DateTime.UtcNow.ToString("o");
            WindowsUser = Environment.UserName;
            Success = true;
            Notes = string.Empty;
            ExecutionMethod = string.Empty;
            CommandName = string.Empty;
            ProjectName = string.Empty;
            CentralModelPath = string.Empty;
            DocumentPath = string.Empty;
        }
    }
}
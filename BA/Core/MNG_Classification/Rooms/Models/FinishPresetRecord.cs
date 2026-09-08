namespace BA.RoomClassification.Models
{
    // One row from the FinishPresets sheet.
    internal sealed class FinishPresetRecord
    {
        public string FinishKey { get; set; } = string.Empty;
        public string FloorFinish { get; set; } = string.Empty;
        public string WallFinish { get; set; } = string.Empty;
        public string CeilingFinish { get; set; } = string.Empty;
        public string BaseFinish { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public string Notes { get; set; } = string.Empty;
        public int SourceRowNumber { get; set; }
    }
}

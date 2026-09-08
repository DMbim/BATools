namespace BA.RoomClassification.Models
{
    internal sealed class RoomClassificationRecord
    {
        public string RoomKey { get; set; } = string.Empty;
        public string ProgramType { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string RoomFunction { get; set; } = string.Empty;
        public string RoomCode { get; set; } = string.Empty;
        public string RoomGroup { get; set; } = string.Empty;
        public string FinishTier { get; set; } = string.Empty;    // Raw value from Excel, may be blank.
        public string FloorFinish { get; set; } = string.Empty;   // Row-level override on read; resolved value after RoomClassificationFinishResolver runs.
        public string WallFinish { get; set; } = string.Empty;    // Same as above.
        public string CeilingFinish { get; set; } = string.Empty; // Same as above.
        public string BaseFinish { get; set; } = string.Empty;    // Same override/resolved pattern.
        public bool IsActive { get; set; } = true;
        public string Notes { get; set; } = string.Empty;
        public int SourceRowNumber { get; set; }
    }
}

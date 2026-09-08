namespace BA.RoomClassification.Configuration
{
    // Excel column headers for the Matrix sheet. Deliberately decoupled from the Revit shared
    // parameter names in RoomClassificationParameterNames - these are the business-facing column
    // labels in the workbook, not the bound parameter names.
    internal static class RoomClassificationMatrixHeaders
    {
        public const string RoomKey = "BA.Tls_RoomKey";
        public const string ProgramType = "BA.Tls_ProgramType";
        public const string Department = "BA.Tls_Department";
        public const string RoomFunction = "BA.Tls_RoomFunction";
        public const string RoomCode = "BA.Tls_RoomCode";
        public const string RoomGroup = "BA.Tls_RoomGroup";
        public const string FinishTier = "BA.Tls_Finish Tier";
        public const string FloorFinish = "BA.Tls_Floor Finish";
        public const string WallFinish = "BA.Tls_Wall Finish";
        public const string CeilingFinish = "BA.Tls_Ceiling Finish";
        public const string BaseFinish = "BA.Tls_Base Finish";
        public const string IsActive = "IsActive";
        public const string Notes = "Notes";
    }
}

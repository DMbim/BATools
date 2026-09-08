namespace BA.RoomClassification.Configuration
{
    // These are the actual shared parameter NAMES as they exist in BA_SharedParametersWIP2,
    // group "BA_Tools". All eleven are real shared parameters - none of these are Revit
    // built-in parameters. Values here must match the SP file exactly, case included.
    //
    // RoomFinishBase and FinishTier are confirmed names for two NEW shared parameters that must
    // still be created in BA_SharedParametersWIP2.txt before this add-in can bind to them.
    // Once created, copy their real GUIDs into RoomClassificationParameterCatalog, replacing
    // the Guid.Empty placeholders there.
    internal static class RoomClassificationParameterNames
    {
        public const string RoomKey = "BA.Tls_RoomKey";
        public const string ProgramType = "BA.Tls_ProgramType";
        public const string Department = "BA.Tls_Department";
        public const string RoomFunction = "BA.Tls_RoomFunction";
        public const string RoomCode = "BA.Tls_RoomCode";
        public const string RoomGroup = "BA.Tls_RoomGroup";
        public const string FinishTier = "BA.Tls_FinishTier";
        public const string RoomFinishFloor = "BA.Tls_RoomFinish_Floor";
        public const string RoomFinishWall = "BA.Tls_RoomFinish_Wall";
        public const string RoomFinishCeiling = "BA.Tls_RoomFinish_Ceiling";
        public const string RoomFinishBase = "BA.Tls_RoomFinishBase"; // confirmed name, no underscore before "Base"
    }
}

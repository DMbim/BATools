namespace BA.RoomClassification.Configuration
{
    // The shared parameter file path itself comes from BA.Core.Parameters.SharedParamPaths.WIP2
    // (single source of truth for the whole add-in, see that class's doc comment). This class
    // now only owns the group name inside that file that the Rooms classification parameters
    // live under. The add-in temporarily swaps Application.SharedParametersFilename to
    // SharedParamPaths.WIP2 for the duration of the binding pass and restores the user's
    // original setting afterward (finally block). It does not assume every workstation already
    // has this configured as their permanent file.
    internal static class RoomClassificationSharedParameterFileConfig
    {
        public const string GroupName = "BA_Tools";
    }
}

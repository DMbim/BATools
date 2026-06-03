using BA.Settings;


namespace BATools.Zoom.Settings
{
    /// <summary>
    /// Per-user settings for the ZoomToRoom feature family.
    /// Stored at %AppData%\bimBA\Zoom\ZoomToRoomSettings.json
    /// </summary>
    public sealed class ZoomToRoomSettings : AppSettingsBase
    {
        protected override string SubFolder => "Zoom";
        protected override string FileName => "ZoomToRoomSettings.json";

        /// <summary>Name of the last selected RevitLinkInstance.</summary>
        public string? SelectedRevitLinkName { get; set; }

        /// <summary>
        /// Determines which parameter is used as the Room ID lookup key.
        /// Valid values: "BuiltIn" | "ByName" | "Shared"
        /// </summary>
        public string? RoomIdParamMode { get; set; }

        /// <summary>Parameter name when RoomIdParamMode == "ByName".</summary>
        public string? RoomIdName { get; set; }

        /// <summary>Shared parameter GUID string when RoomIdParamMode == "Shared".</summary>
        public string? RoomIdSharedGuid { get; set; }

        /// <summary>Reserved for future BuiltInParameter int value overrides.</summary>
        public int RoomIdBuiltIn { get; set; }

        public static ZoomToRoomSettings Load() => Load<ZoomToRoomSettings>();
    }
}
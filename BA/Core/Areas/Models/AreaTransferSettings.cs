using BA.Settings;

namespace BA.Settings.Rooms
{
    /// <summary>
    /// Configurable parameter names/values for TransferAreaValuesToRoomsCommand.
    /// Defaults match the values that were previously hardcoded as consts in that
    /// command, so existing behavior is unchanged until someone actually opens
    /// the settings window and changes something.
    /// </summary>
    public sealed class AreaTransferSettings : AppSettingsBase
    {
        protected override string SubFolder => "Rooms";
        protected override string FileName => "AreaTransferSettings.json";

        /// <summary>Room parameter holding the room key used to match against area numbers.</summary>
        public string RoomNumberParam { get; set; } = "BA_Number";

        /// <summary>Area parameter holding the area's number (room key + suffix, e.g. "1.2").</summary>
        public string AreaNumberParam { get; set; } = "BA_Number";

        /// <summary>Area parameter holding the area type, compared against AreaTypeUpValue/AreaTypePpValue.</summary>
        public string AreaTypeParam { get; set; } = "BA_Area_Type";

        /// <summary>Room parameter written with the summed UP area.</summary>
        public string RoomAreaUpParam { get; set; } = "BA_Area_UP";

        /// <summary>Room parameter written with the summed PP area.</summary>
        public string RoomAreaPpParam { get; set; } = "BA_Area_PP";

        /// <summary>Value of AreaTypeParam that marks an area as "UP" (case-insensitive match).</summary>
        public string AreaTypeUpValue { get; set; } = "UP";

        /// <summary>Value of AreaTypeParam that marks an area as "PP" (case-insensitive match).</summary>
        public string AreaTypePpValue { get; set; } = "PP";

        public static AreaTransferSettings Load() => Load<AreaTransferSettings>();
    }
}
using BA.Settings;

namespace BA.Settings.Rooms
{
    /// <summary>
    /// Configurable parameters for TransferAreaValuesToRoomsCommand.
    /// Matching is now geometric (point-in-polygon), scoped by Area Scheme
    /// name suffix and shared Level, rather than by a room-number string key.
    /// </summary>
    public sealed class AreaTransferSettings : AppSettingsBase
    {
        protected override string SubFolder => "Rooms";
        protected override string FileName => "AreaTransferSettings.json";

        /// <summary>Room parameter written with the summed UP (Uzitna Plocha) area.</summary>
        public string RoomAreaUpParam { get; set; } = "BA_Area_UP";

        /// <summary>Room parameter written with the summed PP (Podlahova Plocha) area.</summary>
        public string RoomAreaPpParam { get; set; } = "BA_Area_PP";

        /// <summary>
        /// Suffix used to identify an Area Scheme as the UP scheme, matched against
        /// AreaScheme.Name via case-insensitive EndsWith. E.g. matches
        /// "6_UzitnaPlocha_SZ (UP)".
        /// </summary>
        public string AreaSchemeSuffixUp { get; set; } = "(UP)";

        /// <summary>
        /// Suffix used to identify an Area Scheme as the PP scheme, matched against
        /// AreaScheme.Name via case-insensitive EndsWith. E.g. matches
        /// "5_PodlahovaPlocha_366 (PP)".
        /// </summary>
        public string AreaSchemeSuffixPp { get; set; } = "(PP)";

        public static AreaTransferSettings Load() => Load<AreaTransferSettings>();
    }
}
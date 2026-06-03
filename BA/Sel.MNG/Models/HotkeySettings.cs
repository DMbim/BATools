using System.Text.Json.Serialization;

namespace BATools.SelectionManager.Models
{
    public enum ToggleShortcut
    {
        DoubleShift = 0,
        DoubleCtrl = 1,
        DoubleAlt = 2
    }

    public enum FreezeShortcut
    {
        ShiftHold = 0,
        CtrlHold = 1,
        AltHold = 2,
        Disabled = 3
    }

    public class HotkeySettings
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ToggleShortcut Toggle { get; set; } = ToggleShortcut.DoubleShift;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public FreezeShortcut Freeze { get; set; } = FreezeShortcut.ShiftHold;

        /// <summary>Max milliseconds between the two key presses for toggle.</summary>
        public int ToggleWindowMs { get; set; } = 350;

        /// <summary>Milliseconds key must be held to trigger freeze.</summary>
        /// <summary>Toolbar width in pixels. Range 200–400.</summary>
        public int ToolbarWidth { get; set; } = 240;
        public int FreezeHoldMs { get; set; } = 500;
    }
}
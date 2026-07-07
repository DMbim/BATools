using System;

namespace BATools.SelectionManager.Models
{
    public class QuickActionConfig
    {
        public int SlotIndex { get; set; }
        public string ActionId { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string IconResourceKey { get; set; } = string.Empty;
        public string ColorHex { get; set; } = "#4A90D9";
    }
}
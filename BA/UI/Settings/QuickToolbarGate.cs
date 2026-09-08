using System;

namespace BA.Core.Settings
{
    /// <summary>
    /// Runtime on/off switch for the Selection Manager quick toolbar. A plain
    /// static flag rather than routed through the toolbar's own ViewModel,
    /// since the gate needs to be readable from wherever the toolbar gets
    /// triggered to show, without that code needing a reference to the
    /// ViewModel instance itself.
    /// </summary>
    public static class QuickToolbarGate
    {
        public static bool Enabled { get; set; } = true;
    }
}
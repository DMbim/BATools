using System.Collections.Generic;

namespace BA.UI.KeyplanGrid
{
    /// <summary>
    /// Holds transient state for a single zone-label pick session.
    /// Created by BeginZoneLabelSession(), discarded on commit or cancel.
    /// </summary>
    public sealed class KeyplanZoneLabelSession
    {
        public ZonePickState State { get; set; } = ZonePickState.AwaitingFirst;

        public string FirstRegionKey { get; set; }
        public string SecondRegionKey { get; set; }
        public string LastRegionKey { get; set; }

        public KeyplanZoneLabelStyle LabelStyle { get; set; } = KeyplanZoneLabelStyle.Numeric;

        /// <summary>
        /// Populated once State == Ready. Cleared when the session resets.
        /// </summary>
        public IReadOnlyList<KeyplanZoneAssignment> PendingAssignments { get; set; }

        /// <summary>
        /// Returns the ZonePickRole for a given StableKey so the canvas can colour it.
        /// </summary>
        public ZonePickRole GetRole(string stableKey)
        {
            if (string.IsNullOrWhiteSpace(stableKey))
                return ZonePickRole.None;

            if (stableKey == FirstRegionKey) return ZonePickRole.First;
            if (stableKey == SecondRegionKey) return ZonePickRole.Second;
            if (stableKey == LastRegionKey) return ZonePickRole.Last;

            if (PendingAssignments != null)
            {
                foreach (KeyplanZoneAssignment a in PendingAssignments)
                {
                    if (a.StableKey == stableKey)
                        return ZonePickRole.InRange;
                }
            }

            return ZonePickRole.None;
        }
    }
}

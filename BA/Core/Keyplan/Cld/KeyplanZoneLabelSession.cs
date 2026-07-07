using System.Collections.Generic;
using System.Linq;

namespace BA.UI.KeyplanGrid
{
    /// <summary>
    /// Holds transient state for a single zone-label pick session.
    /// Created by BeginZoneLabelSession(), discarded on commit or cancel.
    ///
    /// Sequential click-to-assign model: each click appends a region's
    /// StableKey to PickedKeysInOrder. Clicking an already-picked region
    /// removes it (and shifts subsequent labels down automatically, since
    /// labels are derived from list position).
    /// </summary>
    public sealed class KeyplanZoneLabelSession
    {
        public List<string> PickedKeysInOrder { get; } = new List<string>();

        public KeyplanZoneLabelStyle LabelStyle { get; set; } = KeyplanZoneLabelStyle.Numeric;

        public bool CanCommit => PickedKeysInOrder.Count > 0;

        /// <summary>
        /// Returns the ZonePickRole for a given StableKey so the canvas can colour it.
        /// </summary>
        public ZonePickRole GetRole(string stableKey)
        {
            if (string.IsNullOrWhiteSpace(stableKey))
                return ZonePickRole.None;

            return PickedKeysInOrder.Contains(stableKey)
                ? ZonePickRole.Picked
                : ZonePickRole.None;
        }

        /// <summary>
        /// Returns the live label ("1", "A", "a", etc.) for a given StableKey
        /// based on its current position in PickedKeysInOrder, or empty string
        /// if the region has not been picked.
        /// </summary>
        public string GetLabel(string stableKey)
        {
            if (string.IsNullOrWhiteSpace(stableKey))
                return string.Empty;

            int index = PickedKeysInOrder.IndexOf(stableKey);
            if (index < 0)
                return string.Empty;

            return KeyplanZoneLabelService.GenerateLabel(index, LabelStyle);
        }

        /// <summary>
        /// Handles a region pick: appends if new, removes if already picked.
        /// Returns true if the session state changed.
        /// </summary>
        public bool TogglePick(string stableKey)
        {
            if (string.IsNullOrWhiteSpace(stableKey))
                return false;

            int index = PickedKeysInOrder.IndexOf(stableKey);

            if (index >= 0)
            {
                PickedKeysInOrder.RemoveAt(index);
            }
            else
            {
                PickedKeysInOrder.Add(stableKey);
            }

            return true;
        }

        /// <summary>
        /// Recomputes all labels for the current order. This is a no-op for
        /// state (labels are always derived live via GetLabel), but exists
        /// as an explicit hook for callers that want to force a refresh.
        /// </summary>
        public IReadOnlyList<string> SnapshotOrder() => PickedKeysInOrder.ToList();
    }
}
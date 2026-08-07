// BA/Markup/Models/MarkupNotificationItem.cs
using System;

namespace BA.Markup.Models
{
    /// <summary>
    /// Read-only descriptor for a single markup assigned to the current user.
    /// Populated by MarkupScanService from a BA_DetItem_Markup_RCP instance,
    /// enriched with an IsNew flag by MarkupBaselineService after diffing
    /// against the user's last-seen baseline for this central model.
    /// Consumed directly by MarkupNotificationViewModel / the DataGrid.
    /// </summary>
    public sealed class MarkupNotificationItem
    {
        /// <summary>Revit ElementId of the BA_DetItem_Markup_RCP instance.</summary>
        public long ElementId { get; init; }

        /// <summary>
        /// ElementId of the view the markup instance is placed on.
        /// Required to implement Go to View; a DetailItem's OwnerViewId
        /// gives this directly, no lookup needed at scan time.
        /// </summary>
        public long OwnerViewId { get; init; }

        /// <summary>Display name of the owner view, shown in the grid for context.</summary>
        public string ViewName { get; init; } = string.Empty;

        /// <summary>Value of BA_Tls_AssignedUser at scan time. Always equal to the
        /// current session's Application.Username, since MarkupScanService filters
        /// to the current user before this DTO is constructed. Kept as an explicit
        /// field rather than assumed, in case the filter logic changes later.</summary>
        public string AssignedUser { get; init; } = string.Empty;

        /// <summary>Value of BA_Markup_Author, the person who placed the markup.</summary>
        public string Author { get; init; } = string.Empty;

        /// <summary>Value of BA_Markup_Date, free-form string as written at placement time.</summary>
        public string Date { get; init; } = string.Empty;

        /// <summary>Resolved value of BA_Comments.</summary>
        public string Comments { get; init; } = string.Empty;

        /// <summary>Value of BA_Type, shown for additional context in the grid.</summary>
        public string BaType { get; init; } = string.Empty;

        /// <summary>Current value of BA_Tls_WIP.</summary>
        public bool Wip { get; init; }

        /// <summary>Current value of BA_Tls_Solved. Items with Solved == true are
        /// excluded by MarkupScanService before this DTO is ever constructed;
        /// kept here regardless so the ViewModel can reflect a state change
        /// made via MarkSolvedCommand without a full rescan.</summary>
        public bool Solved { get; init; }

        /// <summary>
        /// True if this item was not present, or had different Wip/Solved state,
        /// in the user's last recorded baseline for this central model.
        /// Set exclusively by MarkupBaselineService; MarkupScanService always
        /// leaves this false, it has no baseline context of its own.
        /// </summary>
        public bool IsNew { get; init; }
    }
}
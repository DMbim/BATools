// BA/Markup/Models/MarkupInputModel.cs
using System.Collections.Generic;

namespace BA.Markup.Models
{
    public enum MarkupMode
    {
        InternalComment,
        OfficialRevision
    }

    /// <summary>
    /// Predefined BA_Type values. Written as the enum name's display string.
    /// </summary>
    public static class MarkupTypeOptions
    {
        public static readonly IReadOnlyList<string> All = new[]
        {
            "General Note",
            "Coordination Issue",
            "Design Change",
            "Client Comment",
            "For Approval",
            "For Information"
        };
    }

    /// <summary>
    /// Predefined BA_Comments action values. The user may also type free text.
    /// </summary>
    public static class MarkupActionOptions
    {
        public static readonly IReadOnlyList<string> All = new[]
        {
            "Move",
            "Delete",
            "Added",
            "Changed",
            "Review",
            "Approve",
            "Reject"
        };
    }

    /// <summary>
    /// Immutable DTO produced by the WPF dialog and consumed by MarkupService.
    /// All string fields are trimmed and non-null by the time this leaves the ViewModel.
    /// </summary>
    public sealed class MarkupInputModel
    {
        public MarkupMode Mode { get; init; }

        /// <summary>Value for BA_Type shared parameter.</summary>
        public string BaType { get; init; } = string.Empty;

        /// <summary>
        /// Resolved value for BA_Comments.
        /// Built by the ViewModel as: action + ": " + freeText, or just one of them.
        /// </summary>
        public string BaComments { get; init; } = string.Empty;

        /// <summary>Author — pre-filled from Revit user, editable.</summary>
        public string BaAuthor { get; init; } = string.Empty;

        /// <summary>Date string — pre-filled with today, editable.</summary>
        public string BaDate { get; init; } = string.Empty;

        /// <summary>Revit Revision element Id (integer). Only meaningful in OfficialRevision mode.</summary>
        public int RevisionElementId { get; init; } = -1;

        /// <summary>Display name of the selected revision — used only for UI confirmation.</summary>
        public string RevisionDisplayName { get; init; } = string.Empty;

        // <- NEW: value for BA_Tls_AssignedUser. Written by MarkupService as a plain
        //    Revit username string. Sourced from the MarkupViewModel's assignee picker,
        //    either selected from MarkupUserRegistryService.GetActiveUsers or typed as
        //    free text for someone not yet in the registry. Empty string means unassigned,
        //    not every markup requires an assignee.
        public string AssignedUser { get; init; } = string.Empty;
    }
}
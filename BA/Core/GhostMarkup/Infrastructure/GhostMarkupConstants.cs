// BA/Core/GhostMarkup/GhostMarkupConstants.cs
using Autodesk.Revit.DB;

namespace BA.Core.GhostMarkup
{
    /// <summary>
    /// Central definitions for the Ghost Markup system. Any Type Name
    /// (TextNoteType, detail Line Style, FamilySymbol or Family name for
    /// Detail Items) beginning with PrefixToken is treated as an internal
    /// annotation that must be hidden before export.
    /// </summary>
    public static class GhostMarkupConstants
    {
        public const string PrefixToken = "BA_NPLT";
        public const string LineStyleName = "BA_NPLT";
        public const string FilterName = "BA_NPLT_Ghost";

        public static readonly Color GhostColor = new Color(255, 0, 255);
    }
}
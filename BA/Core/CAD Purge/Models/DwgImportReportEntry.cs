// File: BA_Tools/CadPurge/Models/DwgImportReportEntry.cs
using Autodesk.Revit.DB;

namespace BA.CadPurge.Models
{
    /// <summary>
    /// One row of the report-only DWG import/link inventory. CAD Purge does not delete or explode
    /// these — the Revit API exposes no supported way to explode a CAD import instance outside the
    /// UI-bound Explode command, and per the current scope this tool only reports so a BIM manager
    /// can act on them manually.
    /// </summary>
    public sealed class DwgImportReportEntry
    {
        public ElementId ElementId { get; set; }
        public string Name { get; set; }
        public bool IsLinked { get; set; }
        public string WorksetName { get; set; }
        public string OwnerViewName { get; set; }

        /// <summary>
        /// Resolved external file path for linked CAD instances (via CADLinkType.GetExternalFileReference()).
        /// Null for imported (embedded, non-linked) DWG geometry, which has no external file reference.
        /// </summary>
        public string LinkedFilePath { get; set; }

        /// <summary>Category display name, kept for UI grouping — always "Imported Categories" (OST_ImportObjectStyles).</summary>
        public string CategoryName { get; set; }
    }
}
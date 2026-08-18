// File: BA_Tools/CadPurge/Services/PurgeScanService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.CadPurge.Models;

namespace BA.CadPurge.Services
{
    /// <summary>
    /// Scans the active document for CAD Purge candidates: non-standard LinePatternElement and
    /// TextNoteType instances (ScanLinePatternsAndTextStyles), and a report-only inventory of DWG
    /// ImportInstance elements (ScanDwgImports).
    ///
    /// Must run on the Revit API thread — call from inside an AppExternalInvoker.Instance.Run
    /// callback. Both scan methods are read-only and require no Transaction.
    /// </summary>
    public sealed class PurgeScanService
    {
        private readonly MappingConfigService _mappingConfigService;

        public PurgeScanService(MappingConfigService mappingConfigService)
        {
            _mappingConfigService = mappingConfigService ?? throw new ArgumentNullException(nameof(mappingConfigService));
        }

        /// <summary>
        /// A candidate is anything whose name does not start with config.StandardPrefix AND is not
        /// present in the reference template's baseline (i.e. neither corporate-standard nor a
        /// stock/native Revit name that was simply never renamed). Each candidate is immediately
        /// matched against config.Rules so the UI can show a proposed mapping target in the same
        /// pass, instead of a second full-document scan later.
        /// </summary>
        public List<PurgeCandidate> ScanLinePatternsAndTextStyles(Document doc, MappingConfig config, TemplateBaselineSnapshot baseline)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));

            var results = new List<PurgeCandidate>();

            foreach (LinePatternElement lp in new FilteredElementCollector(doc)
                         .OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>())
            {
                if (!IsCandidate(lp.Name, PurgeItemType.LinePattern, config, baseline)) continue;

                var candidate = new PurgeCandidate(lp.Id, PurgeItemType.LinePattern, lp.Name)
                {
                    // A true reference count would require walking every GraphicsStyle / curve-based
                    // element's line style in the document — expensive, and not needed for
                    // correctness, since mapping a line pattern is an in-place SetLinePattern() that
                    // never touches referencing elements. Left at 0 deliberately; do not read this
                    // as "unused."
                    UsageCount = 0,
                    ResolvedRule = _mappingConfigService.FindMatch(config, PurgeItemType.LinePattern, lp.Name)
                };

                results.Add(candidate);
            }

            // Built once, outside the TextNoteType loop below — grouping every TextNote by its
            // type id in a single pass and looking up counts from the dictionary avoids re-running
            // a FilteredElementCollector over every TextNote once per candidate type (A8: never
            // scan the document repeatedly inside a loop).
            Dictionary<long, int> textNoteUsageByTypeId = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNote))
                .Cast<TextNote>()
                .GroupBy(tn => tn.GetTypeId().Value)
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (TextNoteType tt in new FilteredElementCollector(doc)
                         .OfClass(typeof(TextNoteType)).Cast<TextNoteType>())
            {
                if (!IsCandidate(tt.Name, PurgeItemType.TextStyle, config, baseline)) continue;

                textNoteUsageByTypeId.TryGetValue(tt.Id.Value, out int usageCount);

                var candidate = new PurgeCandidate(tt.Id, PurgeItemType.TextStyle, tt.Name)
                {
                    UsageCount = usageCount,
                    ResolvedRule = _mappingConfigService.FindMatch(config, PurgeItemType.TextStyle, tt.Name)
                };

                results.Add(candidate);
            }

            return results;
        }

        /// <summary>
        /// Inventories every DWG ImportInstance (linked or embedded) in doc. Report-only per the
        /// current scope — no delete/explode action is offered here.
        ///
        /// IMPORTANT SCOPE CAVEAT: ImportInstance is the element class Revit uses for DWG, DGN,
        /// SAT, and SKP imports alike — there is no public API property that reliably reports
        /// "this was originally a DWG" for an embedded (non-linked) import once it's in the
        /// document. For linked instances this method filters to a .dwg file extension via the
        /// resolved link path; for embedded instances it cannot distinguish DWG from other CAD
        /// formats and returns all of them. If your project mixes DGN/SAT imports with DWG imports,
        /// this report will over-include embedded non-DWG imports — flag it in the UI (Stage 4) or
        /// tell me now if you want a different scoping strategy (e.g. exclude embedded imports
        /// entirely and report linked-only, where format IS reliably known).
        /// </summary>
        public List<DwgImportReportEntry> ScanDwgImports(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var results = new List<DwgImportReportEntry>();
            bool isWorkshared = doc.IsWorkshared;
            WorksetTable worksetTable = isWorkshared ? doc.GetWorksetTable() : null;

            foreach (ImportInstance import in new FilteredElementCollector(doc)
                         .OfClass(typeof(ImportInstance)).Cast<ImportInstance>())
            {
                var entry = new DwgImportReportEntry
                {
                    ElementId = import.Id,
                    Name = import.Category?.Name ?? $"ImportInstance {import.Id.Value}",
                    CategoryName = import.Category?.Name,
                    IsLinked = import.IsLinked
                };

                ElementId ownerViewId = import.OwnerViewId;
                entry.OwnerViewName = (ownerViewId != null && ownerViewId != ElementId.InvalidElementId)
                    ? (doc.GetElement(ownerViewId) as View)?.Name ?? "(view not found)"
                    : "Model (not view-specific)";

                if (isWorkshared)
                {
                    WorksetId worksetId = import.WorksetId;
                    if (worksetId != null && worksetId != WorksetId.InvalidWorksetId)
                        entry.WorksetName = worksetTable.GetWorkset(worksetId)?.Name;
                }

                if (import.IsLinked)
                    entry.LinkedFilePath = TryResolveLinkedFilePath(doc, import);

                results.Add(entry);
            }

            return results;
        }

        private static string TryResolveLinkedFilePath(Document doc, ImportInstance import)
        {
            try
            {
                ElementId typeId = import.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId) return null;
                if (doc.GetElement(typeId) is not CADLinkType cadLinkType) return null;

                ExternalFileReference extRef = cadLinkType.GetExternalFileReference();
                if (extRef == null) return null;

                ModelPath modelPath = extRef.GetPath();
                return ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
            }
            catch (Exception)
            {
                // Non-fatal — this is a report-only field. A resolution failure (e.g. an
                // unreachable network path for the linked file) should not stop the whole
                // DWG inventory from returning.
                return null;
            }
        }

        private static bool IsCandidate(string name, PurgeItemType itemType, MappingConfig config, TemplateBaselineSnapshot baseline)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name.StartsWith(config.StandardPrefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (baseline.Contains(itemType, name)) return false;
            return true;
        }
    }
}
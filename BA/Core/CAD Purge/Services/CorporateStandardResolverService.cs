// File: BA_Tools/CadPurge/Services/CorporateStandardResolverService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.CadPurge.Models;

namespace BA.CadPurge.Services
{
    /// <summary>
    /// Resolves each PurgeCandidate's mapping target: is the corporate-standard element already
    /// in the active document, does it need to be copied in from the reference template, or is it
    /// missing from the template entirely (a config problem to surface, not silently swallow).
    ///
    /// Must run on the Revit API thread. BuildExistingIndex is read-only. ResolveTarget may call
    /// into CorporateTemplateLoader.CopyStandardElement, which requires an active Transaction on
    /// targetDoc whenever a target isn't already present — see that method's own guard.
    /// </summary>
    public sealed class CorporateStandardResolverService
    {
        private readonly CorporateTemplateLoader _templateLoader;

        public CorporateStandardResolverService(CorporateTemplateLoader templateLoader = null)
        {
            _templateLoader = templateLoader ?? new CorporateTemplateLoader();
        }

        /// <summary>
        /// Builds a case-insensitive Name -> ElementId index of every LinePatternElement and
        /// TextNoteType already in targetDoc. Build this ONCE per batch and pass the same instance
        /// into every ResolveTarget call — see ExistingStandardIndex's doc comment for why.
        /// </summary>
        public ExistingStandardIndex BuildExistingIndex(Document targetDoc)
        {
            if (targetDoc == null) throw new ArgumentNullException(nameof(targetDoc));

            var lineIndex = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
            foreach (LinePatternElement lp in new FilteredElementCollector(targetDoc)
                         .OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>())
            {
                if (!string.IsNullOrEmpty(lp.Name))
                    lineIndex[lp.Name] = lp.Id;
            }

            var textIndex = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
            foreach (TextNoteType tt in new FilteredElementCollector(targetDoc)
                         .OfClass(typeof(TextNoteType)).Cast<TextNoteType>())
            {
                if (!string.IsNullOrEmpty(tt.Name))
                    textIndex[tt.Name] = tt.Id;
            }

            return new ExistingStandardIndex(lineIndex, textIndex);
        }

        /// <summary>
        /// Resolves candidate.ResolvedTargetElementId / ResolvedTargetName / TargetSource in place.
        /// If candidate.ResolvedRule is null (no rule in corporate_standards.json matched this
        /// candidate's name), TargetSource is left as Unresolved and nothing else happens — that
        /// candidate simply isn't eligible for a MapToStandard action until the config is updated.
        /// </summary>
        public void ResolveTarget(Document targetDoc, MappingConfig config, ExistingStandardIndex existingIndex, PurgeCandidate candidate)
        {
            if (targetDoc == null) throw new ArgumentNullException(nameof(targetDoc));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (existingIndex == null) throw new ArgumentNullException(nameof(existingIndex));
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));

            if (candidate.ResolvedRule == null)
            {
                candidate.TargetSource = MappingTargetSource.Unresolved;
                return;
            }

            string targetName = candidate.ResolvedRule.TargetName;

            Dictionary<string, ElementId> index = candidate.ItemType switch
            {
                PurgeItemType.LinePattern => existingIndex.LinePatterns,
                PurgeItemType.TextStyle => existingIndex.TextStyles,
                _ => throw new InvalidOperationException(
                    $"CorporateStandardResolverService cannot resolve a target for ItemType '{candidate.ItemType}'.")
            };

            if (index.TryGetValue(targetName, out ElementId existingId))
            {
                candidate.ResolvedTargetElementId = existingId;
                candidate.ResolvedTargetName = targetName;
                candidate.TargetSource = MappingTargetSource.AlreadyInProject;
                return;
            }

            try
            {
                ElementId copiedId = _templateLoader.CopyStandardElement(targetDoc, config.TemplateFilePath, candidate.ItemType, targetName);

                if (copiedId != null && copiedId != ElementId.InvalidElementId)
                {
                    candidate.ResolvedTargetElementId = copiedId;
                    candidate.ResolvedTargetName = targetName;
                    candidate.TargetSource = MappingTargetSource.LoadedFromTemplate;

                    // Keep the index consistent within this batch: if a second candidate this same
                    // run also maps to targetName, it must find it here rather than triggering a
                    // second, redundant CopyStandardElement call.
                    index[targetName] = copiedId;
                    return;
                }
            }
            catch (Exception ex)
            {
                candidate.StatusDetail = $"Failed to load '{targetName}' from template: {ex.Message}";
            }

            candidate.TargetSource = MappingTargetSource.NotFoundInTemplate;
            candidate.ResolvedTargetElementId = ElementId.InvalidElementId;
        }
    }
}
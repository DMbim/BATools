// File: BA_Tools/CadPurge/Services/PurgeBatchExecutor.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.CadPurge.Models;

namespace BA.CadPurge.Services
{
    /// <summary>
    /// Top-level orchestrator for applying a batch of PurgeCandidate actions (Delete or
    /// MapToStandard) to the active document. Each candidate is processed inside its own
    /// SubTransaction within one outer Transaction: a failure on one candidate rolls back only
    /// that candidate's changes and is recorded on the candidate itself, while every other
    /// candidate's successful work is still committed when the outer Transaction commits
    /// (best-effort, per-candidate isolation).
    ///
    /// Must run on the Revit API thread. Call from inside an AppExternalInvoker.Instance.Run
    /// callback, exactly like PurgeScanService.
    /// </summary>
    public sealed class PurgeBatchExecutor
    {
        private readonly CorporateStandardResolverService _resolverService;
        private readonly LinePatternMappingService _linePatternMappingService;
        private readonly TextStyleMappingService _textStyleMappingService;

        public PurgeBatchExecutor(
            CorporateStandardResolverService resolverService,
            LinePatternMappingService linePatternMappingService,
            TextStyleMappingService textStyleMappingService)
        {
            _resolverService = resolverService ?? throw new ArgumentNullException(nameof(resolverService));
            _linePatternMappingService = linePatternMappingService ?? throw new ArgumentNullException(nameof(linePatternMappingService));
            _textStyleMappingService = textStyleMappingService ?? throw new ArgumentNullException(nameof(textStyleMappingService));
        }

        /// <summary>
        /// Applies RequestedAction for every candidate whose RequestedAction is not None.
        /// Candidates with RequestedAction == None are left untouched and counted as Skipped.
        /// </summary>
        public PurgeBatchResult ExecuteBatch(Document doc, MappingConfig config, List<PurgeCandidate> candidates)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));

            var result = new PurgeBatchResult(candidates);

            List<PurgeCandidate> actionable = candidates.Where(c => c.RequestedAction != PurgeAction.None).ToList();
            result.Skipped = candidates.Count - actionable.Count;

            if (actionable.Count == 0)
                return result;

            using (var transaction = new Transaction(doc, "CAD Purge: Apply Batch"))
            {
                transaction.Start();

                var failuresPreprocessor = new CadPurgeFailuresPreprocessor();
                FailureHandlingOptions failureOptions = transaction.GetFailureHandlingOptions();
                failureOptions.SetFailuresPreprocessor(failuresPreprocessor);
                transaction.SetFailureHandlingOptions(failureOptions);

                ExistingStandardIndex existingIndex = _resolverService.BuildExistingIndex(doc);

                foreach (PurgeCandidate candidate in actionable)
                {
                    ProcessCandidate(doc, config, existingIndex, candidate, result, failuresPreprocessor);
                }

                result.Warnings.AddRange(failuresPreprocessor.ResolvedWarnings);

                transaction.Commit();
            }

            AppLogger.LogInfo($"[CadPurge] Batch complete. Succeeded={result.Succeeded}, Failed={result.Failed}, Skipped={result.Skipped}, Warnings={result.Warnings.Count}.");

            return result;
        }

        private void ProcessCandidate(
            Document doc,
            MappingConfig config,
            ExistingStandardIndex existingIndex,
            PurgeCandidate candidate,
            PurgeBatchResult result,
            CadPurgeFailuresPreprocessor failuresPreprocessor)
        {
            int warningsBefore = failuresPreprocessor.ResolvedWarnings.Count;

            using (var subTransaction = new SubTransaction(doc))
            {
                subTransaction.Start();

                try
                {
                    switch (candidate.RequestedAction)
                    {
                        case PurgeAction.Delete:
                            ApplyDelete(doc, candidate);
                            break;

                        case PurgeAction.MapToStandard:
                            ApplyMap(doc, config, existingIndex, candidate);
                            break;

                        default:
                            throw new InvalidOperationException(
                                $"ProcessCandidate called for '{candidate.Name}' with RequestedAction = {candidate.RequestedAction}. Actionable candidates are pre-filtered to Delete/MapToStandard only, so this should never happen.");
                    }

                    subTransaction.Commit();
                    result.Succeeded++;

                    List<string> candidateWarnings = failuresPreprocessor.ResolvedWarnings.Skip(warningsBefore).ToList();
                    if (candidateWarnings.Count > 0)
                    {
                        candidate.StatusDetail = (candidate.StatusDetail ?? string.Empty)
                            + " Warnings during apply: " + string.Join("; ", candidateWarnings);
                    }
                }
                catch (Exception ex)
                {
                    subTransaction.RollBack();

                    // If this candidate's own resolve step copied a new target element in from the
                    // template before a later step failed, that copy is now undone by the rollback.
                    // Remove the stale index entry so a later candidate targeting the same name does
                    // not pick up an ElementId that no longer exists. Only clean up entries THIS
                    // candidate's resolution added (TargetSource == LoadedFromTemplate) — a
                    // candidate that merely reused an already-committed entry from an earlier,
                    // successful candidate must leave that entry alone.
                    if (candidate.TargetSource == MappingTargetSource.LoadedFromTemplate && !string.IsNullOrEmpty(candidate.ResolvedTargetName))
                    {
                        Dictionary<string, ElementId> index = candidate.ItemType == PurgeItemType.LinePattern
                            ? existingIndex.LinePatterns
                            : existingIndex.TextStyles;
                        index.Remove(candidate.ResolvedTargetName);
                    }

                    candidate.Status = PurgeCandidateStatus.ActionFailed;
                    candidate.StatusDetail = ex.Message;
                    result.Failed++;

                    AppLogger.LogError($"CadPurge.ProcessCandidate ('{candidate.Name}', {candidate.ItemType}, {candidate.RequestedAction})", ex);
                }
            }
        }

        private void ApplyDelete(Document doc, PurgeCandidate candidate)
        {
            switch (candidate.ItemType)
            {
                case PurgeItemType.LinePattern:
                    _linePatternMappingService.ApplyDeletion(doc, candidate);
                    break;
                case PurgeItemType.TextStyle:
                    _textStyleMappingService.ApplyDeletion(doc, candidate);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"PurgeBatchExecutor cannot delete ItemType '{candidate.ItemType}'. DwgImport candidates are report-only and should never reach here.");
            }
        }

        private void ApplyMap(Document doc, MappingConfig config, ExistingStandardIndex existingIndex, PurgeCandidate candidate)
        {
            if (candidate.TargetSource != MappingTargetSource.AlreadyInProject)
            {
                _resolverService.ResolveTarget(doc, config, existingIndex, candidate);
            }

            if (candidate.TargetSource == MappingTargetSource.NotFoundInTemplate || candidate.TargetSource == MappingTargetSource.Unresolved)
            {
                throw new InvalidOperationException(
                    $"Cannot map '{candidate.Name}'. Target resolution result: {candidate.TargetSource}. Check corporate_standards.json for a matching rule and a valid targetName present in the reference template.");
            }

            switch (candidate.ItemType)
            {
                case PurgeItemType.LinePattern:
                    _linePatternMappingService.ApplyMapping(doc, candidate);
                    break;
                case PurgeItemType.TextStyle:
                    _textStyleMappingService.ApplyMapping(doc, candidate);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"PurgeBatchExecutor cannot map ItemType '{candidate.ItemType}'. DwgImport candidates are report-only and should never reach here.");
            }
        }
    }
}
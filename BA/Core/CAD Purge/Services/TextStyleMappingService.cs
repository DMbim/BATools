// File: BA_Tools/CadPurge/Services/TextStyleMappingService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.CadPurge.Models;

namespace BA.CadPurge.Services
{
    /// <summary>
    /// Applies Delete and MapToStandard actions to TextStyle PurgeCandidates.
    ///
    /// Mapping a text style means reassigning every TextNote currently using the source
    /// TextNoteType to the resolved target TextNoteType via Element.ChangeTypeId(), then deleting
    /// the now-orphaned source type.
    ///
    /// Every public method here requires an already-open Transaction/SubTransaction on doc
    /// (doc.IsModifiable). This class never opens its own transaction, so PurgeBatchExecutor can
    /// wrap each candidate in its own SubTransaction for best-effort, per-candidate isolation.
    /// </summary>
    public sealed class TextStyleMappingService
    {
        /// <summary>
        /// Reassigns every TextNote using candidate.ElementId to candidate.ResolvedTargetElementId,
        /// then deletes candidate.ElementId. Throws if no valid target was resolved, or if any
        /// TextNote fails to reassign (in which case the source type is deliberately NOT deleted,
        /// so it is never left orphaned while still in use by an element that failed reassignment).
        /// </summary>
        public void ApplyMapping(Document doc, PurgeCandidate candidate)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (candidate.ItemType != PurgeItemType.TextStyle)
                throw new ArgumentException($"TextStyleMappingService cannot process ItemType '{candidate.ItemType}'.", nameof(candidate));
            if (!doc.IsModifiable)
                throw new InvalidOperationException("ApplyMapping requires an active Transaction on doc.");
            if (candidate.TargetSource != MappingTargetSource.AlreadyInProject && candidate.TargetSource != MappingTargetSource.LoadedFromTemplate)
                throw new InvalidOperationException($"Cannot map '{candidate.Name}'. No resolved target (TargetSource = {candidate.TargetSource}). CorporateStandardResolverService.ResolveTarget must succeed before calling ApplyMapping.");
            if (candidate.ResolvedTargetElementId == null || candidate.ResolvedTargetElementId == ElementId.InvalidElementId)
                throw new InvalidOperationException($"Cannot map '{candidate.Name}'. ResolvedTargetElementId is invalid.");

            List<TextNote> textNotesUsingSource = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNote))
                .Cast<TextNote>()
                .Where(tn => tn.GetTypeId() == candidate.ElementId)
                .ToList();

            int reassignedCount = 0;
            var failedElementIds = new List<long>();

            foreach (TextNote textNote in textNotesUsingSource)
            {
                try
                {
                    textNote.ChangeTypeId(candidate.ResolvedTargetElementId);
                    reassignedCount++;
                }
                catch (Exception)
                {
                    failedElementIds.Add(textNote.Id.Value);
                }
            }

            if (failedElementIds.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Failed to reassign {failedElementIds.Count} of {textNotesUsingSource.Count} TextNote element(s) off '{candidate.Name}' (element ids: {string.Join(", ", failedElementIds)}). Aborting this candidate's deletion so the source type is not deleted while still in use.");
            }

            ICollection<ElementId> deletedIds = doc.Delete(candidate.ElementId);

            candidate.Status = PurgeCandidateStatus.ActionApplied;
            candidate.StatusDetail = $"Reassigned {reassignedCount} TextNote element(s) to '{candidate.ResolvedTargetName}'. Deleted '{candidate.Name}' ({(deletedIds?.Count ?? 0)} element(s) removed in total).";
        }

        /// <summary>
        /// Deletes candidate.ElementId directly with no reassignment. If any TextNote still uses
        /// it, Revit's own delete-dependents handling applies. This is the deliberately blunt
        /// action a BIM manager chooses when they know they want the type and its usages gone
        /// entirely.
        /// </summary>
        public void ApplyDeletion(Document doc, PurgeCandidate candidate)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (candidate.ItemType != PurgeItemType.TextStyle)
                throw new ArgumentException($"TextStyleMappingService cannot process ItemType '{candidate.ItemType}'.", nameof(candidate));
            if (!doc.IsModifiable)
                throw new InvalidOperationException("ApplyDeletion requires an active Transaction on doc.");

            ICollection<ElementId> deletedIds = doc.Delete(candidate.ElementId);

            candidate.Status = PurgeCandidateStatus.ActionApplied;
            candidate.StatusDetail = $"Deleted '{candidate.Name}' with no replacement mapping ({(deletedIds?.Count ?? 0)} element(s) removed in total, including any TextNote elements Revit removed automatically).";
        }
    }
}
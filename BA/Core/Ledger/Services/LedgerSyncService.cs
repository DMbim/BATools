using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Settings;

namespace BA.Core.Ledger
{
    /// <summary>
    /// Automatic three-way sync, called from DocumentSynchronizingWithCentral. No manual
    /// "Sync Type Data" button involved: every shared, non-ElementId, user-modifiable Type
    /// Parameter on every tracked FamilySymbol is scanned on every sync.
    ///
    /// Merge base = PersonalLedgerService (this user's last known state per field)
    /// Local      = live value currently on the FamilySymbol in this document
    /// Remote     = LedgerFileService's Main Ledger (shared, network, locked)
    ///
    /// Design decision: this is all-or-nothing per sync attempt with respect to genuine
    /// conflicts only. If ANY field conflicts (local changed AND remote changed to something
    /// different since this user's baseline), nothing is written anywhere and the entire
    /// Synchronize with Central is cancelled via e.Cancel(true) so the user can resolve it
    /// before anything commits. Individual field-level failures that are not conflicts
    /// (missing binding, bad value parse) do NOT cancel the sync, they are skipped, logged,
    /// and retried on the next sync attempt since their Personal Ledger baseline is not
    /// advanced on failure.
    ///
    /// NAME FILTER: only shared parameters whose name starts with "BA_" (case-insensitive)
    /// are ever pushed to or pulled from the Main Ledger. This keeps a shared parameter file
    /// that happens to be shared with other, unrelated tools/parameters from ever leaking into
    /// this ledger's sync scope. Applied on both scan passes, and therefore also acts as a
    /// passive filter against any stray non-"BA_" entries that may already exist in an older
    /// Main Ledger file, they will simply stop being read from this point on, no migration
    /// needed.
    ///
    /// NOTE ON PARAMETER SCOPE: this entire engine reads FamilySymbol.Parameters, which by
    /// Revit API construction can only ever return Type-bound shared parameters -- Instance-
    /// bound parameters live on FamilyInstance, not FamilySymbol/ElementType, and never appear
    /// here. Every binding this service creates is therefore correctly a TypeBinding; this is
    /// not a simplification, it is what the object being scanned makes possible. If Instance
    /// parameter sync is ever added, it needs a separate scan over placed FamilyInstance
    /// elements, a different key structure, and its own binding-kind handling; it does not
    /// belong bolted onto this class.
    /// </summary>
    public static class LedgerSyncService
    {
        private const string TrackedNamePrefix = "BA_";

        internal class FieldCandidate
        {
            public string FamilyTypeKey;
            public string ParamGuidString;
            public string ParameterName;
            public StorageType StorageType;

            // Null when this field is known to the Main Ledger but has never existed locally
            // on this symbol (new-parameter propagation case). ClassifyField never reads
            // LiveValue in the baselineEntry == null / mainEntry != null branch, so this is
            // safe, but it must never be treated as "the live value is the empty string".
            public string LiveValue;
        }

        private class PushItem
        {
            public FieldCandidate Field;
        }

        private class PullItem
        {
            public FieldCandidate Field;
            public LedgerParameterEntry SourceEntry;
        }

        public enum LedgerConflictResolution
        {
            KeepMine,
            AcceptServer,
            CancelSync
        }

        /// <summary>
        /// Public-facing conflict summary passed to the resolver delegate. Field/ServerEntry
        /// are internal-only, used to convert a resolved conflict back into a PushItem or
        /// PullItem; the resolver itself only needs the display properties above them.
        /// </summary>
        public class LedgerConflictItem
        {
            public string FamilyTypeKey { get; internal set; }
            public string ParameterName { get; internal set; }
            public string LocalValue { get; internal set; }
            public string ServerValue { get; internal set; }
            public string ServerEditedBy { get; internal set; }
            public DateTime ServerTimestampUtc { get; internal set; }

            internal FieldCandidate Field;
            internal LedgerParameterEntry ServerEntry;
        }

        /// <summary>
        /// Public-facing summary of a pull that failed specifically because the shared
        /// parameter binding could not be resolved or created in this document (as opposed to
        /// a bad value parse, which is logged but not surfaced to the user since it indicates
        /// ledger data corruption rather than an environment issue the user can fix).
        /// </summary>
        public class LedgerBindingFailure
        {
            public string FamilyTypeKey { get; internal set; }
            public string ParameterName { get; internal set; }
            public string Reason { get; internal set; }
        }

        private enum ApplyOutcome
        {
            Applied,
            BindingFailed,
            ValueRejected
        }

        /// <summary>
        /// Runs the full scan + resolve + commit cycle. Returns true if the caller's sync
        /// should proceed, false if it must be cancelled. The caller (BaApplication's event
        /// handler) is responsible for calling e.Cancel() when this returns false; this method
        /// does not touch the event args itself so it stays independently testable.
        ///
        /// resolveConflicts is invoked ONLY if at least one genuine conflict is found (local
        /// and remote both changed, to different values, since this user's baseline). It is
        /// the caller's responsibility to show a TaskDialog (or equivalent) with all listed
        /// conflicts and return a single uniform resolution for all of them.
        ///
        /// warnBindingFailures is invoked ONLY if at least one pull failed because a shared
        /// parameter binding could not be resolved or created in this document (parameter GUID
        /// not found in this session's shared parameter file, or Insert/ReInsert rejected by
        /// Revit). This does NOT cancel the sync; every other successful push/pull in this
        /// batch still commits. It exists purely to tell the user that a newly published Type
        /// Parameter did not make it into this document and why, since the alternative is a
        /// silent retry-forever with no visible symptom. Optional; pass null to suppress (not
        /// recommended, since these failures are otherwise invisible).
        /// </summary>
        public static bool Run(
            Document doc,
            Func<List<LedgerConflictItem>, LedgerConflictResolution> resolveConflicts,
            Action<List<LedgerBindingFailure>> warnBindingFailures,
            out string cancelReason)
        {
            cancelReason = null;

            if (doc == null || doc.IsReadOnly)
            {
                return true;
            }

            // Per-central kill switch. OFF is the default for every model (no entity yet
            // resolves to disabled), so a document must have had this explicitly turned on
            // via the Ledger Settings window before any read/write against either ledger file
            // happens. When off, the real Synchronize with Central is never touched, this step
            // is simply skipped entirely.
            if (!LedgerEnabledService.IsEnabled(doc))
            {
                return true;
            }

            LedgerSettings settings = LedgerSettings.Load();
            string currentUser = Environment.UserName;

            TypeDataLedger mainLedger;
            try
            {
                mainLedger = LedgerFileService.ReadOnly(doc);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("LedgerSyncService.Run: could not read Main Ledger, skipping this sync's ledger step", ex);
                return true; // do not block the user's real sync over a ledger read failure
            }

            TypeDataLedger personalLedger = PersonalLedgerService.Load(doc);

            Dictionary<string, FamilySymbol> symbolsByKey = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .GroupBy(s => LedgerFileService.BuildKey(s.Family.Name, s.Name))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var pushes = new List<PushItem>();
            var pulls = new List<PullItem>();
            var conflicts = new List<LedgerConflictItem>();
            int skippedForPrefix = 0;

            // ---- Scan phase: read-only, no writes ----
            foreach (KeyValuePair<string, FamilySymbol> kvp in symbolsByKey)
            {
                string familyTypeKey = kvp.Key;
                FamilySymbol symbol = kvp.Value;

                long categoryIdValue = symbol.Category?.Id.Value ?? 0;
                if (!settings.IsCategoryAllowed(categoryIdValue))
                {
                    continue;
                }

                mainLedger.Families.TryGetValue(familyTypeKey, out LedgerFamilyNode mainNode);
                personalLedger.Families.TryGetValue(familyTypeKey, out LedgerFamilyNode personalNode);

                var processedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Pass 1: parameters that already exist and are bound on this symbol.
                foreach (Parameter parameter in symbol.Parameters)
                {
                    if (!parameter.IsShared || parameter.IsReadOnly)
                    {
                        continue;
                    }

                    if (parameter.StorageType == StorageType.ElementId || parameter.StorageType == StorageType.None)
                    {
                        continue; // document-local, cannot round-trip across 5 models
                    }

                    if (!IsTrackedParameterName(parameter.Definition.Name))
                    {
                        skippedForPrefix++;
                        continue;
                    }

                    Guid guid = parameter.GUID;
                    if (guid == Guid.Empty)
                    {
                        continue;
                    }

                    string guidString = guid.ToString("D");
                    processedGuids.Add(guidString);

                    string liveValue = ExtractValueAsString(parameter);

                    var field = new FieldCandidate
                    {
                        FamilyTypeKey = familyTypeKey,
                        ParamGuidString = guidString,
                        ParameterName = parameter.Definition.Name,
                        StorageType = parameter.StorageType,
                        LiveValue = liveValue
                    };

                    LedgerParameterEntry mainEntry = null;
                    mainNode?.Parameters.TryGetValue(guidString, out mainEntry);

                    LedgerParameterEntry baselineEntry = null;
                    personalNode?.Parameters.TryGetValue(guidString, out baselineEntry);

                    ClassifyField(field, mainEntry, baselineEntry, pushes, pulls, conflicts);
                }

                // Pass 2: parameters the Main Ledger knows about for this family/type that are
                // NOT currently bound on this symbol at all, and that this user has never
                // synced before (no Personal Ledger baseline). This is the case that was
                // previously unreachable: User A binds a brand-new shared parameter and pushes
                // it, Building B has never heard of it, symbol.Parameters can't see it because
                // it isn't bound yet. Synthesize a candidate straight from the ledger entry so
                // it reaches ApplyParameter, which is what actually calls
                // ParameterBindingFixupService.
                //
                // Only fires when baselineEntry is null: if a baseline exists but the
                // parameter is now absent from the symbol, that means it was bound before and
                // something unbound it locally since -- a different situation this pass does
                // not attempt to resolve, to avoid pushing a null/empty value over a
                // deliberate local unbind.
                if (mainNode != null)
                {
                    foreach (KeyValuePair<string, LedgerParameterEntry> kvp2 in mainNode.Parameters)
                    {
                        string guidString = kvp2.Key;
                        if (processedGuids.Contains(guidString))
                        {
                            continue;
                        }

                        LedgerParameterEntry mainEntry = kvp2.Value;

                        if (!IsTrackedParameterName(mainEntry.ParameterName))
                        {
                            skippedForPrefix++;
                            continue;
                        }

                        LedgerParameterEntry baselineEntry = null;
                        personalNode?.Parameters.TryGetValue(guidString, out baselineEntry);

                        if (baselineEntry != null)
                        {
                            continue; // previously bound, now missing locally: not this pass's job
                        }

                        if (!Enum.TryParse(mainEntry.StorageType, out StorageType storageType)
                            || storageType == StorageType.ElementId
                            || storageType == StorageType.None)
                        {
                            AppLogger.LogError(
                                $"LedgerSyncService.Run: Main Ledger entry for '{familyTypeKey}' / '{mainEntry.ParameterName}' has invalid StorageType '{mainEntry.StorageType}', skipping.", null);
                            continue;
                        }

                        var field = new FieldCandidate
                        {
                            FamilyTypeKey = familyTypeKey,
                            ParamGuidString = guidString,
                            ParameterName = mainEntry.ParameterName,
                            StorageType = storageType,
                            LiveValue = null
                        };

                        ClassifyField(field, mainEntry, null, pushes, pulls, conflicts);
                    }
                }
            }

            // ---- Conflict gate: ask the caller how to resolve, uniformly, if anything conflicts ----
            if (conflicts.Count > 0)
            {
                LedgerConflictResolution resolution = resolveConflicts != null
                    ? resolveConflicts(conflicts)
                    : LedgerConflictResolution.CancelSync;

                AppLogger.LogInfo($"LedgerSyncService.Run: {conflicts.Count} conflict(s) detected, resolution = {resolution}.");

                switch (resolution)
                {
                    case LedgerConflictResolution.KeepMine:
                        foreach (LedgerConflictItem conflict in conflicts)
                        {
                            pushes.Add(new PushItem { Field = conflict.Field });
                        }
                        break;

                    case LedgerConflictResolution.AcceptServer:
                        foreach (LedgerConflictItem conflict in conflicts)
                        {
                            pulls.Add(new PullItem { Field = conflict.Field, SourceEntry = conflict.ServerEntry });
                        }
                        break;

                    default:
                        cancelReason = BuildConflictMessage(conflicts);
                        return false;
                }
            }

            if (pushes.Count == 0 && pulls.Count == 0)
            {
                if (skippedForPrefix > 0)
                {
                    AppLogger.LogInfo($"LedgerSyncService.Run: {skippedForPrefix} field occurrence(s) skipped, parameter name did not start with '{TrackedNamePrefix}'.");
                }
                return true; // nothing to do, let the real sync proceed untouched
            }

            // ---- Commit phase: only reached when there were zero conflicts (or all resolved) ----
            DateTime commitTimestamp = DateTime.UtcNow;
            var bindingFailures = new List<LedgerBindingFailure>();

            if (pushes.Count > 0)
            {
                LedgerFileService.OpenAndModify(doc, ledger =>
                {
                    foreach (PushItem push in pushes)
                    {
                        if (!ledger.Families.TryGetValue(push.Field.FamilyTypeKey, out LedgerFamilyNode node))
                        {
                            node = new LedgerFamilyNode();
                            ledger.Families[push.Field.FamilyTypeKey] = node;
                        }

                        node.Parameters[push.Field.ParamGuidString] = new LedgerParameterEntry
                        {
                            ParameterName = push.Field.ParameterName,
                            Value = push.Field.LiveValue,
                            StorageType = push.Field.StorageType.ToString(),
                            TimestampUtc = commitTimestamp,
                            LastEditedBy = currentUser
                        };
                    }
                    return true;
                });
            }

            if (pulls.Count > 0)
            {
                using (var tx = new Transaction(doc, "Sync Ledger Type Data"))
                {
                    tx.Start();

                    foreach (PullItem pull in pulls)
                    {
                        if (symbolsByKey.TryGetValue(pull.Field.FamilyTypeKey, out FamilySymbol symbol))
                        {
                            try
                            {
                                ApplyOutcome outcome = ApplyParameter(doc, symbol, pull.Field.ParamGuidString, pull.SourceEntry, out string bindingFailureDetail);

                                switch (outcome)
                                {
                                    case ApplyOutcome.Applied:
                                        break;

                                    case ApplyOutcome.BindingFailed:
                                        AppLogger.LogError(
                                            $"LedgerSyncService.Run: could not bind/resolve shared parameter '{pull.Field.ParameterName}' ({pull.Field.ParamGuidString}) on '{pull.Field.FamilyTypeKey}' in this document. {bindingFailureDetail}", null);
                                        bindingFailures.Add(new LedgerBindingFailure
                                        {
                                            FamilyTypeKey = pull.Field.FamilyTypeKey,
                                            ParameterName = pull.Field.ParameterName,
                                            Reason = bindingFailureDetail ?? "Shared parameter binding could not be found or created in this document."
                                        });
                                        pull.SourceEntry = null;
                                        break;

                                    case ApplyOutcome.ValueRejected:
                                        AppLogger.LogError(
                                            $"LedgerSyncService.Run: ApplyParameter rejected value for '{pull.Field.ParameterName}' on '{pull.Field.FamilyTypeKey}' (bad value or read-only parameter).", null);
                                        pull.SourceEntry = null;
                                        break;
                                }
                            }
                            catch (Exception ex)
                            {
                                AppLogger.LogError(
                                    $"LedgerSyncService.Run: failed to apply pull for '{pull.Field.ParameterName}' on '{pull.Field.FamilyTypeKey}'", ex);
                                pull.SourceEntry = null;
                            }
                        }
                    }

                    tx.Commit();
                }
            }

            // ---- Update this user's baseline for everything that actually succeeded ----
            foreach (PushItem push in pushes)
            {
                SetPersonalBaseline(personalLedger, push.Field.FamilyTypeKey, push.Field.ParamGuidString, new LedgerParameterEntry
                {
                    ParameterName = push.Field.ParameterName,
                    Value = push.Field.LiveValue,
                    StorageType = push.Field.StorageType.ToString(),
                    TimestampUtc = commitTimestamp,
                    LastEditedBy = currentUser
                });
            }

            foreach (PullItem pull in pulls)
            {
                if (pull.SourceEntry != null) // null means the apply failed above, don't advance baseline
                {
                    SetPersonalBaseline(personalLedger, pull.Field.FamilyTypeKey, pull.Field.ParamGuidString, pull.SourceEntry);
                }
            }

            PersonalLedgerService.Save(doc, personalLedger);

            AppLogger.LogInfo($"LedgerSyncService.Run: {pushes.Count} field(s) pushed, {pulls.Count(p => p.SourceEntry != null)} field(s) pulled.");

            if (skippedForPrefix > 0)
            {
                AppLogger.LogInfo($"LedgerSyncService.Run: {skippedForPrefix} field occurrence(s) skipped, parameter name did not start with '{TrackedNamePrefix}'.");
            }

            if (bindingFailures.Count > 0)
            {
                AppLogger.LogInfo($"LedgerSyncService.Run: {bindingFailures.Count} field(s) could not be bound in this document, see errors above.");
                warnBindingFailures?.Invoke(bindingFailures);
            }

            return true;
        }

        private static bool IsTrackedParameterName(string parameterName)
        {
            return !string.IsNullOrEmpty(parameterName)
                && parameterName.StartsWith(TrackedNamePrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static void ClassifyField(
            FieldCandidate field,
            LedgerParameterEntry mainEntry,
            LedgerParameterEntry baselineEntry,
            List<PushItem> pushes,
            List<PullItem> pulls,
            List<LedgerConflictItem> conflicts)
        {
            if (baselineEntry == null)
            {
                if (mainEntry == null)
                {
                    // Bootstrap: nobody has ever published this field. This user's live value
                    // becomes the seed. Only reachable from pass 1 (a live parameter exists);
                    // pass 2 always has a non-null mainEntry by construction.
                    pushes.Add(new PushItem { Field = field });
                }
                else
                {
                    // This user has no history with this field, but the Main Ledger does.
                    // Covers both: (a) pass 1, live value happens to already match or differ,
                    // baseline must still be seeded either way, and (b) pass 2, the parameter
                    // doesn't exist locally at all yet and must be bound + set from scratch.
                    pulls.Add(new PullItem { Field = field, SourceEntry = mainEntry });
                }
                return;
            }

            bool localChanged = !string.Equals(field.LiveValue, baselineEntry.Value, StringComparison.Ordinal);

            if (!localChanged)
            {
                // No local edit. Check whether the remote moved since our baseline.
                if (mainEntry != null && !string.Equals(mainEntry.Value, baselineEntry.Value, StringComparison.Ordinal))
                {
                    pulls.Add(new PullItem { Field = field, SourceEntry = mainEntry });
                }
                return;
            }

            // Local changed. Compare against remote.
            if (mainEntry == null || string.Equals(mainEntry.Value, baselineEntry.Value, StringComparison.Ordinal))
            {
                // Remote didn't move since our baseline (or doesn't exist yet). Clean push.
                pushes.Add(new PushItem { Field = field });
            }
            else if (string.Equals(mainEntry.Value, field.LiveValue, StringComparison.Ordinal))
            {
                // Remote already has the exact value we were about to push. Not a conflict,
                // just adopt it as our new baseline silently.
                pulls.Add(new PullItem { Field = field, SourceEntry = mainEntry });
            }
            else
            {
                // Both sides changed, to different values. Genuine conflict.
                conflicts.Add(new LedgerConflictItem
                {
                    FamilyTypeKey = field.FamilyTypeKey,
                    ParameterName = field.ParameterName,
                    LocalValue = field.LiveValue,
                    ServerValue = mainEntry.Value,
                    ServerEditedBy = mainEntry.LastEditedBy,
                    ServerTimestampUtc = mainEntry.TimestampUtc,
                    Field = field,
                    ServerEntry = mainEntry
                });
            }
        }

        private static void SetPersonalBaseline(TypeDataLedger personalLedger, string familyTypeKey, string paramGuidString, LedgerParameterEntry entry)
        {
            if (!personalLedger.Families.TryGetValue(familyTypeKey, out LedgerFamilyNode node))
            {
                node = new LedgerFamilyNode();
                personalLedger.Families[familyTypeKey] = node;
            }

            node.Parameters[paramGuidString] = entry;
        }

        private static ApplyOutcome ApplyParameter(Document doc, FamilySymbol symbol, string paramGuidString, LedgerParameterEntry entry, out string bindingFailureDetail)
        {
            bindingFailureDetail = null;

            if (!Guid.TryParse(paramGuidString, out Guid targetGuid))
            {
                return ApplyOutcome.ValueRejected;
            }

            Parameter target = FindSharedParameter(symbol, targetGuid);

            if (target == null)
            {
                bool fixedUp = ParameterBindingFixupService.EnsureParameterBound(doc, symbol.Category, targetGuid, out bindingFailureDetail);
                if (fixedUp)
                {
                    target = FindSharedParameter(symbol, targetGuid);
                    bindingFailureDetail = null;
                }

                if (target == null)
                {
                    if (bindingFailureDetail == null)
                    {
                        bindingFailureDetail = "Binding fix-up reported success but the parameter still could not be found on this symbol after Regenerate().";
                    }
                    return ApplyOutcome.BindingFailed;
                }
            }

            if (target.IsReadOnly)
            {
                return ApplyOutcome.ValueRejected;
            }

            switch (target.StorageType)
            {
                case StorageType.String:
                    return target.Set(entry.Value ?? string.Empty) ? ApplyOutcome.Applied : ApplyOutcome.ValueRejected;

                case StorageType.Integer:
                    if (int.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
                    {
                        return target.Set(intVal) ? ApplyOutcome.Applied : ApplyOutcome.ValueRejected;
                    }
                    return ApplyOutcome.ValueRejected;

                case StorageType.Double:
                    if (double.TryParse(entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double dblVal))
                    {
                        return target.Set(dblVal) ? ApplyOutcome.Applied : ApplyOutcome.ValueRejected;
                    }
                    return ApplyOutcome.ValueRejected;

                default:
                    return ApplyOutcome.ValueRejected;
            }
        }
        private static Parameter FindSharedParameter(FamilySymbol symbol, Guid targetGuid)
        {
            return symbol.Parameters
                .Cast<Parameter>()
                .FirstOrDefault(p => p.IsShared && p.GUID == targetGuid);
        }

        private static string ExtractValueAsString(Parameter parameter)
        {
            switch (parameter.StorageType)
            {
                case StorageType.String:
                    return parameter.AsString() ?? string.Empty;
                case StorageType.Integer:
                    return parameter.AsInteger().ToString(CultureInfo.InvariantCulture);
                case StorageType.Double:
                    return parameter.AsDouble().ToString("R", CultureInfo.InvariantCulture);
                default:
                    throw new InvalidOperationException($"Unsupported StorageType '{parameter.StorageType}' reached ExtractValueAsString.");
            }
        }

        private static string BuildConflictMessage(List<LedgerConflictItem> conflicts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Synchronize with Central was cancelled because the following Type Parameters were changed by someone else since your last sync:");
            sb.AppendLine();

            foreach (LedgerConflictItem c in conflicts)
            {
                sb.AppendLine($"- {c.FamilyTypeKey} / {c.ParameterName}:");
                sb.AppendLine($"    Your value: '{c.LocalValue}'");
                sb.AppendLine($"    Server value: '{c.ServerValue}' (by {c.ServerEditedBy} at {c.ServerTimestampUtc:u})");
            }

            sb.AppendLine();
            sb.AppendLine("Resolve these values on the affected Types (match the server value, or re-confirm your own) and try Synchronize with Central again.");

            return sb.ToString();
        }
    }
}
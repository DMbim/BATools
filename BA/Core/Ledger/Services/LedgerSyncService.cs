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
    /// Design decision: this is all-or-nothing per sync attempt. The whole document is scanned
    /// read-only first with zero writes. If ANY field conflicts (local changed AND remote
    /// changed to something different since this user's baseline), nothing is written anywhere
    /// -- not the Main Ledger, not the Personal Ledger, not the document -- and the entire
    /// Synchronize with Central is cancelled via e.Cancel(true) so the user can resolve it
    /// before anything commits. Only when there are zero conflicts do pushes, pulls, and both
    /// ledger baselines get written, atomically as one batch.
    /// </summary>
    public static class LedgerSyncService
    {
        private class FieldCandidate
        {
            public string FamilyTypeKey;
            public string ParamGuidString;
            public string ParameterName;
            public StorageType StorageType;
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

        private class ConflictItem
        {
            public string FamilyTypeKey;
            public string ParameterName;
            public string LocalValue;
            public string ServerValue;
            public string ServerEditedBy;
            public DateTime ServerTimestampUtc;
        }

        /// <summary>
        /// Runs the full scan + (cancel on conflict) or (push+pull+commit) cycle.
        /// Returns true if the caller's sync should proceed, false if it must be cancelled.
        /// The caller (BaApplication's event handler) is responsible for calling e.Cancel(true)
        /// when this returns false; this method does not touch the event args itself so it
        /// stays independently testable.
        /// </summary>
        public static bool Run(Document doc, out string cancelReason)
        {
            cancelReason = null;

            if (doc == null || doc.IsReadOnly)
            {
                return true;
            }

            LedgerSettings settings = LedgerSettings.Load();
            string currentUser = Environment.UserName;

            TypeDataLedger mainLedger;
            try
            {
                mainLedger = LedgerFileService.ReadOnly();
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
            var conflicts = new List<ConflictItem>();

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

                    Guid guid = parameter.GUID;
                    if (guid == Guid.Empty)
                    {
                        continue;
                    }

                    string guidString = guid.ToString("D");
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
            }

            // ---- Conflict gate: all-or-nothing ----
            if (conflicts.Count > 0)
            {
                cancelReason = BuildConflictMessage(conflicts);
                AppLogger.LogInfo($"LedgerSyncService.Run: {conflicts.Count} conflict(s) detected, cancelling sync. {cancelReason}");
                return false;
            }

            if (pushes.Count == 0 && pulls.Count == 0)
            {
                return true; // nothing to do, let the real sync proceed untouched
            }

            // ---- Commit phase: only reached when there were zero conflicts ----
            DateTime commitTimestamp = DateTime.UtcNow;

            if (pushes.Count > 0)
            {
                LedgerFileService.OpenAndModify(ledger =>
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
                                bool applied = ApplyParameter(doc, symbol, pull.Field.ParamGuidString, pull.SourceEntry);
                                if (!applied)
                                {
                                    // No exception, but nothing was actually written (parse
                                    // failure, missing/read-only parameter). Must be treated
                                    // as a failure, not left to silently advance the baseline
                                    // for a value that was never applied to the document.
                                    AppLogger.LogError(
                                        $"LedgerSyncService.Run: ApplyParameter returned false for '{pull.Field.ParameterName}' on '{pull.Field.FamilyTypeKey}' (bad value or missing parameter)", null);
                                    pull.SourceEntry = null;
                                }
                            }
                            catch (Exception ex)
                            {
                                // Should be rare here since conflicts were already ruled out,
                                // but a type could still be locked by another user at the exact
                                // moment of commit. Log and continue; it will be re-evaluated
                                // fresh on the next sync attempt since the Personal Ledger
                                // baseline for this field is not updated below on failure.
                                AppLogger.LogError(
                                    $"LedgerSyncService.Run: failed to apply pull for '{pull.Field.ParameterName}' on '{pull.Field.FamilyTypeKey}'", ex);
                                pull.SourceEntry = null; // marks this pull as not-applied, see baseline update below
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

            return true;
        }

        private static void ClassifyField(
            FieldCandidate field,
            LedgerParameterEntry mainEntry,
            LedgerParameterEntry baselineEntry,
            List<PushItem> pushes,
            List<PullItem> pulls,
            List<ConflictItem> conflicts)
        {
            if (baselineEntry == null)
            {
                if (mainEntry == null)
                {
                    // Bootstrap: nobody has ever published this field. This user's live value
                    // becomes the seed.
                    pushes.Add(new PushItem { Field = field });
                }
                else
                {
                    // This user has no history with this field, but the Main Ledger does.
                    // Whether or not it already matches the live value, this field's baseline
                    // must still be recorded -- otherwise, if it happens to already match,
                    // nothing gets written anywhere (no push, no pull), the Personal Ledger
                    // never actually gets created for this field, and every future sync
                    // re-derives the exact same "nothing to do" conclusion forever. Treating
                    // this as a pull (even when it's a same-value no-op Set) guarantees the
                    // baseline always gets seeded on first encounter.
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
                conflicts.Add(new ConflictItem
                {
                    FamilyTypeKey = field.FamilyTypeKey,
                    ParameterName = field.ParameterName,
                    LocalValue = field.LiveValue,
                    ServerValue = mainEntry.Value,
                    ServerEditedBy = mainEntry.LastEditedBy,
                    ServerTimestampUtc = mainEntry.TimestampUtc
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

        private static bool ApplyParameter(Document doc, FamilySymbol symbol, string paramGuidString, LedgerParameterEntry entry)
        {
            if (!Guid.TryParse(paramGuidString, out Guid targetGuid))
            {
                return false;
            }

            Parameter target = FindSharedParameter(symbol, targetGuid);

            if (target == null)
            {
                // Not found on this symbol at all: either the parameter isn't bound to this
                // category, or isn't bound anywhere in the project. Attempt a silent fix-up,
                // then retry the lookup once. Regenerate() inside the fix-up service ensures
                // the symbol's Parameters collection reflects the new/widened binding before
                // this retry runs.
                bool fixedUp = ParameterBindingFixupService.EnsureParameterBound(doc, symbol.Category, targetGuid);
                if (fixedUp)
                {
                    target = FindSharedParameter(symbol, targetGuid);
                }
            }

            if (target == null || target.IsReadOnly)
            {
                return false;
            }

            switch (target.StorageType)
            {
                case StorageType.String:
                    return target.Set(entry.Value ?? string.Empty);

                case StorageType.Integer:
                    if (int.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
                    {
                        return target.Set(intVal);
                    }
                    return false;

                case StorageType.Double:
                    if (double.TryParse(entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double dblVal))
                    {
                        return target.Set(dblVal);
                    }
                    return false;

                default:
                    return false;
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

        private static string BuildConflictMessage(List<ConflictItem> conflicts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Synchronize with Central was cancelled because the following Type Parameters were changed by someone else since your last sync:");
            sb.AppendLine();

            foreach (ConflictItem c in conflicts)
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

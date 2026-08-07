using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.Settings;

namespace BA.Core.Ledger
{
    public class CategoryOption
    {
        public long Id { get; set; }
        public string Name { get; set; }
    }

    public class PendingLedgerItem
    {
        public string FamilyTypeKey { get; set; }
        public string ParameterName { get; set; }
        public string Value { get; set; }
        public string LastEditedBy { get; set; }
        public DateTime TimestampUtc { get; set; }
    }

    public class LedgerDiagnosticsResult
    {
        public int TrackedFamiliesCount { get; set; }
        public DateTime? LastSyncUtc { get; set; }
        public List<PendingLedgerItem> PendingItems { get; set; } = new List<PendingLedgerItem>();
        public List<CategoryOption> AvailableCategories { get; set; } = new List<CategoryOption>();
        public string CurrentCentralIdentifier { get; set; }

        /// <summary>
        /// The Project Set this document currently resolves to (auto-detected from the
        /// central's file path, or a manual override if one is set), or null if neither
        /// resolved and this document is using the legacy fallback ledger.
        /// </summary>
        public string CurrentProjectSetName { get; set; }

        /// <summary>
        /// The actual physical ledger file path this document is currently reading/writing,
        /// shown so the user can visually confirm which project set's file they're synced
        /// against. Purely informational; LedgerFileService resolves this independently on
        /// every call, this is not cached or authoritative.
        /// </summary>
        public string ResolvedLedgerFilePath { get; set; }

        /// <summary>
        /// Whether Type Data Ledger sync is currently turned on for this central. Per-central,
        /// stored via LedgerEnabledService; defaults to false (off) for a central that has
        /// never had this explicitly set.
        /// </summary>
        public bool LedgerEnabled { get; set; }
    }

    /// <summary>
    /// Must only be called from a valid Revit API execution context (IExternalCommand.Execute,
    /// IExternalEventHandler.Execute, or Idling). Never call from WPF code-behind directly.
    /// </summary>
    public static class LedgerDiagnosticsService
    {
        public static LedgerDiagnosticsResult Compute(Document doc)
        {
            var result = new LedgerDiagnosticsResult();

            if (doc == null)
            {
                return result;
            }

            result.CurrentCentralIdentifier = CentralIdentifierService.GetIdentifier(doc);
            result.CurrentProjectSetName = ProjectSetService.GetProjectSetName(doc);
            result.ResolvedLedgerFilePath = LedgerFileService.ResolveLedgerPathForDocument(doc);
            result.LedgerEnabled = LedgerEnabledService.IsEnabled(doc);

            // Categories actually used by loaded families in this document, not the full
            // system category list. This is what a filter checkbox list should show, since
            // filtering categories that have no families loaded is meaningless.
            result.AvailableCategories = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Select(f => f.FamilyCategory)
                .Where(c => c != null)
                .GroupBy(c => c.Id.Value)
                .Select(g => new CategoryOption { Id = g.Key, Name = g.First().Name })
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            TypeDataLedger ledger;
            try
            {
                ledger = LedgerFileService.ReadOnly(doc);
            }
            catch (Exception)
            {
                // Ledger unreachable right now (locked or path invalid). Return what we have
                // (category list, resolved path, enabled flag) rather than throwing out of a
                // diagnostics refresh.
                return result;
            }

            result.TrackedFamiliesCount = ledger.Families.Count;

            LedgerSettings settings = LedgerSettings.Load();
            TypeDataLedger personalLedger = PersonalLedgerService.Load(doc);

            Dictionary<string, FamilySymbol> symbolsByKey = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .GroupBy(s => LedgerFileService.BuildKey(s.Family.Name, s.Name))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            long maxBaselineTicks = 0;

            foreach (KeyValuePair<string, LedgerFamilyNode> familyEntry in ledger.Families)
            {
                if (!symbolsByKey.TryGetValue(familyEntry.Key, out FamilySymbol symbol))
                {
                    continue;
                }

                long categoryIdValue = symbol.Category?.Id.Value ?? 0;
                if (!settings.IsCategoryAllowed(categoryIdValue))
                {
                    continue;
                }

                personalLedger.Families.TryGetValue(familyEntry.Key, out LedgerFamilyNode personalNode);

                foreach (KeyValuePair<string, LedgerParameterEntry> paramEntry in familyEntry.Value.Parameters)
                {
                    LedgerParameterEntry baselineEntry = null;
                    personalNode?.Parameters.TryGetValue(paramEntry.Key, out baselineEntry);

                    if (baselineEntry != null && baselineEntry.TimestampUtc.Ticks > maxBaselineTicks)
                    {
                        maxBaselineTicks = baselineEntry.TimestampUtc.Ticks;
                    }

                    // Pending = this user's baseline doesn't yet match the Main Ledger for this
                    // field, meaning the next sync's automatic three-way merge will either pull
                    // it in or, if this user also changed it locally, potentially conflict on
                    // it. This mirrors LedgerSyncService.ClassifyField's remote-moved check,
                    // read-only, no writes happen from a diagnostics refresh.
                    bool baselineMatchesMain = baselineEntry != null
                        && string.Equals(baselineEntry.Value, paramEntry.Value.Value, StringComparison.Ordinal);

                    if (!baselineMatchesMain)
                    {
                        result.PendingItems.Add(new PendingLedgerItem
                        {
                            FamilyTypeKey = familyEntry.Key,
                            ParameterName = paramEntry.Value.ParameterName,
                            Value = paramEntry.Value.Value,
                            LastEditedBy = paramEntry.Value.LastEditedBy,
                            TimestampUtc = paramEntry.Value.TimestampUtc
                        });
                    }
                }
            }

            result.LastSyncUtc = maxBaselineTicks > 0 ? new DateTime(maxBaselineTicks, DateTimeKind.Utc) : (DateTime?)null;

            return result;
        }
    }
}
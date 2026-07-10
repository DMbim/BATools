using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using BA.BAApplication;
using BA.Settings;

namespace BA.Core.Ledger
{
    /// <summary>
    /// Applies pending ledger changes to the given document. Called from
    /// Application.DocumentSynchronizingWithCentral, which is confirmed by the Revit API
    /// documentation to permit document modification. Run() never throws out of itself;
    /// every per-field failure is caught and logged so a single locked type cannot abort
    /// the synchronization in progress. Per-field convergence is tracked via ExtensibleStorage
    /// on each FamilySymbol (a Map&lt;string,long&gt; of parameter GUID -> applied tick count),
    /// not a single per-symbol timestamp, since fields can be published independently.
    ///
    /// GetSchema/ReadAppliedTicks are internal (not private) so LedgerDiagnosticsService can
    /// read the same convergence state without duplicating the ExtensibleStorage schema logic.
    /// </summary>
    public static class LedgerSubscriberService
    {
        internal static readonly Guid SchemaGuid = new Guid("2C9A4E1B-3F3B-4C7F-9C0E-6D2C1B7E9F41");
        internal const string SchemaFieldName = "AppliedTimestampsTicks";
        private static Schema _schema;

        public static void Run(Document doc)
        {
            if (doc == null || doc.IsReadOnly)
            {
                return;
            }

            LedgerSettings settings = LedgerSettings.Load();

            TypeDataLedger ledger;
            try
            {
                ledger = LedgerFileService.ReadOnly();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("LedgerSubscriberService.Run: could not read ledger, skipping this sync", ex);
                return;
            }

            if (ledger.Families.Count == 0)
            {
                return;
            }

            // Single collector pass per Run(), cached in a dictionary. Per performance rule,
            // never re-scan the document per ledger entry.
            Dictionary<string, FamilySymbol> symbolsByKey = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .GroupBy(s => LedgerFileService.BuildKey(s.Family.Name, s.Name))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            int updatedSymbols = 0;
            int updatedFields = 0;
            int skippedFields = 0;
            int skippedByCategoryFilter = 0;

            using (var tx = new Transaction(doc, "Sync Ledger Type Data"))
            {
                tx.Start();

                foreach (KeyValuePair<string, LedgerFamilyNode> familyEntry in ledger.Families)
                {
                    if (!symbolsByKey.TryGetValue(familyEntry.Key, out FamilySymbol symbol))
                    {
                        continue; // this document doesn't have that family/type loaded
                    }

                    long categoryIdValue = symbol.Category?.Id.Value ?? 0;
                    if (!settings.IsCategoryAllowed(categoryIdValue))
                    {
                        skippedByCategoryFilter++;
                        continue;
                    }

                    Dictionary<string, long> appliedTicks = ReadAppliedTicks(symbol);
                    bool symbolTouched = false;

                    foreach (KeyValuePair<string, LedgerParameterEntry> paramEntry in familyEntry.Value.Parameters)
                    {
                        string paramGuidString = paramEntry.Key;
                        LedgerParameterEntry entry = paramEntry.Value;
                        long entryTicks = entry.TimestampUtc.Ticks;

                        appliedTicks.TryGetValue(paramGuidString, out long lastAppliedTicks);
                        if (entryTicks <= lastAppliedTicks)
                        {
                            continue; // already applied, or older than what this doc already has
                        }

                        try
                        {
                            if (TryApplyParameter(symbol, paramGuidString, entry))
                            {
                                appliedTicks[paramGuidString] = entryTicks;
                                symbolTouched = true;
                                updatedFields++;
                            }
                            else
                            {
                                skippedFields++;
                            }
                        }
                        catch (Exception ex)
                        {
                            // Most likely cause: this type is currently checked out by another
                            // user in this same central at the moment of this sync. Skip this
                            // field; it will be retried on the next sync since the tick was not
                            // advanced.
                            AppLogger.LogError(
                                $"LedgerSubscriberService: failed to apply '{entry.ParameterName}' on '{familyEntry.Key}'", ex);
                            skippedFields++;
                        }
                    }

                    if (symbolTouched)
                    {
                        WriteAppliedTicks(symbol, appliedTicks);
                        updatedSymbols++;
                    }
                }

                tx.Commit();
            }

            AppLogger.LogInfo(
                $"LedgerSubscriberService.Run: {updatedSymbols} type(s) touched, {updatedFields} field(s) applied, "
                + $"{skippedFields} field(s) skipped, {skippedByCategoryFilter} type(s) excluded by category filter.");
        }

        private static bool TryApplyParameter(FamilySymbol symbol, string paramGuidString, LedgerParameterEntry entry)
        {
            if (!Guid.TryParse(paramGuidString, out Guid targetGuid))
            {
                return false;
            }

            Parameter target = symbol.Parameters
                .Cast<Parameter>()
                .FirstOrDefault(p => p.IsShared && p.GUID == targetGuid);

            if (target == null || target.IsReadOnly)
            {
                return false;
            }

            switch (target.StorageType)
            {
                case StorageType.String:
                    return target.Set(entry.Value ?? string.Empty);

                case StorageType.Integer:
                    if (!int.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
                    {
                        AppLogger.LogInfo($"LedgerSubscriberService: could not parse Integer value '{entry.Value}' for '{entry.ParameterName}'");
                        return false;
                    }
                    return target.Set(intVal);

                case StorageType.Double:
                    if (!double.TryParse(entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double dblVal))
                    {
                        AppLogger.LogInfo($"LedgerSubscriberService: could not parse Double value '{entry.Value}' for '{entry.ParameterName}'");
                        return false;
                    }
                    return target.Set(dblVal);

                default:
                    // ElementId/None are never written by the Publisher. If one shows up here
                    // the ledger was hand-edited or written by a different tool; refuse rather
                    // than guessing at a conversion.
                    AppLogger.LogInfo($"LedgerSubscriberService: refusing unsupported StorageType '{target.StorageType}' for '{entry.ParameterName}'");
                    return false;
            }
        }

        internal static Dictionary<string, long> ReadAppliedTicks(FamilySymbol symbol)
        {
            Entity entity = symbol.GetEntity(GetSchema());
            if (!entity.IsValid())
            {
                return new Dictionary<string, long>();
            }

            IDictionary<string, long> stored = entity.Get<IDictionary<string, long>>(SchemaFieldName);
            return stored != null ? new Dictionary<string, long>(stored) : new Dictionary<string, long>();
        }

        private static void WriteAppliedTicks(FamilySymbol symbol, Dictionary<string, long> ticks)
        {
            var entity = new Entity(GetSchema());
            entity.Set(SchemaFieldName, (IDictionary<string, long>)ticks);
            symbol.SetEntity(entity);
        }

        internal static Schema GetSchema()
        {
            if (_schema != null)
            {
                return _schema;
            }

            _schema = Schema.Lookup(SchemaGuid);
            if (_schema != null)
            {
                return _schema;
            }

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName("BA_LedgerAppliedTimestamps");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddMapField(SchemaFieldName, typeof(string), typeof(long));

            _schema = builder.Finish();
            return _schema;
        }
    }
}

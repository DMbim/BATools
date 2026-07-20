using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BA.BAApplication;
using BA.Core.Ledger;
using BA.Settings;

namespace BA.Commands
{
    /// <summary>
    /// Ribbon command "Sync Type Data". Publishes only the shared Type Parameters that
    /// differ from what is currently in the ledger for this FamilySymbol. Does not modify
    /// the active Revit document, so TransactionMode.ReadOnly is correct here.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdPublishTypeData : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                FamilyInstance instance = ResolveSelectedInstance(uidoc, doc);
                if (instance == null)
                {
                    return Result.Cancelled;
                }

                FamilySymbol symbol = instance.Symbol;
                if (symbol == null)
                {
                    message = "Selected instance has no resolvable FamilySymbol.";
                    return Result.Failed;
                }

                LedgerSettings settings = LedgerSettings.Load();
                long categoryIdValue = symbol.Category?.Id.Value ?? 0;
                if (!settings.IsCategoryAllowed(categoryIdValue))
                {
                    TaskDialog.Show(
                        "Sync Type Data",
                        $"The category '{symbol.Category?.Name}' is not enabled for ledger tracking. "
                        + "Enable it in Ledger Settings if this Type should be synced.");
                    return Result.Cancelled;
                }

                List<SharedParamSnapshot> candidates = CollectSyncableParameters(symbol);
                if (candidates.Count == 0)
                {
                    TaskDialog.Show(
                        "Sync Type Data",
                        "No shared, non-ElementId, user-modifiable Type Parameters were found on this Type. Nothing to publish.");
                    return Result.Cancelled;
                }

                string ledgerKey = LedgerFileService.BuildKey(symbol.Family.Name, symbol.Name);
                string currentUser = Environment.UserName;

                // Step 1: read-only pass to compute the diff and detect conflicts against
                // whatever is currently in the ledger.
                TypeDataLedger snapshotLedger = LedgerFileService.ReadOnly(doc);
                snapshotLedger.Families.TryGetValue(ledgerKey, out LedgerFamilyNode existingNodeSnapshot);

                var conflicts = new List<ConflictItem>();
                var toPublish = new List<SharedParamSnapshot>();

                foreach (SharedParamSnapshot candidate in candidates)
                {
                    LedgerParameterEntry existingEntry = null;
                    existingNodeSnapshot?.Parameters.TryGetValue(candidate.GuidString, out existingEntry);

                    bool valueChangedFromLedger = existingEntry == null
                        || !string.Equals(existingEntry.Value, candidate.ValueAsString, StringComparison.Ordinal);

                    if (!valueChangedFromLedger)
                    {
                        continue;
                    }

                    toPublish.Add(candidate);

                    bool isForeignConflict = existingEntry != null
                        && !string.Equals(existingEntry.LastEditedBy, currentUser, StringComparison.OrdinalIgnoreCase);

                    if (isForeignConflict)
                    {
                        conflicts.Add(new ConflictItem
                        {
                            ParameterName = candidate.Name,
                            OldValue = existingEntry.Value,
                            NewValue = candidate.ValueAsString,
                            LastEditedBy = existingEntry.LastEditedBy,
                            TimestampUtc = existingEntry.TimestampUtc
                        });
                    }
                }

                if (toPublish.Count == 0)
                {
                    TaskDialog.Show("Sync Type Data", "The ledger already matches the current values on this Type. Nothing to publish.");
                    return Result.Cancelled;
                }

                if (conflicts.Count > 0 && !ShowConflictDialog(conflicts))
                {
                    return Result.Cancelled;
                }

                // Step 2: locked write with re-validation. The lock was not held across the
                // dialog in Step 1, so re-check each field against the live ledger before
                // committing; if a field moved again while the dialog was open, skip it
                // instead of silently clobbering a newer edit.
                DateTime publishTimestamp = DateTime.UtcNow;
                var reconflictParamNames = new List<string>();

                LedgerFileService.OpenAndModify(doc,ledger =>
                {
                    if (!ledger.Families.TryGetValue(ledgerKey, out LedgerFamilyNode node))
                    {
                        node = new LedgerFamilyNode();
                        ledger.Families[ledgerKey] = node;
                    }

                    foreach (SharedParamSnapshot candidate in toPublish)
                    {
                        node.Parameters.TryGetValue(candidate.GuidString, out LedgerParameterEntry currentEntry);

                        LedgerParameterEntry entrySeenInStep1 = null;
                        if (existingNodeSnapshot != null)
                        {
                            existingNodeSnapshot.Parameters.TryGetValue(candidate.GuidString, out entrySeenInStep1);
                        }

                        bool changedAgainSinceStep1 = currentEntry != null
                            && !string.Equals(currentEntry.LastEditedBy, currentUser, StringComparison.OrdinalIgnoreCase)
                            && (entrySeenInStep1 == null || entrySeenInStep1.TimestampUtc.Ticks != currentEntry.TimestampUtc.Ticks);

                        if (changedAgainSinceStep1)
                        {
                            reconflictParamNames.Add(candidate.Name);
                            continue;
                        }

                        node.Parameters[candidate.GuidString] = new LedgerParameterEntry
                        {
                            ParameterName = candidate.Name,
                            Value = candidate.ValueAsString,
                            StorageType = candidate.StorageType.ToString(),
                            TimestampUtc = publishTimestamp,
                            LastEditedBy = currentUser
                        };
                    }

                    return true;
                });

                if (reconflictParamNames.Count > 0)
                {
                    TaskDialog.Show(
                        "Sync Type Data",
                        "These fields changed again on the ledger while the confirmation dialog was open, "
                        + "and were NOT overwritten: " + string.Join(", ", reconflictParamNames)
                        + ". Re-run Sync Type Data to review the latest values.");
                }

                AppLogger.LogInfo($"CmdPublishTypeData: published {toPublish.Count - reconflictParamNames.Count} field(s) for '{ledgerKey}' by {currentUser}.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("CmdPublishTypeData.Execute failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static FamilyInstance ResolveSelectedInstance(UIDocument uidoc, Document doc)
        {
            ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
            FamilyInstance selectedInstance = selectedIds
                .Select(id => doc.GetElement(id))
                .OfType<FamilyInstance>()
                .FirstOrDefault();

            if (selectedInstance != null)
            {
                return selectedInstance;
            }

            TaskDialog.Show("Sync Type Data", "No Family Instance is currently selected. Pick one on the model to continue.");

            try
            {
                Reference pickedRef = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new FamilyInstanceSelectionFilter(),
                    "Select a Family Instance to publish Type Data from");
                return doc.GetElement(pickedRef) as FamilyInstance;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null;
            }
        }

        private static List<SharedParamSnapshot> CollectSyncableParameters(FamilySymbol symbol)
        {
            var result = new List<SharedParamSnapshot>();

            foreach (Parameter parameter in symbol.Parameters)
            {
                if (!parameter.IsShared || parameter.IsReadOnly)
                {
                    continue;
                }

                if (parameter.StorageType == StorageType.ElementId || parameter.StorageType == StorageType.None)
                {
                    // Explicitly excluded by design. ElementId values are document-local
                    // and cannot be meaningfully resolved across 5 independent models.
                    continue;
                }

                Guid guid = parameter.GUID;
                if (guid == Guid.Empty)
                {
                    continue;
                }

                result.Add(new SharedParamSnapshot
                {
                    Name = parameter.Definition.Name,
                    GuidString = guid.ToString("D"),
                    StorageType = parameter.StorageType,
                    ValueAsString = ExtractValueAsString(parameter)
                });
            }

            return result;
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
                    throw new InvalidOperationException(
                        $"Unsupported StorageType '{parameter.StorageType}' reached ExtractValueAsString. This should have been filtered in CollectSyncableParameters.");
            }
        }

        private static bool ShowConflictDialog(List<ConflictItem> conflicts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("The following fields were updated by someone else since the ledger was last read:");
            sb.AppendLine();

            foreach (ConflictItem c in conflicts)
            {
                sb.AppendLine($"- {c.ParameterName}: '{c.OldValue}' (by {c.LastEditedBy} at {c.TimestampUtc:u}) -> your new value '{c.NewValue}'");
            }

            sb.AppendLine();
            sb.AppendLine("Overwrite ALL of the above with your values?");

            var dialog = new TaskDialog("Sync Type Data - Conflict Detected")
            {
                MainInstruction = "Conflicting edits detected",
                MainContent = sb.ToString(),
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };

            return dialog.Show() == TaskDialogResult.Yes;
        }

        private class SharedParamSnapshot
        {
            public string Name { get; set; }
            public string GuidString { get; set; }
            public StorageType StorageType { get; set; }
            public string ValueAsString { get; set; }
        }

        private class ConflictItem
        {
            public string ParameterName { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
            public string LastEditedBy { get; set; }
            public DateTime TimestampUtc { get; set; }
        }

        private class FamilyInstanceSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is FamilyInstance;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA_Tools.ScheduleExporter.Helpers;
using BA_Tools.ScheduleExporter.Models;

namespace BA_Tools.ScheduleExporter.Services
{
    /// <summary>
    /// Writes ImportCompareResult's changed cells back to the Revit document.
    ///
    /// TRANSACTION STRATEGY:
    ///   All writes occur inside a single Transaction named "BA Schedule Import".
    ///   If the transaction itself throws (document locked, worksharing conflict), it is
    ///   rolled back and the exception is rethrown for the caller to handle.
    ///   Individual parameter write failures do NOT abort the transaction — they are
    ///   logged in WriteResult.Errors and skipped, allowing as many writes to succeed
    ///   as possible.
    ///
    /// INSTANCE vs TYPE PARAMETER WRITES:
    ///   Instance parameters: written directly on each element.
    ///   Type parameters:     deduplicated by (typeId, columnIndex) — written once on the
    ///                        type element. Last-write-wins for conflicts (user was warned
    ///                        in ImportPreviewWindow). This avoids redundant type writes and
    ///                        the confusion of writing the same type parameter N times.
    ///
    /// ELEMENT RESOLUTION:
    ///   Elements are looked up by ElementId (long). If GetElement returns null, the row
    ///   is counted as a failure with a descriptive error.
    /// </summary>
    public class ParameterWriteService
    {
        private readonly Document _doc;

        public ParameterWriteService(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public WriteResult WriteAll(
            List<ScheduleFieldMeta> fields,
            ImportCompareResult compareResult)
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            if (compareResult == null) throw new ArgumentNullException(nameof(compareResult));

            var result = new WriteResult();
            Dictionary<int, ScheduleFieldMeta> fieldsByIndex = fields.ToDictionary(f => f.ColumnIndex);

            // Separate instance writes (per element) from type writes (per type, deduplicated)
            var instanceWrites = new List<InstanceWrite>();
            var typeWrites = new Dictionary<(long typeId, int colIdx), TypeWrite>();

            foreach (ImportRowData row in compareResult.ProcessableRows)
            {
                Element element = _doc.GetElement(new ElementId(row.ElementId));
                if (element == null)
                {
                    result.FailureCount++;
                    result.Errors.Add(new WriteError
                    {
                        ElementId = row.ElementId,
                        ParameterName = "(element lookup)",
                        ErrorMessage = $"Element {row.ElementId} no longer exists in document."
                    });
                    continue;
                }

                foreach (KeyValuePair<int, ImportCellData> pair in row.Cells)
                {
                    ImportCellData cellData = pair.Value;
                    if (cellData.State != ChangeState.Changed) continue;

                    if (!fieldsByIndex.TryGetValue(pair.Key, out ScheduleFieldMeta meta)) continue;
                    if (meta.IsReadOnly) continue;

                    if (meta.Category == FieldCategory.TypeParameter)
                    {
                        ElementId typeId = element.GetTypeId();
                        if (typeId == ElementId.InvalidElementId) continue;
                        // Last-write-wins: overwrite any previous entry for the same type+column
                        typeWrites[(typeId.Value, pair.Key)] = new TypeWrite
                        {
                            TypeElementId = typeId.Value,
                            Meta = meta,
                            NewValue = cellData.RawValue
                        };
                    }
                    else
                    {
                        instanceWrites.Add(new InstanceWrite
                        {
                            Element = element,
                            Meta = meta,
                            NewValue = cellData.RawValue
                        });
                    }
                }
            }

            // Execute all writes inside one transaction
            using var tx = new Transaction(_doc, "BA Schedule Import");
            tx.Start();

            try
            {
                // Instance parameter writes
                foreach (InstanceWrite write in instanceWrites)
                {
                    ExecuteInstanceWrite(write, result);
                }

                // Type parameter writes (one per type per parameter)
                foreach (TypeWrite write in typeWrites.Values)
                {
                    ExecuteTypeWrite(write, result);
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                if (tx.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();
                throw new InvalidOperationException(
                    $"Transaction was rolled back due to an unexpected error: {ex.Message}", ex);
            }

            return result;
        }

        private void ExecuteInstanceWrite(InstanceWrite write, WriteResult result)
        {
            Parameter param = ScheduleFieldTypeDetector.GetParameterForField(
                write.Meta, _doc, write.Element);

            if (param == null)
            {
                result.FailureCount++;
                result.Errors.Add(new WriteError
                {
                    ElementId = write.Element.Id.Value,
                    ParameterName = write.Meta.DisplayName,
                    AttemptedValue = write.NewValue,
                    ErrorMessage = "Parameter not found on element."
                });
                return;
            }

            if (ParameterValueConverter.TrySetValue(param, write.NewValue, _doc, out string error))
            {
                result.SuccessCount++;
            }
            else
            {
                result.FailureCount++;
                result.Errors.Add(new WriteError
                {
                    ElementId = write.Element.Id.Value,
                    ParameterName = write.Meta.DisplayName,
                    AttemptedValue = write.NewValue,
                    ErrorMessage = error ?? "Unknown write failure."
                });
            }
        }

        private void ExecuteTypeWrite(TypeWrite write, WriteResult result)
        {
            Element typeElement = _doc.GetElement(new ElementId(write.TypeElementId));
            if (typeElement == null)
            {
                result.FailureCount++;
                result.Errors.Add(new WriteError
                {
                    ElementId = write.TypeElementId,
                    ParameterName = write.Meta.DisplayName,
                    AttemptedValue = write.NewValue,
                    ErrorMessage = $"Element type {write.TypeElementId} not found in document."
                });
                return;
            }

            // For type parameters, search directly on the type element's parameters
            Parameter param = null;
            foreach (Parameter p in typeElement.Parameters)
            {
                if (p.Id == write.Meta.ParameterId)
                {
                    param = p;
                    break;
                }
            }

            if (param == null)
            {
                result.FailureCount++;
                result.Errors.Add(new WriteError
                {
                    ElementId = write.TypeElementId,
                    ParameterName = write.Meta.DisplayName,
                    AttemptedValue = write.NewValue,
                    ErrorMessage = "Type parameter not found on element type."
                });
                return;
            }

            if (ParameterValueConverter.TrySetValue(param, write.NewValue, _doc, out string error))
            {
                result.SuccessCount++;
            }
            else
            {
                result.FailureCount++;
                result.Errors.Add(new WriteError
                {
                    ElementId = write.TypeElementId,
                    ParameterName = write.Meta.DisplayName,
                    AttemptedValue = write.NewValue,
                    ErrorMessage = error ?? "Unknown write failure on element type."
                });
            }
        }

        // ─── Private DTOs ──────────────────────────────────────────────────────

        private class InstanceWrite
        {
            public Element Element { get; set; }
            public ScheduleFieldMeta Meta { get; set; }
            public string NewValue { get; set; }
        }

        private class TypeWrite
        {
            public long TypeElementId { get; set; }
            public ScheduleFieldMeta Meta { get; set; }
            public string NewValue { get; set; }
        }
    }
}

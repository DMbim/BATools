
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace BA.Core.Content.Services
{
    public sealed class LoadedFamilyOperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public sealed class PurgeResult
    {
        public List<ElementId> Deleted { get; } = new();
        public List<(ElementId Id, string Reason)> Skipped { get; } = new();
    }

    /// <summary>
    /// All methods here must run inside Revit API context (via
    /// AppExternalInvoker.Instance.Run/Run&lt;T&gt;). None of these are
    /// thread-safe to call from WPF directly.
    /// </summary>
    public static class LoadedFamilyOperations
    {
        public static LoadedFamilyOperationResult RenameType(Document doc, ElementId symbolId, string newName)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(newName))
                return Fail("New type name cannot be empty.");

            if (doc.GetElement(symbolId) is not FamilySymbol symbol)
                return Fail("Type no longer exists in the document.");

            newName = newName.Trim();

            Family family = symbol.Family;
            bool duplicate = family.GetFamilySymbolIds()
                .Where(id => id != symbolId)
                .Select(id => doc.GetElement(id) as FamilySymbol)
                .Any(s => s != null && string.Equals(s.Name, newName, StringComparison.OrdinalIgnoreCase));

            if (duplicate)
                return Fail($"A type named '{newName}' already exists in family '{family.Name}'.");

            using var tx = new Transaction(doc, "BA Rename Loaded Type");
            try
            {
                tx.Start();
                symbol.Name = newName;
                tx.Commit();
                return Ok();
            }
            catch (Exception ex)
            {
                if (tx.HasStarted() && !tx.HasEnded())
                    tx.RollBack();
                return Fail(ex.Message);
            }
        }

        public static LoadedFamilyOperationResult RenameFamily(Document doc, ElementId familyId, string newName)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(newName))
                return Fail("New family name cannot be empty.");

            if (doc.GetElement(familyId) is not Family family)
                return Fail("Family no longer exists in the document.");

            newName = newName.Trim();

            using var tx = new Transaction(doc, "BA Rename Loaded Family");
            try
            {
                tx.Start();
                family.Name = newName;
                tx.Commit();
                return Ok();
            }
            catch (Exception ex)
            {
                if (tx.HasStarted() && !tx.HasEnded())
                    tx.RollBack();
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// targetIds may mix Family ids (delete the whole family, all its
        /// types) and FamilySymbol ids (delete just that type). The caller
        /// (ViewModel) is responsible for resolving checked tree nodes into
        /// this flat, de-duplicated id list and for pre-filtering to only
        /// unused elements before calling this.
        /// </summary>
        public static PurgeResult PurgeUnused(Document doc, IReadOnlyList<ElementId> targetIds)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var result = new PurgeResult();
            if (targetIds == null || targetIds.Count == 0)
                return result;

            using var tx = new Transaction(doc, "BA Purge Unused Loaded Families");
            tx.Start();

            foreach (ElementId id in targetIds)
            {
                Element? element = doc.GetElement(id);
                if (element == null)
                {
                    result.Skipped.Add((id, "Element no longer exists."));
                    continue;
                }

                try
                {
                    ICollection<ElementId> deleted = doc.Delete(id);
                    result.Deleted.AddRange(deleted);
                }
                catch (Exception ex)
                {
                    result.Skipped.Add((id, ex.Message));
                }
            }

            tx.Commit();
            return result;
        }

        /// <summary>
        /// Commits all edited parameter values for a single FamilySymbol in
        /// one transaction. rawValuesByParamName values are parsed according
        /// to each parameter's actual StorageType.
        /// </summary>
        public static LoadedFamilyOperationResult SetParameterValues(
            Document doc,
            ElementId symbolId,
            IReadOnlyDictionary<string, string> rawValuesByParamName)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (rawValuesByParamName == null || rawValuesByParamName.Count == 0)
                return Ok();

            if (doc.GetElement(symbolId) is not FamilySymbol symbol)
                return Fail("Type no longer exists in the document.");

            using var tx = new Transaction(doc, "BA Edit Loaded Type Parameters");
            try
            {
                tx.Start();

                foreach (var kvp in rawValuesByParamName)
                {
                    Parameter? param = symbol.LookupParameter(kvp.Key);
                    if (param == null)
                        throw new InvalidOperationException($"Parameter '{kvp.Key}' not found on type.");

                    if (param.IsReadOnly)
                        throw new InvalidOperationException($"Parameter '{kvp.Key}' is read-only.");

                    SetParameterFromText(param, kvp.Value);
                }

                tx.Commit();
                return Ok();
            }
            catch (Exception ex)
            {
                if (tx.HasStarted() && !tx.HasEnded())
                    tx.RollBack();
                return Fail(ex.Message);
            }
        }

        private static void SetParameterFromText(Parameter param, string text)
        {
            switch (param.StorageType)
            {
                case StorageType.String:
                    param.Set(text ?? string.Empty);
                    break;

                case StorageType.Integer:
                    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
                        throw new InvalidOperationException($"'{text}' is not a valid integer for '{param.Definition.Name}'.");
                    param.Set(intValue);
                    break;

                case StorageType.Double:
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double dblValue))
                        throw new InvalidOperationException($"'{text}' is not a valid number for '{param.Definition.Name}'.");
                    param.Set(dblValue);
                    break;

                case StorageType.ElementId:
                    if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long idValue))
                        throw new InvalidOperationException($"'{text}' is not a valid element id for '{param.Definition.Name}'.");
                    param.Set(new ElementId(idValue));
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported parameter storage type for '{param.Definition.Name}'.");
            }
        }

        private static LoadedFamilyOperationResult Ok() => new() { Success = true, Message = string.Empty };

        private static LoadedFamilyOperationResult Fail(string message) => new() { Success = false, Message = message };
    }
}
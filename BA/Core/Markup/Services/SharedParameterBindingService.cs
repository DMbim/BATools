// BA/Core/Parameters/SharedParameterBindingService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace BA.Core.Parameters
{
    /// <summary>
    /// Single entry point for binding shared parameters to categories. Consolidates what used
    /// to be split across SharedParameterBindingService.EnsureBound and
    /// SharedParameterBinder.BindSharedParameter / BindSharedParameterByGuid.
    ///
    /// HISTORY OF FIXES ON THIS CLASS, in order encountered:
    /// 1. Old SharedParameterBinder did map.Insert(...) || map.ReInsert(...); ReInsert replaces
    ///    the entire binding, category set included, silently dropping previously bound
    ///    categories. Fixed by always reading the existing binding first and unioning category
    ///    sets before writing.
    /// 2. A version of this fix mutated BindingMap via ReInsert while a
    ///    DefinitionBindingMapIterator over the same map was still in scope; ReInsert returned
    ///    false with no exception. Fixed by fully separating the read-only scan from the
    ///    mutation phase.
    /// 3. CONFIRMED VIA DIAGNOSTIC OUTPUT: ReInsert on an existing Instance binding (BA_Type
    ///    bound to Sheets only) still returned false when extending to Detail Items, despite
    ///    every category passing Category.AllowsBoundParameters. This matches a known,
    ///    documented unreliability in Revit's BindingMap.ReInsert. Workaround: Remove the
    ///    existing binding and Insert a fresh one covering the merged category set.
    /// 4. DATA SAFETY ADDITION: Remove+Insert unbinds the parameter from its previously bound
    ///    categories before rebinding. Whether Revit reliably re-exposes each element's
    ///    existing parameter value through that cycle is not something this codebase treats as
    ///    guaranteed. Before Remove, every element of every previously-bound category has its
    ///    current value for this parameter captured in memory (by StorageType). After the fresh
    ///    Insert succeeds, every captured value is written back explicitly via Parameter.Set().
    ///    This makes data survival independent of whatever Revit does internally during the
    ///    Remove/Insert cycle. All of this happens inside the caller's transaction; if any step
    ///    fails, the transaction rolls back and neither the binding nor the data is touched.
    ///
    /// MUST be called from within an active Transaction on doc. Caller's responsibility.
    /// </summary>
    public static class SharedParameterBindingService
    {
        public static void EnsureBound(
            Document doc,
            string sharedParamFilePath,
            string groupName,
            string paramName,
            BuiltInCategory category,
            bool instanceBinding = true)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(sharedParamFilePath))
                throw new ArgumentException("Shared parameter file path is empty.", nameof(sharedParamFilePath));
            if (string.IsNullOrWhiteSpace(paramName))
                throw new ArgumentException("Parameter name is empty.", nameof(paramName));

            Category cat = Category.GetCategory(doc, category)
                ?? throw new InvalidOperationException(
                    $"Category '{category}' does not exist in this document.");

            Definition definition = SharedParameterFileReader.FindExternalDefinitionByName(
                doc.Application, sharedParamFilePath, paramName)
                ?? throw new InvalidOperationException(
                    $"Shared parameter '{paramName}' was not found anywhere in the shared " +
                    $"parameter file '{sharedParamFilePath}'. The definition itself is missing, " +
                    "this cannot be auto-fixed. Contact your BIM admin to add it before this " +
                    "feature can be used.");

            BindOrExtendCategories(doc, definition, new[] { cat }, GroupTypeId.Data, instanceBinding);
        }

        public static void EnsureBound(
            Application app,
            Document doc,
            string sharedParamFilePath,
            string defName,
            Guid guidHint,
            ForgeTypeId groupId,
            bool isInstance,
            IList<Category> categories,
            bool createIfMissing)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(defName))
                throw new ArgumentException("Definition name is required.", nameof(defName));
            if (categories == null || categories.Count == 0)
                throw new ArgumentException("At least one category is required.", nameof(categories));

            ExternalDefinition extDef = SharedParameterFileReader.FindExternalDefinitionByName(
                app, sharedParamFilePath, defName);

            if (extDef == null && createIfMissing)
                extDef = SharedParameterFileReader.CreateExternalDefinition_String(
                    app, sharedParamFilePath, defName, "BA");

            if (extDef == null)
                throw new InvalidOperationException(
                    $"Shared parameter '{defName}' not found in the shared parameter file.");

            if (guidHint != Guid.Empty && extDef.GUID != guidHint)
                throw new InvalidOperationException(
                    $"GUID mismatch for '{defName}'. File GUID={extDef.GUID} vs expected={guidHint}");

            BindOrExtendCategories(doc, extDef, categories.ToList(), groupId ?? GroupTypeId.Data, isInstance);
        }

        public static void EnsureBoundByGuid(
            Application app,
            Document doc,
            string sharedParamFilePath,
            Guid guid,
            string nameHint,
            ForgeTypeId groupId,
            bool isInstance,
            IList<Category> categories,
            bool createIfMissing)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (guid == Guid.Empty) throw new ArgumentException("GUID is required.", nameof(guid));
            if (categories == null || categories.Count == 0)
                throw new ArgumentException("At least one category is required.", nameof(categories));

            ExternalDefinition extDef = SharedParameterFileReader.FindExternalDefinitionByGuid(
                app, sharedParamFilePath, guid);

            if (extDef == null && !string.IsNullOrWhiteSpace(nameHint))
                extDef = SharedParameterFileReader.FindExternalDefinitionByName(app, sharedParamFilePath, nameHint);

            if (extDef == null && createIfMissing && !string.IsNullOrWhiteSpace(nameHint))
                extDef = SharedParameterFileReader.CreateExternalDefinition_String(
                    app, sharedParamFilePath, nameHint, "BA");

            if (extDef == null)
                throw new InvalidOperationException(
                    $"Shared parameter not found in SP file. GUID={guid}, NameHint='{nameHint}'");

            if (extDef.GUID != guid)
                throw new InvalidOperationException(
                    $"GUID mismatch. Expected={guid}, Found={extDef.GUID} (Name='{extDef.Name}')");

            BindOrExtendCategories(doc, extDef, categories.ToList(), groupId ?? GroupTypeId.Data, isInstance);
        }

        private static void BindOrExtendCategories(
            Document doc,
            Definition definition,
            IReadOnlyCollection<Category> categoriesToEnsure,
            ForgeTypeId groupId,
            bool instanceBinding)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            string paramName = definition.Name;

            // ---- Phase 1: read-only scan. No mutation happens in this block. ----
            Definition matchedDef = null;
            ElementBinding matchedBinding = null;

            {
                BindingMap scanMap = doc.ParameterBindings;
                DefinitionBindingMapIterator iterator = scanMap.ForwardIterator();
                iterator.Reset();

                while (iterator.MoveNext())
                {
                    Definition existingDef = iterator.Key;
                    if (!string.Equals(existingDef.Name, paramName, StringComparison.Ordinal))
                        continue;

                    if (iterator.Current is not ElementBinding existingBinding)
                        continue;

                    matchedDef = existingDef;
                    matchedBinding = existingBinding;
                    break;
                }
            }

            // ---- Phase 2: mutation, against a freshly fetched BindingMap. ----
            BindingMap bindingMap = doc.ParameterBindings;

            if (matchedDef != null && matchedBinding != null)
            {
                bool isInstanceBinding = matchedBinding is InstanceBinding;
                if (isInstanceBinding != instanceBinding)
                    throw new InvalidOperationException(
                        $"Shared parameter '{paramName}' is already bound as a " +
                        $"{(isInstanceBinding ? "Type" : "Instance")} binding, not " +
                        $"{(instanceBinding ? "Instance" : "Type")}. Cannot auto-resolve this " +
                        "conflict, it must be fixed manually in Manage > Project Parameters.");

                List<Category> missingCategories = categoriesToEnsure
                    .Where(c => !matchedBinding.Categories.Contains(c))
                    .ToList();

                if (missingCategories.Count == 0)
                    return; // already fully bound to every requested category, true no-op

                var invalidCategories = new List<string>();
                foreach (Category existingCat in matchedBinding.Categories)
                    if (!existingCat.AllowsBoundParameters)
                        invalidCategories.Add($"{existingCat.Name} (already bound, invalid)");
                foreach (Category newCat in missingCategories)
                    if (!newCat.AllowsBoundParameters)
                        invalidCategories.Add($"{newCat.Name} (being added, invalid)");

                if (invalidCategories.Count > 0)
                    throw new InvalidOperationException(
                        $"Cannot extend the binding for shared parameter '{paramName}': the " +
                        $"following categories do not allow bound parameters: " +
                        $"{string.Join(", ", invalidCategories)}. Fix manually in Manage > " +
                        "Project Parameters before retrying.");

                CategorySet mergedSet = doc.Application.Create.NewCategorySet();
                foreach (Category existingCat in matchedBinding.Categories)
                    mergedSet.Insert(existingCat);
                foreach (Category c in missingCategories)
                    mergedSet.Insert(c);

                ElementBinding mergedBinding = instanceBinding
                    ? doc.Application.Create.NewInstanceBinding(mergedSet)
                    : doc.Application.Create.NewTypeBinding(mergedSet);

                // ---- Attempt 1: ReInsert. Fast path. Existing element data is untouched by
                //      this call regardless of outcome, no capture/restore needed here. ----
                bool reinserted = bindingMap.ReInsert(matchedDef, mergedBinding, groupId);
                if (reinserted)
                    return;

                // ---- Attempt 2: Remove + Insert fallback. Data-safety net: capture every
                //      existing value for this parameter, across every PREVIOUSLY bound
                //      category, before Remove touches anything. ----
                List<CapturedValue> capturedValues = CaptureExistingValues(
                    doc, matchedBinding.Categories, paramName);

                bool removed = bindingMap.Remove(matchedDef);
                if (!removed)
                    throw new InvalidOperationException(
                        $"Could not extend the binding for shared parameter '{paramName}' " +
                        "(ReInsert failed) and the Remove+Insert fallback also failed at the " +
                        "Remove step. No changes were made (transaction will roll back).");

                bool freshInserted = bindingMap.Insert(matchedDef, mergedBinding, groupId);
                if (!freshInserted)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"Failed to rebind shared parameter '{paramName}' even after " +
                        "removing the existing binding and attempting a fresh Insert.");
                    sb.AppendLine($"Binding kind: {(isInstanceBinding ? "Instance" : "Type")}");
                    sb.AppendLine($"Categories attempted ({mergedSet.Size}):");
                    foreach (Category c in mergedSet)
                        sb.AppendLine($"  - {c.Name}");
                    sb.AppendLine("All categories passed the AllowsBoundParameters check. " +
                        "This transaction will roll back, the parameter's original binding and " +
                        $"its {capturedValues.Count} captured value(s) are unaffected since " +
                        "nothing was ever written back. Contact your BIM admin.");
                    throw new InvalidOperationException(sb.ToString());
                }

                // Restore every captured value now that the parameter is bound again on its
                // original categories (plus the new ones). If any single restore fails, the
                // exception propagates up and the whole transaction rolls back, so this never
                // leaves some elements restored and others not.
                RestoreCapturedValues(doc, capturedValues, paramName);

                return;
            }

            // Not bound at all yet, no existing data can possibly exist for this parameter
            // on any element, capture/restore does not apply.
            var invalidNewCategories = categoriesToEnsure
                .Where(c => !c.AllowsBoundParameters)
                .Select(c => c.Name)
                .ToList();

            if (invalidNewCategories.Count > 0)
                throw new InvalidOperationException(
                    $"Cannot bind shared parameter '{paramName}': the following categories do " +
                    $"not allow bound parameters: {string.Join(", ", invalidNewCategories)}.");

            CategorySet catSet = doc.Application.Create.NewCategorySet();
            foreach (Category c in categoriesToEnsure)
                catSet.Insert(c);

            ElementBinding binding = instanceBinding
                ? doc.Application.Create.NewInstanceBinding(catSet)
                : doc.Application.Create.NewTypeBinding(catSet);

            bool inserted = bindingMap.Insert(definition, binding, groupId);
            if (!inserted)
                throw new InvalidOperationException(
                    $"Revit rejected binding shared parameter '{paramName}' to " +
                    $"{string.Join(", ", categoriesToEnsure.Select(c => c.Name))}. All " +
                    "categories passed the AllowsBoundParameters check; this usually means the " +
                    "parameter's underlying spec/type is incompatible with the target category.");
        }

        // ================================================================== //
        //  DATA SAFETY: capture / restore for the Remove+Insert fallback
        // ================================================================== //

        private sealed class CapturedValue
        {
            public ElementId ElementId;
            public StorageType StorageType;
            public string StringValue;
            public int IntValue;
            public double DoubleValue;
            public ElementId ElementIdValue;
            public bool HadValue;
        }

        /// <summary>
        /// Reads paramName's current value off every element of every given category, before
        /// the binding is removed. Elements with no value set (HasValue == false) are recorded
        /// as such rather than skipped, so restoration can distinguish "was never set" from
        /// "was set to a default/empty value" — though in practice only elements that HadValue
        /// are written back by RestoreCapturedValues.
        /// </summary>
        private static List<CapturedValue> CaptureExistingValues(
            Document doc, CategorySet categories, string paramName)
        {
            var captured = new List<CapturedValue>();

            foreach (Category cat in categories)
            {
                if (cat.Id.Value <= 0) continue; // skip anything not a real BuiltInCategory-backed category

                var collector = new FilteredElementCollector(doc)
                    .OfCategoryId(cat.Id)
                    .WhereElementIsNotElementType();

                foreach (Element el in collector)
                {
                    Parameter p = el.LookupParameter(paramName);
                    if (p == null) continue;

                    var cv = new CapturedValue
                    {
                        ElementId = el.Id,
                        StorageType = p.StorageType,
                        HadValue = p.HasValue
                    };

                    if (p.HasValue)
                    {
                        switch (p.StorageType)
                        {
                            case StorageType.String:
                                cv.StringValue = p.AsString();
                                break;
                            case StorageType.Integer:
                                cv.IntValue = p.AsInteger();
                                break;
                            case StorageType.Double:
                                cv.DoubleValue = p.AsDouble();
                                break;
                            case StorageType.ElementId:
                                cv.ElementIdValue = p.AsElementId();
                                break;
                        }
                    }

                    captured.Add(cv);
                }
            }

            return captured;
        }

        /// <summary>
        /// Writes every captured value back onto its original element. Only elements that
        /// HadValue are written; elements that never had a value stay unset, matching their
        /// original state rather than being forced to some default. Throws immediately on the
        /// first failed restore rather than continuing and silently dropping later ones, so a
        /// partial-restore failure surfaces clearly and rolls back the whole transaction.
        /// </summary>
        private static void RestoreCapturedValues(
            Document doc, List<CapturedValue> capturedValues, string paramName)
        {
            foreach (var cv in capturedValues)
            {
                if (!cv.HadValue) continue;

                Element el = doc.GetElement(cv.ElementId);
                if (el == null)
                    throw new InvalidOperationException(
                        $"Could not restore '{paramName}' on element {cv.ElementId}: the " +
                        "element no longer exists in the document. This should not be " +
                        "possible mid-transaction; the transaction will roll back.");

                Parameter p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly)
                    throw new InvalidOperationException(
                        $"Could not restore '{paramName}' on element {cv.ElementId}: parameter " +
                        "is missing or read-only immediately after rebinding. The transaction " +
                        "will roll back, no data has been lost.");

                switch (cv.StorageType)
                {
                    case StorageType.String:
                        p.Set(cv.StringValue ?? string.Empty);
                        break;
                    case StorageType.Integer:
                        p.Set(cv.IntValue);
                        break;
                    case StorageType.Double:
                        p.Set(cv.DoubleValue);
                        break;
                    case StorageType.ElementId:
                        p.Set(cv.ElementIdValue);
                        break;
                }
            }
        }
    }
}
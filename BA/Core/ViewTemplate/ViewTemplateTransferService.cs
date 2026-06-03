using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using View = Autodesk.Revit.DB.View;

namespace BA.Core.ViewTemplates
{
    public static class ViewTemplateTransferService
    {
        public static List<ViewTemplateItem> GetAllViewTemplates(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            return new FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<  Autodesk.Revit.DB.View>()
                .Where(v => v != null && v.IsTemplate)
                .OrderBy(v => v.ViewType.ToString())
                .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .Select(v => new ViewTemplateItem(v.Id, v.Name, v.ViewType))
                .ToList();
        }

        public static List<TemplatePropertyItem> GetTemplateProperties(Document doc, ElementId sourceTemplateId)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sourceTemplateId == null || sourceTemplateId == ElementId.InvalidElementId)
                throw new ArgumentException("Invalid source template id.", nameof(sourceTemplateId));

                Autodesk.Revit.DB.View source = doc.GetElement(sourceTemplateId) as View;
            if (source == null || !source.IsTemplate)
                throw new InvalidOperationException("Source element is not a valid view template.");

            IList<ElementId> parameterIds = source.GetTemplateParameterIds();
            List<TemplatePropertyItem> result = new List<TemplatePropertyItem>();

            foreach (ElementId pid in parameterIds)
            {
                string name = TryGetTemplateParameterDisplayName(doc, source, pid);
                result.Add(new TemplatePropertyItem(pid, name));
            }

            return result
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static ApplyTemplatePropertiesResult ApplySelectedProperties(
            Document doc,
            ElementId sourceTemplateId,
            ICollection<ElementId> targetTemplateIds,
            ICollection<ElementId> selectedParameterIds)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sourceTemplateId == null || sourceTemplateId == ElementId.InvalidElementId)
                throw new ArgumentException("Invalid source template id.", nameof(sourceTemplateId));
            if (targetTemplateIds == null) throw new ArgumentNullException(nameof(targetTemplateIds));
            if (selectedParameterIds == null) throw new ArgumentNullException(nameof(selectedParameterIds));

                Autodesk.Revit.DB.View sourceTemplate = doc.GetElement(sourceTemplateId) as Autodesk.Revit.DB.View;
            if (sourceTemplate == null || !sourceTemplate.IsTemplate)
                throw new InvalidOperationException("Source element is not a valid view template.");

            ApplyTemplatePropertiesResult result = new ApplyTemplatePropertiesResult
            {
                SourceTemplateName = sourceTemplate.Name,
                RequestedTargets = targetTemplateIds.Count
            };

            if (selectedParameterIds.Count == 0)
            {
                result.Messages.Add("No template properties were selected.");
                return result;
            }

            HashSet<long> availableSourceParamKeys = new HashSet<long>(
                sourceTemplate.GetTemplateParameterIds().Select(GetStableIdKey));

            List<ElementId> effectiveSelected = selectedParameterIds
                .Where(id => id != null && availableSourceParamKeys.Contains(GetStableIdKey(id)))
                .ToList();

            if (effectiveSelected.Count == 0)
            {
                result.Messages.Add("None of the selected properties are valid for the chosen source template.");
                return result;
            }

            using (TransactionGroup tg = new TransactionGroup(doc, "Apply selected template properties"))
            {
                tg.Start();

                try
                {
                    IList<ElementId> originalNonControlled =
                        sourceTemplate.GetNonControlledTemplateParameterIds().ToList();

                    ConfigureSourceTemplateForSelectedTransfer(doc, sourceTemplate, effectiveSelected);
                    ApplySourceTemplateToTargets(doc, sourceTemplate, sourceTemplateId, targetTemplateIds, result);
                    RestoreSourceTemplateConfiguration(doc, sourceTemplate, originalNonControlled);

                    tg.Assimilate();
                }
                catch
                {
                    tg.RollBack();
                    throw;
                }
            }

            return result;
        }

        private static void ConfigureSourceTemplateForSelectedTransfer(
            Document doc,
            View sourceTemplate,
            IList<ElementId> selectedParameterIds)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sourceTemplate == null) throw new ArgumentNullException(nameof(sourceTemplate));
            if (selectedParameterIds == null) throw new ArgumentNullException(nameof(selectedParameterIds));

            using Transaction tx = new Transaction(doc, "Temporarily configure source template");
            tx.Start();

            IList<ElementId> allParamIds = sourceTemplate.GetTemplateParameterIds();

            List<ElementId> temporaryNonControlled = allParamIds
                .Where(id => !ContainsElementId(selectedParameterIds, id))
                .ToList();

            sourceTemplate.SetNonControlledTemplateParameterIds(temporaryNonControlled);

            tx.Commit();
        }

        private static void ApplySourceTemplateToTargets(
            Document doc,
            View sourceTemplate,
            ElementId sourceTemplateId,
            ICollection<ElementId> targetTemplateIds,
            ApplyTemplatePropertiesResult result)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sourceTemplate == null) throw new ArgumentNullException(nameof(sourceTemplate));
            if (sourceTemplateId == null || sourceTemplateId == ElementId.InvalidElementId)
                throw new ArgumentException("Invalid source template id.", nameof(sourceTemplateId));
            if (targetTemplateIds == null) throw new ArgumentNullException(nameof(targetTemplateIds));
            if (result == null) throw new ArgumentNullException(nameof(result));

            using Transaction tx = new Transaction(doc, "Apply selected properties to target templates");
            tx.Start();

            foreach (ElementId targetId in targetTemplateIds)
            {
                if (targetId == null || targetId == ElementId.InvalidElementId)
                {
                    result.SkippedTargets++;
                    result.Messages.Add("Skipped invalid target template id.");
                    continue;
                }

                if (IdsEqual(targetId, sourceTemplateId))
                {
                    result.SkippedTargets++;
                    result.Messages.Add($"Skipped source template itself: {sourceTemplate.Name}");
                    continue;
                }

                View targetTemplate = doc.GetElement(targetId) as View;
                if (targetTemplate == null || !targetTemplate.IsTemplate)
                {
                    result.SkippedTargets++;
                    result.Messages.Add($"Skipped non-template element id {IdText(targetId)}.");
                    continue;
                }

                if (targetTemplate.ViewType != sourceTemplate.ViewType)
                {
                    result.SkippedTargets++;
                    result.Messages.Add(
                        $"Skipped '{targetTemplate.Name}' because view type differs " +
                        $"({targetTemplate.ViewType} != {sourceTemplate.ViewType}).");
                    continue;
                }

                try
                {
                    targetTemplate.ApplyViewTemplateParameters(sourceTemplate);
                    result.UpdatedTargets++;
                    result.Messages.Add($"Updated: {targetTemplate.Name}");
                }
                catch (Exception ex)
                {
                    result.SkippedTargets++;
                    result.Messages.Add($"Failed: {targetTemplate.Name} -> {ex.Message}");
                }
            }

            tx.Commit();
        }

        private static void RestoreSourceTemplateConfiguration(
            Document doc,
            Autodesk.Revit.DB.View sourceTemplate,
            IList<ElementId> originalNonControlled)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sourceTemplate == null) throw new ArgumentNullException(nameof(sourceTemplate));
            if (originalNonControlled == null) throw new ArgumentNullException(nameof(originalNonControlled));

            using Transaction tx = new Transaction(doc, "Restore source template configuration");
            tx.Start();

            sourceTemplate.SetNonControlledTemplateParameterIds(originalNonControlled);

            tx.Commit();
        }

        private static string TryGetTemplateParameterDisplayName(Document doc, View source, ElementId parameterId)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (source == null) throw new ArgumentNullException(nameof(source));

            if (parameterId == null || parameterId == ElementId.InvalidElementId)
                return "<Invalid parameter>";

            string? name = TryGetNameFromParameterElement(doc, parameterId);
            if (!string.IsNullOrWhiteSpace(name))
                return name;

            name = TryGetNameFromBuiltInParameter(parameterId);
            if (!string.IsNullOrWhiteSpace(name))
                return name;

            name = TryGetNameFromSourceParameters(source, parameterId);
            if (!string.IsNullOrWhiteSpace(name))
                return name;

            return $"Parameter {parameterId.Value}";
        }

        private static string? TryGetNameFromParameterElement(Document doc, ElementId parameterId)
        {
            ParameterElement pe = doc.GetElement(parameterId) as ParameterElement;
            if (pe != null && !string.IsNullOrWhiteSpace(pe.Name))
                return pe.Name;

            return null;
        }

        private static string? TryGetNameFromBuiltInParameter(ElementId parameterId)
        {
            try
            {
                int raw = unchecked((int)parameterId.Value);
                BuiltInParameter bip = (BuiltInParameter)raw;
                string label = LabelUtils.GetLabelFor(bip);
                if (!string.IsNullOrWhiteSpace(label))
                    return label;
            }
            catch
            {
            }

            return null;
        }

        private static string? TryGetNameFromSourceParameters(View source, ElementId parameterId)
        {
            try
            {
                foreach (Parameter p in source.Parameters)
                {
                    if (p == null) continue;
                    if (p.Id == null) continue;

                    if (p.Id.Value == parameterId.Value)
                    {
                        if (p.Definition != null && !string.IsNullOrWhiteSpace(p.Definition.Name))
                            return p.Definition.Name;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool ContainsElementId(IEnumerable<ElementId> ids, ElementId testId)
        {
            if (ids == null || testId == null) return false;
            long key = GetStableIdKey(testId);
            return ids.Any(x => x != null && GetStableIdKey(x) == key);
        }

        private static bool IdsEqual(ElementId a, ElementId b)
        {
            if (a == null || b == null) return false;
            return GetStableIdKey(a) == GetStableIdKey(b);
        }

        private static long GetStableIdKey(ElementId id)
        {
            return id.Value;
        }

        private static string IdText(ElementId id)
        {
            return id == null ? "<null>" : id.Value.ToString();
        }
    }
}
using System;
using Autodesk.Revit.DB;
using BA_Tools.ScheduleExporter.Models;

namespace BA_Tools.ScheduleExporter.Helpers
{
    /// <summary>
    /// Determines the FieldCategory and StorageType for a ScheduleField, and resolves
    /// the correct Parameter from an element for a given ScheduleFieldMeta.
    ///
    /// StorageType detection requires probing a live element because ScheduleField does
    /// not expose StorageType directly. This is done once against the first collected
    /// element in ScheduleReaderService and is safe for all elements of the same schedule.
    /// </summary>
    public static class ScheduleFieldTypeDetector
    {
        /// <summary>
        /// Determines the FieldCategory from the ScheduleField definition alone.
        /// StorageType is not set here; call UpdateStorageType separately after element probe.
        /// </summary>
        public static FieldCategory DetermineCategory(ScheduleField field)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));

            if (field.IsCalculatedField)
                return FieldCategory.Calculated;

            switch (field.FieldType)
            {
                case ScheduleFieldType.Instance:
                    return FieldCategory.Instance;

                case ScheduleFieldType.ElementType:
                    return FieldCategory.TypeParameter;

                default:
                    // Count, Percentage, Formula, CombinedParameters, etc.
                    return FieldCategory.Calculated;
            }
        }

        /// <summary>
        /// Probes a sample element to determine the StorageType of the parameter backing
        /// this field meta. Mutates meta.StorageType and may demote category to ElementIdType.
        /// Call this once per field using the first element from the schedule collector.
        /// </summary>
        public static void UpdateStorageType(
            ScheduleFieldMeta meta,
            Document doc,
            IList<Element> sampleElements)
        {
            if (meta.Category == FieldCategory.Calculated || meta.Category == FieldCategory.Hidden)
            {
                meta.StorageType = StorageType.None;
                return;
            }

            foreach (Element element in sampleElements)
            {
                Parameter param = GetParameterForField(meta, doc, element);
                if (param == null) continue;

                meta.StorageType = param.StorageType;
                if (param.StorageType == StorageType.ElementId)
                    meta.Category = FieldCategory.ElementIdType;
                return;
            }

            // No element in the schedule had this parameter
            meta.StorageType = StorageType.None;
            meta.Category = FieldCategory.Calculated;
        }
        /// <summary>
        /// Resolves the Parameter for a given ScheduleFieldMeta from an element.
        /// For type parameters, the parameter is read from the element's type, not the instance.
        /// Returns null if the parameter cannot be found.
        /// </summary>
        public static Parameter GetParameterForField(ScheduleFieldMeta meta, Document doc, Element element)
        {
            if (element == null || meta?.ParameterId == null
                || meta.ParameterId == ElementId.InvalidElementId)
                return null;

            // For type parameters, resolve against the element type
            Element targetElement;
            if (meta.Category == FieldCategory.TypeParameter)
            {
                ElementId typeId = element.GetTypeId();
                if (typeId == ElementId.InvalidElementId) return null;
                targetElement = doc.GetElement(typeId);
                if (targetElement == null) return null;
            }
            else
            {
                targetElement = element;
            }

            // Strategy 1: BuiltInParameter (negative ElementId values in Revit 2026)
            if (TryGetBuiltInParameter(meta.ParameterId, out BuiltInParameter bip))
            {
                Parameter p = targetElement.get_Parameter(bip);
                if (p != null) return p;
            }

            // Strategy 2: SharedParameterElement — resolve by GUID for stability
            if (doc.GetElement(meta.ParameterId) is SharedParameterElement sharedParamElem)
            {
                Parameter p = targetElement.get_Parameter(sharedParamElem.GuidValue);
                if (p != null) return p;
            }

            // Strategy 3: Scan element's parameters collection by ID (project parameters)
            foreach (Parameter p in targetElement.Parameters)
            {
                if (p.Id == meta.ParameterId)
                    return p;
            }

            return null;
        }

        /// <summary>
        /// In Revit 2026, ElementId.Value is a long. BuiltInParameter enum values are negative
        /// integers within the int range. This check distinguishes BIPs from document element IDs.
        /// </summary>
        private static bool TryGetBuiltInParameter(ElementId id, out BuiltInParameter bip)
        {
            bip = BuiltInParameter.INVALID;
            long val = id.Value;
            // BIPs are negative and within int range
            if (val < 0 && val >= int.MinValue)
            {
                bip = (BuiltInParameter)(int)val;
                return Enum.IsDefined(typeof(BuiltInParameter), bip);
            }
            return false;
        }
    }
}

// Path: BA\Materials\MaterialIdentityService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.BAApplication;

namespace BA.Materials
{
    public sealed class MaterialIdentityInfo
    {
        public string Name { get; set; } = string.Empty;
        public string MaterialClass { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Keynote { get; set; } = string.Empty;
    }

    public sealed class MaterialIdentityResult
    {
        public bool Success { get; set; }
        public string FailureReason { get; set; } = string.Empty;
    }

    public sealed class BulkClassAssignResult
    {
        public bool Success { get; set; }
        public List<ElementId> FailedIds { get; set; } = new List<ElementId>();
        public string FailureReason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Reads/writes the Identity tab fields of a Material. Must be called on Revit's
    /// API thread (invoke through BA.UI.ExternalEvents.RevitExternalInvoker).
    ///
    /// VERIFY BEFORE SHIPPING: Description and Keynote are read/written via
    /// LookupParameter(name) rather than a BuiltInParameter, because I do not have a
    /// confirmed BuiltInParameter enum for these two on Material specifically, and a
    /// wrong enum throws immediately (same failure class as the documented
    /// ALL_MODEL_INSTANCE_COMMENTS / OST_Lines pitfall). Once you confirm the correct
    /// BuiltInParameter against a real material in the project, replace LookupParameter
    /// with material.get_Parameter(BuiltInParameter.XXX) for reliability independent of
    /// UI language.
    /// </summary>
    public sealed class MaterialIdentityService
    {
        private const string DescriptionParamName = "Description";
        private const string KeynoteParamName = "Keynote";

        public MaterialIdentityInfo GetIdentity(Material material)
        {
            if (material == null)
                throw new ArgumentNullException(nameof(material));

            return new MaterialIdentityInfo
            {
                Name = material.Name,
                MaterialClass = material.MaterialClass ?? string.Empty,
                Description = ReadStringParameter(material, DescriptionParamName),
                Keynote = ReadStringParameter(material, KeynoteParamName)
            };
        }

        public MaterialIdentityResult SetIdentity(Document doc, ElementId materialId, MaterialIdentityInfo info)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (materialId == null || materialId == ElementId.InvalidElementId)
                throw new ArgumentException("materialId must be a valid ElementId.", nameof(materialId));
            if (info == null) throw new ArgumentNullException(nameof(info));

            Material material = doc.GetElement(materialId) as Material;
            if (material == null)
            {
                return new MaterialIdentityResult
                {
                    Success = false,
                    FailureReason = "Element is not a Material, or no longer exists in the document."
                };
            }

            using (Transaction t = new Transaction(doc, "BA Tools: Update material identity"))
            {
                try
                {
                    t.Start();

                    if (!string.Equals(material.Name, info.Name, StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(info.Name))
                    {
                        material.Name = info.Name;
                    }

                    if (!string.Equals(material.MaterialClass, info.MaterialClass, StringComparison.Ordinal))
                    {
                        material.MaterialClass = info.MaterialClass ?? string.Empty;
                    }

                    WriteStringParameter(material, DescriptionParamName, info.Description);
                    WriteStringParameter(material, KeynoteParamName, info.Keynote);

                    t.Commit();

                    AppLogger.LogInfo($"BA.Materials: updated identity for material '{material.Name}' (id {materialId.Value})");

                    return new MaterialIdentityResult { Success = true };
                }
                catch (Exception ex)
                {
                    if (t.HasStarted() && !t.HasEnded())
                        t.RollBack();

                    AppLogger.LogError("MaterialIdentityService.SetIdentity", ex);

                    bool looksLikeDuplicateName = ex is ArgumentException
                        && ex.Message.IndexOf("name", StringComparison.OrdinalIgnoreCase) >= 0;

                    return new MaterialIdentityResult
                    {
                        Success = false,
                        FailureReason = looksLikeDuplicateName
                            ? $"A material named '{info.Name}' already exists in this document."
                            : "Failed to update material identity. See BA Tools log for details."
                    };
                }
            }
        }

        /// <summary>
        /// Assigns MaterialClass across many materials in a single Transaction, for the
        /// drag-multiple-materials-onto-a-category workflow. Deliberately does NOT go
        /// through GetIdentity/SetIdentity per material, that would touch Name/
        /// Description/Keynote unnecessarily and cost one Transaction per material.
        /// This only ever writes MaterialClass. Must be called on Revit's API thread.
        /// </summary>
        public BulkClassAssignResult SetMaterialClassBulk(Document doc, IReadOnlyDictionary<ElementId, string> assignments)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            if (assignments == null || assignments.Count == 0)
                return new BulkClassAssignResult { Success = true };

            var result = new BulkClassAssignResult();

            using (Transaction t = new Transaction(doc, "BA Tools: Bulk assign material class"))
            {
                try
                {
                    t.Start();

                    foreach (var kvp in assignments)
                    {
                        Material material = doc.GetElement(kvp.Key) as Material;
                        if (material == null)
                        {
                            result.FailedIds.Add(kvp.Key);
                            continue;
                        }

                        material.MaterialClass = kvp.Value ?? string.Empty;
                    }

                    t.Commit();

                    result.Success = result.FailedIds.Count == 0;
                    if (!result.Success)
                    {
                        result.FailureReason = $"{result.FailedIds.Count} material(s) could not be found and were skipped.";
                    }

                    AppLogger.LogInfo($"BA.Materials: bulk-assigned class for {assignments.Count - result.FailedIds.Count} material(s).");

                    return result;
                }
                catch (Exception ex)
                {
                    if (t.HasStarted() && !t.HasEnded())
                        t.RollBack();

                    AppLogger.LogError("MaterialIdentityService.SetMaterialClassBulk", ex);

                    return new BulkClassAssignResult
                    {
                        Success = false,
                        FailedIds = assignments.Keys.ToList(),
                        FailureReason = "Failed to assign material class in bulk. See BA Tools log for details."
                    };
                }
            }
        }

        private static string ReadStringParameter(Material material, string parameterName)
        {
            Parameter p = material.LookupParameter(parameterName);
            if (p == null || p.StorageType != StorageType.String)
                return string.Empty;

            return p.AsString() ?? string.Empty;
        }

        private static void WriteStringParameter(Material material, string parameterName, string value)
        {
            Parameter p = material.LookupParameter(parameterName);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.String)
            {
                AppLogger.LogInfo($"BA.Materials: parameter '{parameterName}' not found or not writable on material '{material.Name}', skipped.");
                return;
            }

            p.Set(value ?? string.Empty);
        }
    }
}

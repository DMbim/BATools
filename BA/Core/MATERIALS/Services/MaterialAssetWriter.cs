// Path: BA\Materials\MaterialAssetWriter.cs
using System;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using BA.BAApplication;
using BA.Materials.Models;

namespace BA.Materials
{
    public sealed class MaterialCreateResult
    {
        public bool Success { get; set; }
        public ElementId MaterialId { get; set; } = ElementId.InvalidElementId;
        public ElementId AppearanceAssetElementId { get; set; } = ElementId.InvalidElementId;
        public string FailureReason { get; set; } = string.Empty;
    }

    public sealed class MaterialAssetWriteResult
    {
        public bool Success { get; set; }
        public string FailureReason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Writes MaterialChannelSet values into a Generic-schema appearance asset, and
    /// creates new Material + AppearanceAssetElement pairs for the New Material action.
    /// Must be called on Revit's API thread.
    ///
    /// SCHEMA UNCERTAINTY: generic_self_illum_filter_map and generic_cutout_opacity are
    /// not confidently confirmed property names/types for this schema. Call
    /// MaterialAssetReader.DumpAssetPropertyNames against the seed asset once (see
    /// SeedAssetProvider) and correct SetColorProperty/SetDoubleProperty calls below if
    /// the dumped names differ. Every other property mapped here (generic_diffuse,
    /// generic_glossiness, generic_reflectivity_at_0deg, generic_bump_amount,
    /// generic_self_illum_luminance, generic_transparency) is a well-documented Generic
    /// schema property and does not need re-verification.
    /// </summary>
    public sealed class MaterialAssetWriter
    {
        private readonly SeedAssetProvider _seedAssetProvider;

        public MaterialAssetWriter(SeedAssetProvider seedAssetProvider)
        {
            _seedAssetProvider = seedAssetProvider ?? throw new ArgumentNullException(nameof(seedAssetProvider));
        }

        /// <summary>
        /// Phase 1: creates the Material element and a new AppearanceAssetElement
        /// duplicated from the Generic seed asset, links them via AppearanceAssetId,
        /// commits. Does NOT apply channel values, call WriteChannels afterward for that
        /// (see class remarks on why this is split into two steps).
        /// </summary>
        public MaterialCreateResult CreateNew(Document doc, Application application, string materialName)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (application == null) throw new ArgumentNullException(nameof(application));
            if (string.IsNullOrWhiteSpace(materialName))
                throw new ArgumentException("materialName cannot be null or empty.", nameof(materialName));

            Asset seedAsset = _seedAssetProvider.FindGenericSeedAsset(application);
            if (seedAsset == null)
            {
                return new MaterialCreateResult
                {
                    Success = false,
                    FailureReason = "Could not resolve a Generic seed asset from Revit's built-in appearance library. See BA Tools log."
                };
            }

            using (Transaction t = new Transaction(doc, "BA Tools: Create material"))
            {
                try
                {
                    t.Start();

                    ElementId materialId = Material.Create(doc, materialName);
                    Material material = doc.GetElement(materialId) as Material;

                    string assetName = materialName;
                    AppearanceAssetElement appearanceAssetElement =
                        AppearanceAssetElement.Create(doc, assetName, seedAsset);

                    material.AppearanceAssetId = appearanceAssetElement.Id;

                    t.Commit();

                    AppLogger.LogInfo($"BA.Materials: created material '{materialName}' (id {materialId.Value}) with appearance asset id {appearanceAssetElement.Id.Value}");

                    return new MaterialCreateResult
                    {
                        Success = true,
                        MaterialId = materialId,
                        AppearanceAssetElementId = appearanceAssetElement.Id
                    };
                }
                catch (Exception ex)
                {
                    if (t.HasStarted() && !t.HasEnded())
                        t.RollBack();

                    AppLogger.LogError("MaterialAssetWriter.CreateNew", ex);

                    bool looksLikeDuplicateName = ex is ArgumentException
                        && ex.Message.IndexOf("name", StringComparison.OrdinalIgnoreCase) >= 0;

                    return new MaterialCreateResult
                    {
                        Success = false,
                        FailureReason = looksLikeDuplicateName
                            ? $"A material or appearance asset named '{materialName}' already exists in this document."
                            : "Failed to create material. See BA Tools log for details."
                    };
                }
            }
        }

        /// <summary>
        /// Phase 2 (and the general-purpose editor for existing materials): applies
        /// MaterialChannelSet values into the appearance asset via AppearanceAssetEditScope.
        /// This is the method the debounced writer calls on every settled slider change.
        /// </summary>
        public MaterialAssetWriteResult WriteChannels(Document doc, ElementId appearanceAssetElementId, MaterialChannelSet channels)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (appearanceAssetElementId == null || appearanceAssetElementId == ElementId.InvalidElementId)
                throw new ArgumentException("appearanceAssetElementId must be valid.", nameof(appearanceAssetElementId));
            if (channels == null) throw new ArgumentNullException(nameof(channels));

            try
            {
                using (AppearanceAssetEditScope editScope = new AppearanceAssetEditScope(doc))
                {
                    Asset editableAsset = editScope.Start(appearanceAssetElementId);

                    SetColorProperty(editableAsset, "generic_diffuse",
                        channels.AlbedoR, channels.AlbedoG, channels.AlbedoB);

                    SetDoubleProperty(editableAsset, "generic_glossiness", 1.0 - channels.Roughness);
                    SetDoubleProperty(editableAsset, "generic_reflectivity_at_0deg", channels.Reflectivity);
                    SetDoubleProperty(editableAsset, "generic_bump_amount", channels.BumpAmount);
                    SetDoubleProperty(editableAsset, "generic_self_illum_luminance", channels.EmissiveLuminanceCdM2);
                    SetDoubleProperty(editableAsset, "generic_transparency", channels.Transparency);
                    SetDoubleProperty(editableAsset, "generic_cutout_opacity", channels.CutoutOpacity);

                    SetColorProperty(editableAsset, "generic_self_illum_filter_map",
                        channels.EmissiveR, channels.EmissiveG, channels.EmissiveB);

                    editScope.Commit(true);
                }

                AppLogger.LogInfo($"BA.Materials: wrote channel set to appearance asset id {appearanceAssetElementId.Value}");

                return new MaterialAssetWriteResult { Success = true };
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MaterialAssetWriter.WriteChannels", ex);

                return new MaterialAssetWriteResult
                {
                    Success = false,
                    FailureReason = "Failed to write appearance channels. See BA Tools log for details."
                };
            }
        }

        private static void SetDoubleProperty(Asset editableAsset, string propertyName, double value)
        {
            AssetProperty prop = editableAsset.FindByName(propertyName);
            if (prop is AssetPropertyDouble doubleProp && !doubleProp.IsReadOnly)
            {
                doubleProp.Value = value;
                return;
            }

            AppLogger.LogInfo($"BA.Materials: could not set double property '{propertyName}', not found, wrong type, or read-only.");
        }

        private static void SetColorProperty(Asset editableAsset, string propertyName, byte r, byte g, byte b)
        {
            AssetProperty prop = editableAsset.FindByName(propertyName);
            if (prop is AssetPropertyDoubleArray4d colorProp && !colorProp.IsReadOnly)
            {
                colorProp.SetValueAsColor(new Color(r, g, b));
                return;
            }

            AppLogger.LogInfo($"BA.Materials: could not set color property '{propertyName}', not found, wrong type, or read-only.");
        }
    }
}
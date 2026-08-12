// Path: BA\Materials\MaterialAssetReader.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using BA.BAApplication;
using BA.Materials.Models;

namespace BA.Materials
{
    /// <summary>
    /// Reads Generic-schema appearance asset properties into a MaterialChannelSet.
    /// Uses AppearanceAssetElement.GetRenderingAsset(), a read-only snapshot, safe to
    /// call without any transaction. Must be called on Revit's API thread.
    /// </summary>
    public sealed class MaterialAssetReader
    {
        public MaterialChannelSet ReadChannelSet(AppearanceAssetElement assetElement, string materialName)
        {
            if (assetElement == null)
                throw new ArgumentNullException(nameof(assetElement));

            Asset asset = assetElement.GetRenderingAsset();
            return ReadChannelSetFromAsset(asset, materialName);
        }

        /// <summary>
        /// Same read logic as ReadChannelSet, but operates directly on a bare Asset
        /// rather than requiring an AppearanceAssetElement already resident in a
        /// document. Used by AssetLibraryBrowserService to preview a built-in library
        /// asset before it has been duplicated into anything.
        /// </summary>
        public MaterialChannelSet ReadChannelSetFromAsset(Asset asset, string materialName)
        {
            MaterialChannelSet channels = new MaterialChannelSet { MaterialName = materialName ?? string.Empty };

            Color albedo = GetColorProperty(asset, "generic_diffuse", new Color(200, 200, 200));
            channels.AlbedoR = albedo.Red;
            channels.AlbedoG = albedo.Green;
            channels.AlbedoB = albedo.Blue;

            double glossiness = GetDoubleProperty(asset, "generic_glossiness", 0.5);
            channels.Roughness = 1.0 - glossiness;

            channels.Reflectivity = GetDoubleProperty(asset, "generic_reflectivity_at_0deg", 0.0);
            channels.BumpAmount = GetDoubleProperty(asset, "generic_bump_amount", 0.0);
            channels.EmissiveLuminanceCdM2 = GetDoubleProperty(asset, "generic_self_illum_luminance", 0.0);
            channels.Transparency = GetDoubleProperty(asset, "generic_transparency", 0.0);
            channels.CutoutOpacity = GetDoubleProperty(asset, "generic_cutout_opacity", 1.0);

            // Uncertain property name/type, see file header note in MaterialAssetWriter.
            // Falls back to black (no emissive tint) if the property isn't found or
            // isn't a color-typed property under this name.
            Color emissive = GetColorProperty(asset, "generic_self_illum_filter_map", new Color(0, 0, 0));
            channels.EmissiveR = emissive.Red;
            channels.EmissiveG = emissive.Green;
            channels.EmissiveB = emissive.Blue;

            return channels;
        }

        /// <summary>
        /// Diagnostic helper: logs every property name present on the given asset.
        /// Run once against the Generic seed asset (see SeedAssetProvider) to confirm
        /// the exact property names before trusting the mapping above in production.
        /// Mirrors the codebase's existing "diagnose before coding" convention.
        /// </summary>
        public void DumpAssetPropertyNames(Asset asset, string context)
        {
            if (asset == null)
            {
                AppLogger.LogInfo($"BA.Materials: DumpAssetPropertyNames({context}) called with null asset.");
                return;
            }

            AppLogger.LogInfo($"BA.Materials: dumping {asset.Size} asset properties for context '{context}':");

            for (int i = 0; i < asset.Size; i++)
            {
                AssetProperty prop = asset[i];
                if (prop == null) continue;
                AppLogger.LogInfo($"BA.Materials:   [{i}] Name='{prop.Name}' Type='{prop.Type}'");
            }
        }

        private static double GetDoubleProperty(Asset asset, string propertyName, double fallback)
        {
            AssetProperty prop = asset?.FindByName(propertyName);
            if (prop is AssetPropertyDouble doubleProp)
                return doubleProp.Value;

            AppLogger.LogInfo($"BA.Materials: asset property '{propertyName}' not found or not a double, using fallback {fallback}.");
            return fallback;
        }

        private static Color GetColorProperty(Asset asset, string propertyName, Color fallback)
        {
            AssetProperty prop = asset?.FindByName(propertyName);
            if (prop is AssetPropertyDoubleArray4d colorProp)
            {
                try
                {
                    return colorProp.GetValueAsColor();
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"MaterialAssetReader.GetColorProperty({propertyName})", ex);
                    return fallback;
                }
            }

            AppLogger.LogInfo($"BA.Materials: asset property '{propertyName}' not found or not a color, using fallback.");
            return fallback;
        }
    }
}
// Path: BA\Materials\SeedAssetProvider.cs
using System;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB.Visual;
using BA.BAApplication;

namespace BA.Materials
{
    /// <summary>
    /// Supplies the seed Asset used to create new AppearanceAssetElements carrying the
    /// Generic schema. Sourced from Application.GetAssets(AssetType.Appearance), which
    /// returns Revit's own built-in asset library (the same assets the native Material
    /// Browser's "Autodesk library" pane draws from), no shipped .adsklib or embedded
    /// resource required.
    ///
    /// VERIFY BEFORE SHIPPING: I have not run this against a live Revit 2026 session to
    /// confirm GetAssets(AssetType.Appearance) still returns a Generic-schema entry with
    /// this exact property name in this version. The lookup below is written defensively
    /// (checks for the generic_diffuse property rather than trusting a display name that
    /// could vary by language pack), but the very first call should be smoke-tested with
    /// a breakpoint on FindGenericSeedAsset before this goes near production, since if
    /// this returns null, every "New Material" action in the tool fails at that point.
    /// </summary>
    public sealed class SeedAssetProvider
    {
        private const string GenericSchemaMarkerProperty = "generic_diffuse";

        private Asset _cachedSeedAsset;

        /// <summary>
        /// Returns the Generic-schema seed Asset, cached after first lookup for the
        /// lifetime of this provider instance. Must be called on Revit's API thread.
        /// Returns null if no Generic-schema asset could be found, callers must check
        /// for null and surface a clear failure rather than assume success.
        /// </summary>
        public Asset FindGenericSeedAsset(Application application)
        {
            if (application == null)
                throw new ArgumentNullException(nameof(application));

            if (_cachedSeedAsset != null)
                return _cachedSeedAsset;

            try
            {
                System.Collections.Generic.IList<Asset> appearanceAssets =
                    application.GetAssets(AssetType.Appearance);

                if (appearanceAssets == null || appearanceAssets.Count == 0)
                {
                    AppLogger.LogInfo("BA.Materials: Application.GetAssets(AssetType.Appearance) returned no assets.");
                    return null;
                }

                Asset match = appearanceAssets.FirstOrDefault(HasGenericSchema);

                if (match == null)
                {
                    AppLogger.LogInfo("BA.Materials: no built-in asset with a generic_diffuse property was found in the appearance asset library.");
                    return null;
                }

                _cachedSeedAsset = match;
                AppLogger.LogInfo($"BA.Materials: resolved Generic seed asset '{match.Name}' from the built-in appearance library.");

                return _cachedSeedAsset;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("SeedAssetProvider.FindGenericSeedAsset", ex);
                return null;
            }
        }

        private static bool HasGenericSchema(Asset asset)
        {
            if (asset == null)
                return false;

            for (int i = 0; i < asset.Size; i++)
            {
                AssetProperty prop = asset[i];
                if (prop != null && string.Equals(prop.Name, GenericSchemaMarkerProperty, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
// Path: BA\Materials\AssetLibraryBrowserService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using BA.BAApplication;
using BA.Materials.Models;

namespace BA.Materials
{
    public sealed class AssetApplyResult
    {
        public bool Success { get; set; }
        public ElementId NewAppearanceAssetElementId { get; set; } = ElementId.InvalidElementId;
        public string FailureReason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Browses and applies entries from Revit's built-in appearance asset library
    /// (Application.GetAssets(AssetType.Appearance)). Applying an asset works uniformly
    /// regardless of schema, it duplicates the whole Asset object into a new
    /// AppearanceAssetElement and re-points the material at it, no property
    /// decomposition involved, unlike the live preview path which only understands
    /// the Generic schema. Must be called on Revit's API thread.
    ///
    /// KNOWN GAP, not fixed here: applying a new asset orphans the material's previous
    /// AppearanceAssetElement, it is not deleted. Deleting it automatically was
    /// deliberately not done, in case that asset element is ever referenced by more
    /// than one material, an assumption I can't verify from here, this mirrors the
    /// project's existing stance on deferring destructive cleanup operations (see the
    /// Type Data Ledger's parameter unbinding, explicitly left as a manual operation).
    /// If orphaned AppearanceAssetElements accumulate noticeably in the library over
    /// time, that's a separate cleanup command to build deliberately, not something to
    /// bolt on here.
    /// </summary>
    public sealed class AssetLibraryBrowserService
    {
        private const string GenericSchemaMarkerProperty = "generic_diffuse";

        public List<AssetLibraryEntry> ListAllAssets(Application application, HashSet<string> favoriteNames)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));

            List<AssetLibraryEntry> result = new List<AssetLibraryEntry>();

            IList<Asset> assets = application.GetAssets(AssetType.Appearance);
            if (assets == null) return result;

            foreach (Asset asset in assets)
            {
                if (asset == null) continue;

                result.Add(new AssetLibraryEntry
                {
                    Name = asset.Name,
                    IsGenericSchema = HasGenericSchema(asset),
                    IsFavorite = favoriteNames != null && favoriteNames.Contains(asset.Name)
                });
            }

            return result.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Returns a preview MaterialChannelSet for a Generic-schema asset, or null if
        /// the named asset can't be found or isn't Generic-schema. Callers should show
        /// a neutral placeholder rather than an empty/default sphere on null, to avoid
        /// implying "this is what it looks like" for a schema we can't actually read.
        /// </summary>
        public MaterialChannelSet TryReadPreviewChannels(Application application, string assetName, MaterialAssetReader reader)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            Asset asset = FindAssetByName(application, assetName);
            if (asset == null || !HasGenericSchema(asset))
                return null;

            return reader.ReadChannelSetFromAsset(asset, assetName);
        }

        /// <summary>
        /// Duplicates the named built-in asset into libraryDoc as a new
        /// AppearanceAssetElement and re-points materialId's AppearanceAssetId at it.
        /// Works for any schema. Does not delete the material's previous appearance
        /// asset, see class remarks.
        /// </summary>
        public AssetApplyResult ApplyAssetToMaterial(Document libraryDoc, Application application, string assetName, ElementId materialId)
        {
            if (libraryDoc == null) throw new ArgumentNullException(nameof(libraryDoc));
            if (application == null) throw new ArgumentNullException(nameof(application));

            Asset sourceAsset = FindAssetByName(application, assetName);
            if (sourceAsset == null)
            {
                return new AssetApplyResult
                {
                    Success = false,
                    FailureReason = $"Asset '{assetName}' could not be located in the built-in library."
                };
            }

            Material material = libraryDoc.GetElement(materialId) as Material;
            if (material == null)
            {
                return new AssetApplyResult
                {
                    Success = false,
                    FailureReason = "Target material could not be found."
                };
            }

            using (Transaction t = new Transaction(libraryDoc, "BA Tools: Apply asset from library"))
            {
                try
                {
                    t.Start();

                    string uniqueName = GenerateUniqueAssetElementName(libraryDoc, assetName);
                    AppearanceAssetElement newElement = AppearanceAssetElement.Create(libraryDoc, uniqueName, sourceAsset);

                    material.AppearanceAssetId = newElement.Id;

                    t.Commit();

                    AppLogger.LogInfo($"BA.Materials: applied library asset '{assetName}' to material '{material.Name}' as new appearance asset '{uniqueName}'.");

                    return new AssetApplyResult { Success = true, NewAppearanceAssetElementId = newElement.Id };
                }
                catch (Exception ex)
                {
                    if (t.HasStarted() && !t.HasEnded())
                        t.RollBack();

                    AppLogger.LogError("AssetLibraryBrowserService.ApplyAssetToMaterial", ex);

                    return new AssetApplyResult
                    {
                        Success = false,
                        FailureReason = "Failed to apply asset. See BA Tools log for details."
                    };
                }
            }
        }

        private static Asset FindAssetByName(Application application, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            IList<Asset> assets = application.GetAssets(AssetType.Appearance);
            return assets?.FirstOrDefault(a => string.Equals(a?.Name, name, StringComparison.Ordinal));
        }

        private static bool HasGenericSchema(Asset asset)
        {
            if (asset == null) return false;

            for (int i = 0; i < asset.Size; i++)
            {
                AssetProperty prop = asset[i];
                if (prop != null && string.Equals(prop.Name, GenericSchemaMarkerProperty, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static string GenerateUniqueAssetElementName(Document doc, string baseName)
        {
            HashSet<string> existingNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(AppearanceAssetElement))
                    .Cast<AppearanceAssetElement>()
                    .Select(a => a.Name),
                StringComparer.OrdinalIgnoreCase);

            if (!existingNames.Contains(baseName))
                return baseName;

            int suffix = 2;
            string candidate;
            do
            {
                candidate = $"{baseName} ({suffix})";
                suffix++;
            } while (existingNames.Contains(candidate));

            return candidate;
        }
    }
}
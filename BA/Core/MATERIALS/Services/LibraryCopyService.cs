// Path: BA\Materials\LibraryCopyService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using BA.BAApplication;

namespace BA.Materials
{
    public sealed class CopyIntoProjectResult
    {
        public bool Success { get; set; }
        public ElementId ResultMaterialId { get; set; } = ElementId.InvalidElementId;
        public string FailureReason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Handles the Load Into Project action. Two distinct code paths depending on the
    /// conflict-resolution policy agreed for the UI (silent copy on no conflict, prompt
    /// on conflict, Keep vs Overwrite):
    ///
    /// - No conflict / Keep project version: ElementTransformUtils.CopyElements, the
    ///   normal, fully supported Document-to-Document copy.
    /// - Overwrite from library: NOT done via CopyElements. CopyElements' duplicate name
    ///   handler can only choose "abort" or "keep the existing destination element", it
    ///   cannot push the incoming element's property values onto an existing destination
    ///   element. True overwrite is implemented here by reading the library material's
    ///   Identity/Graphics/Appearance through the existing reader/writer services and
    ///   applying those values directly onto the existing project material by Id.
    ///
    /// Must be called on Revit's API thread.
    /// </summary>
    public sealed class LibraryCopyService
    {
        private readonly MaterialIdentityService _identityService;
        private readonly MaterialGraphicsService _graphicsService;
        private readonly MaterialAssetReader _assetReader;
        private readonly MaterialAssetWriter _assetWriter;

        public LibraryCopyService(
            MaterialIdentityService identityService,
            MaterialGraphicsService graphicsService,
            MaterialAssetReader assetReader,
            MaterialAssetWriter assetWriter)
        {
            _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
            _graphicsService = graphicsService ?? throw new ArgumentNullException(nameof(graphicsService));
            _assetReader = assetReader ?? throw new ArgumentNullException(nameof(assetReader));
            _assetWriter = assetWriter ?? throw new ArgumentNullException(nameof(assetWriter));
        }

        /// <summary>
        /// Returns the ElementId of an existing Material in targetDoc with the given
        /// name, or ElementId.InvalidElementId if no conflict exists. Call this before
        /// CopyIntoProjectAsNew to decide whether the UI needs to show the conflict prompt.
        /// </summary>
        public ElementId FindExistingMaterialByName(Document targetDoc, string materialName)
        {
            if (targetDoc == null) throw new ArgumentNullException(nameof(targetDoc));
            if (string.IsNullOrWhiteSpace(materialName)) return ElementId.InvalidElementId;

            Material existing = new FilteredElementCollector(targetDoc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m => string.Equals(m.Name, materialName, StringComparison.OrdinalIgnoreCase));

            return existing?.Id ?? ElementId.InvalidElementId;
        }

        /// <summary>
        /// No-conflict path: straight CopyElements from the library document into the
        /// active project document. Brings the Material and its linked
        /// AppearanceAssetElement across in one call.
        /// </summary>
        public CopyIntoProjectResult CopyIntoProjectAsNew(Document libraryDoc, Document targetDoc, ElementId libraryMaterialId)
        {
            if (libraryDoc == null) throw new ArgumentNullException(nameof(libraryDoc));
            if (targetDoc == null) throw new ArgumentNullException(nameof(targetDoc));
            if (libraryMaterialId == null || libraryMaterialId == ElementId.InvalidElementId)
                throw new ArgumentException("libraryMaterialId must be valid.", nameof(libraryMaterialId));

            using (Transaction t = new Transaction(targetDoc, "BA Tools: Load material from library"))
            {
                try
                {
                    t.Start();

                    CopyPasteOptions options = new CopyPasteOptions();
                    options.SetDuplicateTypeNamesHandler(new KeepDestinationDuplicateHandler());

                    ICollection<ElementId> copiedIds = ElementTransformUtils.CopyElements(
                        libraryDoc,
                        new List<ElementId> { libraryMaterialId },
                        targetDoc,
                        Transform.Identity,
                        options);

                    t.Commit();

                    ElementId resultId = copiedIds.FirstOrDefault() ?? ElementId.InvalidElementId;

                    AppLogger.LogInfo($"BA.Materials: copied material id {libraryMaterialId.Value} from library into project as id {resultId.Value}");

                    return new CopyIntoProjectResult { Success = true, ResultMaterialId = resultId };
                }
                catch (Exception ex)
                {
                    if (t.HasStarted() && !t.HasEnded())
                        t.RollBack();

                    AppLogger.LogError("LibraryCopyService.CopyIntoProjectAsNew", ex);

                    return new CopyIntoProjectResult
                    {
                        Success = false,
                        FailureReason = "Failed to copy material from library. See BA Tools log for details."
                    };
                }
            }
        }

        /// <summary>
        /// Overwrite path: reads Identity/Graphics/Appearance from the library material
        /// and applies those values onto an existing project material by Id, in place.
        /// The existing project material must already have a valid AppearanceAssetId,
        /// this method does not create a new appearance asset for a material that has
        /// none, if that case comes up in practice, use the Appearance tab's New flow
        /// instead and re-run this afterward.
        /// </summary>
        public CopyIntoProjectResult OverwriteExisting(
            Document libraryDoc,
            Document targetDoc,
            ElementId libraryMaterialId,
            ElementId targetMaterialId)
        {
            if (libraryDoc == null) throw new ArgumentNullException(nameof(libraryDoc));
            if (targetDoc == null) throw new ArgumentNullException(nameof(targetDoc));

            Material libraryMaterial = libraryDoc.GetElement(libraryMaterialId) as Material;
            Material targetMaterial = targetDoc.GetElement(targetMaterialId) as Material;

            if (libraryMaterial == null)
            {
                return new CopyIntoProjectResult
                {
                    Success = false,
                    FailureReason = "Source library material could not be found."
                };
            }

            if (targetMaterial == null)
            {
                return new CopyIntoProjectResult
                {
                    Success = false,
                    FailureReason = "Target project material could not be found."
                };
            }

            if (targetMaterial.AppearanceAssetId == ElementId.InvalidElementId)
            {
                return new CopyIntoProjectResult
                {
                    Success = false,
                    FailureReason = $"'{targetMaterial.Name}' has no appearance asset assigned yet, cannot overwrite. Use New Material instead."
                };
            }

            AppearanceAssetElement libraryAssetElement =
                libraryDoc.GetElement(libraryMaterial.AppearanceAssetId) as AppearanceAssetElement;

            if (libraryAssetElement == null)
            {
                return new CopyIntoProjectResult
                {
                    Success = false,
                    FailureReason = $"Library material '{libraryMaterial.Name}' has no appearance asset to read from."
                };
            }

            var identity = _identityService.GetIdentity(libraryMaterial);
            identity.Name = targetMaterial.Name; // do not rename the target material as a side effect of overwrite
            var graphics = _graphicsService.GetGraphics(libraryMaterial);
            var channels = _assetReader.ReadChannelSet(libraryAssetElement, targetMaterial.Name);

            var identityResult = _identityService.SetIdentity(targetDoc, targetMaterialId, identity);
            if (!identityResult.Success)
            {
                return new CopyIntoProjectResult { Success = false, FailureReason = identityResult.FailureReason };
            }

            var graphicsResult = _graphicsService.SetGraphics(targetDoc, targetMaterialId, graphics);
            if (!graphicsResult.Success)
            {
                return new CopyIntoProjectResult { Success = false, FailureReason = graphicsResult.FailureReason };
            }

            var assetResult = _assetWriter.WriteChannels(targetDoc, targetMaterial.AppearanceAssetId, channels);
            if (!assetResult.Success)
            {
                return new CopyIntoProjectResult { Success = false, FailureReason = assetResult.FailureReason };
            }

            AppLogger.LogInfo($"BA.Materials: overwrote project material id {targetMaterialId.Value} from library material id {libraryMaterialId.Value}");

            return new CopyIntoProjectResult { Success = true, ResultMaterialId = targetMaterialId };
        }

        /// <summary>
        /// Default duplicate-name handler for CopyIntoProjectAsNew: always keeps
        /// whatever already exists in the destination for any incidental duplicate
        /// (e.g. a shared FillPatternElement referenced by the material's Surface/Cut
        /// pattern that already exists by name in the target document). This is only
        /// reached for elements OTHER than the material itself, since the material name
        /// conflict is checked upfront via FindExistingMaterialByName before this is called.
        /// </summary>
        private sealed class KeepDestinationDuplicateHandler : IDuplicateTypeNamesHandler
        {
            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
            {
                return DuplicateTypeAction.UseDestinationTypes;
            }
        }
    }
}

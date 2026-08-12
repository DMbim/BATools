// Path: BA\Materials\MaterialGraphicsService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.BAApplication;

namespace BA.Materials
{
    public sealed class MaterialGraphicsInfo
    {
        public bool UseRenderAppearanceForShading { get; set; }
        public byte ShadingColorR { get; set; } = 120;
        public byte ShadingColorG { get; set; } = 120;
        public byte ShadingColorB { get; set; } = 120;

        /// <summary>0-100, matches Material.Transparency directly. Not 0-1.</summary>
        public int Transparency { get; set; }

        public ElementId SurfaceForegroundPatternId { get; set; } = ElementId.InvalidElementId;
        public byte SurfaceForegroundColorR { get; set; } = 120;
        public byte SurfaceForegroundColorG { get; set; } = 120;
        public byte SurfaceForegroundColorB { get; set; } = 120;

        public ElementId SurfaceBackgroundPatternId { get; set; } = ElementId.InvalidElementId;
        public byte SurfaceBackgroundColorR { get; set; } = 120;
        public byte SurfaceBackgroundColorG { get; set; } = 120;
        public byte SurfaceBackgroundColorB { get; set; } = 120;

        public ElementId CutForegroundPatternId { get; set; } = ElementId.InvalidElementId;
        public byte CutForegroundColorR { get; set; } = 120;
        public byte CutForegroundColorG { get; set; } = 120;
        public byte CutForegroundColorB { get; set; } = 120;

        public ElementId CutBackgroundPatternId { get; set; } = ElementId.InvalidElementId;
        public byte CutBackgroundColorR { get; set; } = 120;
        public byte CutBackgroundColorG { get; set; } = 120;
        public byte CutBackgroundColorB { get; set; } = 120;
    }

    public sealed class MaterialGraphicsResult
    {
        public bool Success { get; set; }
        public string FailureReason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Reads/writes the Graphics tab (Shading, Surface Pattern, Cut Pattern) of a
    /// Material. Plain transaction, no AppearanceAssetEditScope involved, these are
    /// direct Material properties. Must be called on Revit's API thread.
    ///
    /// Texture Alignment (the "Texture Alignment..." button under Surface Pattern >
    /// Foreground in native Revit) is intentionally NOT implemented here, per the
    /// agreed v1 scope it is deferred, the UI button should be disabled with a tooltip
    /// pointing to native Revit for that specific edit.
    /// </summary>
    public sealed class MaterialGraphicsService
    {
        public MaterialGraphicsInfo GetGraphics(Material material)
        {
            if (material == null)
                throw new ArgumentNullException(nameof(material));

            Color shading = material.Color;
            Color surfFg = material.SurfaceForegroundPatternColor;
            Color surfBg = material.SurfaceBackgroundPatternColor;
            Color cutFg = material.CutForegroundPatternColor;
            Color cutBg = material.CutBackgroundPatternColor;

            return new MaterialGraphicsInfo
            {
                UseRenderAppearanceForShading = material.UseRenderAppearanceForShading,
                ShadingColorR = shading.Red,
                ShadingColorG = shading.Green,
                ShadingColorB = shading.Blue,
                Transparency = material.Transparency,

                SurfaceForegroundPatternId = material.SurfaceForegroundPatternId,
                SurfaceForegroundColorR = surfFg.Red,
                SurfaceForegroundColorG = surfFg.Green,
                SurfaceForegroundColorB = surfFg.Blue,

                SurfaceBackgroundPatternId = material.SurfaceBackgroundPatternId,
                SurfaceBackgroundColorR = surfBg.Red,
                SurfaceBackgroundColorG = surfBg.Green,
                SurfaceBackgroundColorB = surfBg.Blue,

                CutForegroundPatternId = material.CutForegroundPatternId,
                CutForegroundColorR = cutFg.Red,
                CutForegroundColorG = cutFg.Green,
                CutForegroundColorB = cutFg.Blue,

                CutBackgroundPatternId = material.CutBackgroundPatternId,
                CutBackgroundColorR = cutBg.Red,
                CutBackgroundColorG = cutBg.Green,
                CutBackgroundColorB = cutBg.Blue
            };
        }

        public MaterialGraphicsResult SetGraphics(Document doc, ElementId materialId, MaterialGraphicsInfo info)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (materialId == null || materialId == ElementId.InvalidElementId)
                throw new ArgumentException("materialId must be a valid ElementId.", nameof(materialId));
            if (info == null) throw new ArgumentNullException(nameof(info));

            Material material = doc.GetElement(materialId) as Material;
            if (material == null)
            {
                return new MaterialGraphicsResult
                {
                    Success = false,
                    FailureReason = "Element is not a Material, or no longer exists in the document."
                };
            }

            using (Transaction t = new Transaction(doc, "BA Tools: Update material graphics"))
            {
                try
                {
                    t.Start();

                    material.UseRenderAppearanceForShading = info.UseRenderAppearanceForShading;
                    material.Color = new Color(info.ShadingColorR, info.ShadingColorG, info.ShadingColorB);
                    material.Transparency = Math.Max(0, Math.Min(100, info.Transparency));

                    material.SurfaceForegroundPatternId = info.SurfaceForegroundPatternId ?? ElementId.InvalidElementId;
                    material.SurfaceForegroundPatternColor = new Color(
                        info.SurfaceForegroundColorR, info.SurfaceForegroundColorG, info.SurfaceForegroundColorB);

                    material.SurfaceBackgroundPatternId = info.SurfaceBackgroundPatternId ?? ElementId.InvalidElementId;
                    material.SurfaceBackgroundPatternColor = new Color(
                        info.SurfaceBackgroundColorR, info.SurfaceBackgroundColorG, info.SurfaceBackgroundColorB);

                    material.CutForegroundPatternId = info.CutForegroundPatternId ?? ElementId.InvalidElementId;
                    material.CutForegroundPatternColor = new Color(
                        info.CutForegroundColorR, info.CutForegroundColorG, info.CutForegroundColorB);

                    material.CutBackgroundPatternId = info.CutBackgroundPatternId ?? ElementId.InvalidElementId;
                    material.CutBackgroundPatternColor = new Color(
                        info.CutBackgroundColorR, info.CutBackgroundColorG, info.CutBackgroundColorB);

                    t.Commit();

                    AppLogger.LogInfo($"BA.Materials: updated graphics for material '{material.Name}' (id {materialId.Value})");

                    return new MaterialGraphicsResult { Success = true };
                }
                catch (Exception ex)
                {
                    if (t.HasStarted() && !t.HasEnded())
                        t.RollBack();

                    AppLogger.LogError("MaterialGraphicsService.SetGraphics", ex);

                    return new MaterialGraphicsResult
                    {
                        Success = false,
                        FailureReason = "Failed to update material graphics. See BA Tools log for details."
                    };
                }
            }
        }

        /// <summary>
        /// Drafting fill patterns available in the document, for the Surface/Cut
        /// pattern pickers. Matches what native Revit's Graphics tab pattern dropdowns
        /// show. Includes a leading "&lt;none&gt;" entry represented as InvalidElementId,
        /// callers should render that as the "&lt;none&gt;" option.
        /// </summary>
        public List<FillPatternElement> GetAvailableDraftingPatterns(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            return new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .Where(fp => fp.GetFillPattern()?.Target == FillPatternTarget.Drafting)
                .OrderBy(fp => fp.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
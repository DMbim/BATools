// File: BA/Core/CurveToElement/Services/WallFaceOffsetPreviewCalculator.cs
// Action: CREATE NEW

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.Core.CurveToElement.Models;

namespace BA.Core.CurveToElement.Services
{
    /// <summary>
    /// Computes, for display only, where each WallLocationLine reference sits relative to a
    /// WallType's raw creation centerline. Pure CompoundStructure arithmetic - does not read
    /// or write any element instance data, does not open a transaction. Must still be called
    /// from a valid Revit API context (main thread / ExternalEvent handler), since it reads
    /// WallType and Document.GetUnits().
    /// </summary>
    public class WallFaceOffsetPreviewCalculator
    {
        public WallFaceOffsetPreview Calculate(Document doc, WallType wallType)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (wallType == null) throw new ArgumentNullException(nameof(wallType));

            if (wallType.Kind != WallKind.Basic)
            {
                string reason = $"WallType '{wallType.Name}' is {wallType.Kind}, not Basic - no single compound structure to preview.";
                AppLogger.LogInfo($"[CurveToElement] {reason}");
                return WallFaceOffsetPreview.Unsupported(reason);
            }

            CompoundStructure cs = wallType.GetCompoundStructure();
            if (cs == null)
            {
                string reason = $"WallType '{wallType.Name}' has no compound structure definition.";
                AppLogger.LogInfo($"[CurveToElement] {reason}");
                return WallFaceOffsetPreview.Unsupported(reason);
            }

            IList<CompoundStructureLayer> layers = cs.GetLayers();
            if (layers == null || layers.Count == 0)
            {
                string reason = $"WallType '{wallType.Name}' compound structure has no layers.";
                AppLogger.LogInfo($"[CurveToElement] {reason}");
                return WallFaceOffsetPreview.Unsupported(reason);
            }

            int firstCoreIndex = cs.GetFirstCoreLayerIndex();
            int lastCoreIndex = cs.GetLastCoreLayerIndex();

            if (firstCoreIndex < 0 || lastCoreIndex < 0 || firstCoreIndex >= layers.Count || lastCoreIndex >= layers.Count)
            {
                string reason = $"WallType '{wallType.Name}' has no identifiable core layer boundaries.";
                AppLogger.LogInfo($"[CurveToElement] {reason}");
                return WallFaceOffsetPreview.Unsupported(reason);
            }

            // Cumulative width from Side1 face (offset 0) to each layer boundary.
            double totalWidth = 0.0;
            double coreStartFromSide1 = 0.0;
            double coreEndFromSide1 = 0.0;
            double runningWidth = 0.0;

            for (int i = 0; i < layers.Count; i++)
            {
                double layerWidth = layers[i].Width;

                if (i == firstCoreIndex)
                    coreStartFromSide1 = runningWidth;

                runningWidth += layerWidth;

                if (i == lastCoreIndex)
                    coreEndFromSide1 = runningWidth;
            }
            totalWidth = runningWidth;

            if (totalWidth <= 1e-9)
            {
                string reason = $"WallType '{wallType.Name}' has zero total width.";
                AppLogger.LogInfo($"[CurveToElement] {reason}");
                return WallFaceOffsetPreview.Unsupported(reason);
            }

            double centerlineFromSide1 = totalWidth / 2.0;
            double coreCenterlineFromSide1 = (coreStartFromSide1 + coreEndFromSide1) / 2.0;

            double coreCenterlineOffset = coreCenterlineFromSide1 - centerlineFromSide1;
            double coreSide1FaceOffset = coreStartFromSide1 - centerlineFromSide1;
            double coreSide2FaceOffset = coreEndFromSide1 - centerlineFromSide1;
            double finishSide1FaceOffset = 0.0 - centerlineFromSide1;
            double finishSide2FaceOffset = totalWidth - centerlineFromSide1;

            return WallFaceOffsetPreview.Supported(
                totalWidth,
                coreCenterlineOffset,
                coreSide1FaceOffset,
                coreSide2FaceOffset,
                finishSide1FaceOffset,
                finishSide2FaceOffset);
        }

        /// <summary>
        /// Formats an internal-units offset value using the document's current project units
        /// (honors whatever length unit/precision the user has set - feet-inches, mm, etc.)
        /// for direct display in the settings panel.
        /// </summary>
        public string FormatOffset(Document doc, double internalUnitsValue)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            Units units = doc.GetUnits();
            return UnitFormatUtils.Format(units, SpecTypeId.Length, internalUnitsValue, false);
        }
    }
}
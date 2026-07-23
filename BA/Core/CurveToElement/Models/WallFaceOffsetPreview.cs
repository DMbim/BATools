// File: BA/Core/CurveToElement/Models/WallFaceOffsetPreview.cs
// Action: CREATE NEW

using Autodesk.Revit.DB;

namespace BA.Core.CurveToElement.Models
{
    /// <summary>
    /// Read-only, informational-only preview of where each WallLocationLine option sits
    /// relative to the wall's raw creation centerline (the curve passed to Wall.Create).
    /// All offsets are signed distances in internal units (feet): positive = toward Side2,
    /// negative = toward Side1. This does NOT drive wall generation - WallLocationLineResolver
    /// is the actual mechanism used at creation time.
    /// </summary>
    public class WallFaceOffsetPreview
    {
        public bool IsSupported { get; }
        public string UnsupportedReason { get; }

        public double TotalWidth { get; }
        public double CoreCenterlineOffset { get; }
        public double CoreSide1FaceOffset { get; }
        public double CoreSide2FaceOffset { get; }
        public double FinishSide1FaceOffset { get; }
        public double FinishSide2FaceOffset { get; }

        private WallFaceOffsetPreview(
            bool isSupported,
            string unsupportedReason,
            double totalWidth,
            double coreCenterlineOffset,
            double coreSide1FaceOffset,
            double coreSide2FaceOffset,
            double finishSide1FaceOffset,
            double finishSide2FaceOffset)
        {
            IsSupported = isSupported;
            UnsupportedReason = unsupportedReason;
            TotalWidth = totalWidth;
            CoreCenterlineOffset = coreCenterlineOffset;
            CoreSide1FaceOffset = coreSide1FaceOffset;
            CoreSide2FaceOffset = coreSide2FaceOffset;
            FinishSide1FaceOffset = finishSide1FaceOffset;
            FinishSide2FaceOffset = finishSide2FaceOffset;
        }

        public static WallFaceOffsetPreview Unsupported(string reason)
        {
            return new WallFaceOffsetPreview(false, reason, 0, 0, 0, 0, 0, 0);
        }

        public static WallFaceOffsetPreview Supported(
            double totalWidth,
            double coreCenterlineOffset,
            double coreSide1FaceOffset,
            double coreSide2FaceOffset,
            double finishSide1FaceOffset,
            double finishSide2FaceOffset)
        {
            return new WallFaceOffsetPreview(
                true, null, totalWidth, coreCenterlineOffset,
                coreSide1FaceOffset, coreSide2FaceOffset,
                finishSide1FaceOffset, finishSide2FaceOffset);
        }

        /// <summary>
        /// Returns the offset that corresponds to a given WallLocationLine value.
        /// Used by the settings panel to show only the single relevant value for whatever
        /// LocationLine the user currently has selected for the group.
        /// </summary>
        public double? GetOffsetFor(WallLocationLine locationLine)
        {
            if (!IsSupported) return null;

            switch (locationLine)
            {
                case WallLocationLine.WallCenterline:
                    return 0.0;
                case WallLocationLine.CoreCenterline:
                    return CoreCenterlineOffset;
                case WallLocationLine.FinishFaceExterior:
                    return FinishSide1FaceOffset;
                case WallLocationLine.FinishFaceInterior:
                    return FinishSide2FaceOffset;
                case WallLocationLine.CoreExterior:
                    return CoreSide1FaceOffset;
                case WallLocationLine.CoreInterior:
                    return CoreSide2FaceOffset;
                default:
                    return null;
            }
        }
    }
}
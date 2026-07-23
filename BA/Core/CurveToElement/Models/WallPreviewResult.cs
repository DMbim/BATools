// File: BA/Core/CurveToElement/Models/WallPreviewResult.cs
// Action: CREATE NEW

using System;

namespace BA.Core.CurveToElement.Models
{
    public class WallPreviewResult
    {
        public Guid GroupId { get; }
        public WallFaceOffsetPreview Preview { get; }
        public string FormattedTotalWidth { get; }
        public string FormattedCoreCenterline { get; }
        public string FormattedCoreSide1Face { get; }
        public string FormattedCoreSide2Face { get; }
        public string FormattedFinishSide1Face { get; }
        public string FormattedFinishSide2Face { get; }

        public WallPreviewResult(
            Guid groupId,
            WallFaceOffsetPreview preview,
            string formattedTotalWidth,
            string formattedCoreCenterline,
            string formattedCoreSide1Face,
            string formattedCoreSide2Face,
            string formattedFinishSide1Face,
            string formattedFinishSide2Face)
        {
            GroupId = groupId;
            Preview = preview ?? throw new ArgumentNullException(nameof(preview));
            FormattedTotalWidth = formattedTotalWidth;
            FormattedCoreCenterline = formattedCoreCenterline;
            FormattedCoreSide1Face = formattedCoreSide1Face;
            FormattedCoreSide2Face = formattedCoreSide2Face;
            FormattedFinishSide1Face = formattedFinishSide1Face;
            FormattedFinishSide2Face = formattedFinishSide2Face;
        }
    }
}
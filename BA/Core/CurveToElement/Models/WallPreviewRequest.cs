// File: BA/Core/CurveToElement/Models/WallPreviewRequest.cs
// Action: CREATE NEW

using System;
using Autodesk.Revit.DB;

namespace BA.Core.CurveToElement.Models
{
    /// <summary>
    /// A single group's request to recompute its face offset preview against a candidate
    /// WallType. GroupId ties the async result back to the correct CurveTypeGroupViewModel.
    /// </summary>
    public class WallPreviewRequest
    {
        public Guid GroupId { get; }
        public ElementId WallTypeId { get; }

        public WallPreviewRequest(Guid groupId, ElementId wallTypeId)
        {
            GroupId = groupId;
            WallTypeId = wallTypeId ?? ElementId.InvalidElementId;
        }
    }
}
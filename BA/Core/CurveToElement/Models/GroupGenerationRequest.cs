// File: BA/Core/CurveToElement/Models/GroupGenerationRequest.cs
// Action: CREATE NEW

using System.Collections.Generic;

namespace BA.Core.CurveToElement.Models
{
    /// <summary>
    /// One group's fully-validated generation payload, assembled by
    /// CurveToElementWindowViewModel.ExecuteGenerate and handed to whatever consumes
    /// RequestGenerate (WallGenerationService, via ExternalEvent, next).
    /// </summary>
    public class GroupGenerationRequest
    {
        public CurveTypeGroup Group { get; }
        public IReadOnlyList<CurveChain> Chains { get; }
        public WallGroupSettings Settings { get; }

        public GroupGenerationRequest(CurveTypeGroup group, IReadOnlyList<CurveChain> chains, WallGroupSettings settings)
        {
            Group = group;
            Chains = chains;
            Settings = settings;
        }
    }
}
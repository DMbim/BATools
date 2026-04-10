using System;

namespace BA.Core.Models
{
    public sealed record ComputationAuditMetadata
    {
        public required DateTime ComputedAtUtc { get; init; }
        public required string ComputationMethod { get; init; }
        public required string AppliedNormCitation { get; init; }
        public required DateOnly NormValidFrom { get; init; }
        public int ProcessedElementCount { get; init; }
        public string? Notes { get; init; }
    }
}
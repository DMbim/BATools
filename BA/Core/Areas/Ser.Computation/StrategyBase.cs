using System;
using BA.Core.Interfaces;
using BA.Core.Models;

namespace BA.Services.Computation
{
    /// <summary>
    /// Sdílené pomocné metody pro všechny strategie.
    /// </summary>
    public abstract class StrategyBase
    {
        protected readonly INormInfoProvider NormInfoProvider;

        protected StrategyBase(INormInfoProvider normInfoProvider)
        {
            NormInfoProvider = normInfoProvider
                ?? throw new ArgumentNullException(nameof(normInfoProvider));
        }

        protected ComputationAuditMetadata BuildAudit(
            BA.Core.Enums.AreaType areaType,
            string method,
            int elementCount,
            string? notes = null)
        {
            var norm = NormInfoProvider.GetNormInfo(areaType);
            return new ComputationAuditMetadata
            {
                ComputedAtUtc = DateTime.UtcNow,
                ComputationMethod = method,
                AppliedNormCitation = norm.LegalCitation,
                NormValidFrom = norm.ValidFrom,
                ProcessedElementCount = elementCount,
                Notes = notes
            };
        }
    }
}
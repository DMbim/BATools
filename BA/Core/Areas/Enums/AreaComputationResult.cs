using System.Collections.Generic;
using Autodesk.Revit.DB;
using BA.Core.Enums;

namespace BA.Core.Models
{
    /// <summary>
    /// Immutabilní výsledek výpočtu jedné plochy pro jeden zdrojový element.
    /// Obsahuje kompletní audit trail zpětně dohledatelný k právnímu předpisu.
    /// </summary>
    public sealed record AreaComputationResult
    {
        public required ElementId SourceElementId { get; init; }
        public required string SourceElementName { get; init; }
        public required AreaType AreaType { get; init; }
        public required double ComputedAreaM2 { get; init; }
        public required ComputationStatus Status { get; init; }
        public required ComputationAuditMetadata Audit { get; init; }
        public required IReadOnlyList<AreaDeduction> Deductions { get; init; }

        /// <summary>
        /// Výsledné hranice plochy v Revit interních jednotkách.
        /// Použito pro vizualizaci FilledRegion.
        /// </summary>
        public required IReadOnlyList<CurveLoop> ComputedBoundary { get; init; }

        public string? ErrorMessage { get; init; }

        /// <summary>
        /// Pouze pro HPP: klasifikace podlaží dle PSP §2 g).
        /// </summary>
        public FloorClassification? FloorClassification { get; init; }
    }
}


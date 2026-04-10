using System.Collections.Generic;
using Autodesk.Revit.DB;
using BA.Core.Enums;

namespace BA.Core.Models
{
    public sealed record AreaDeduction
    {
        public required DeductionType Type { get; init; }
        public required double DeductedAreaM2 { get; init; }
        public required string LegalBasis { get; init; }

        /// <summary>
        /// Geometrie odečtené plochy v Revit interních jednotkách (stopy).
        /// Null pokud deduction je koeficientová (SpaceTypeMultiplier).
        /// </summary>
        public IReadOnlyList<CurveLoop>? DeductedGeometry { get; init; }
    }
}


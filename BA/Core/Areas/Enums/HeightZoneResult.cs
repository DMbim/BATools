using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitLevel = Autodesk.Revit.DB.Level;

namespace BA.Core.Models
{
    /// <summary>
    /// Výsledek analýzy výškových zón místnosti dle NV 366/2013 §4 odst. 2.
    /// FullZone:  výška >= 2.0 m → koef. 1.0
    /// HalfZone:  výška 1.2–2.0 m → koef. 0.5
    /// ZeroZone:  výška < 1.2 m → koef. 0.0
    /// </summary>
    public sealed record HeightZoneResult
    {
        public required IReadOnlyList<CurveLoop> FullZoneLoops { get; init; }
        public required IReadOnlyList<CurveLoop> HalfZoneLoops { get; init; }
        public required IReadOnlyList<CurveLoop> ZeroZoneLoops { get; init; }

        public double FullZoneAreaM2 { get; init; }
        public double HalfZoneAreaM2 { get; init; }
        public double ZeroZoneAreaM2 { get; init; }
    }
}

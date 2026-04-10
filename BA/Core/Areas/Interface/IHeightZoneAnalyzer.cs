using System.Collections.Generic;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using BA.Core.Models;

namespace BA.Core.Interfaces
{
    public interface IHeightZoneAnalyzer
    {
        /// <summary>
        /// Analyzuje výškové zóny místnosti raycasting gridem.
        /// Vrací rozdělení plochy dle NV 366/2013 §4 odst. 2.
        /// </summary>
        Task<HeightZoneResult> AnalyzeAsync(
            Room room,
            Solid roomSolid,
            IReadOnlyList<CurveLoop> floorLoops,
            Document document);
    }
}

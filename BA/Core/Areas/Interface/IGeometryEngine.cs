using System.Collections.Generic;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitLevel = Autodesk.Revit.DB.Level;
namespace BA.Core.Interfaces
{
    public interface IGeometryEngine
    {
        /// <summary>
        /// Extrahuje spodní průmět (floor projection) z room solidu jako CurveLoop kolekci.
        /// </summary>
        IReadOnlyList<CurveLoop> ExtractFloorProjectionLoops(Solid roomSolid);

        /// <summary>
        /// Vypočítá plochu CurveLoop v m². Používá ExporterIFCUtils s fallback na shoelace.
        /// </summary>
        double ComputeLoopAreaM2(CurveLoop loop);

        /// <summary>
        /// Sestaví outer shell footprint ze stěn podlaží — pro HPP výpočet.
        /// Měří k vnějšímu líci obvodových konstrukcí dle PSP §2 c).
        Task<IReadOnlyList<CurveLoop>> BuildOuterShellFootprintAsync(
            IReadOnlyList<Wall> walls,
            RevitLevel level,               // ← alias
            Document document);

        /// <summary>
        /// Sestaví footprint budovy pro zastavěnou plochu dle SZ §13 o).
        /// Zahrnuje přesahy > 0.5 m.
        /// </summary>
        Task<IReadOnlyList<CurveLoop>> BuildBuildingFootprintAsync(
            IReadOnlyList<Element> groundFloorElements,
            double overhangThresholdM,
            Document document);

        /// <summary>
        /// Odečte díry (sloupy, stěny) z vnější loop pomocí Clipper2.
        /// </summary>
        CurveLoop? SubtractHolesFromLoop(
            CurveLoop outerLoop,
            IReadOnlyList<CurveLoop> holes,
            double elevationFt);
    }
}

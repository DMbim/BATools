using Autodesk.Revit.DB;
using BA.Core.Enums;

namespace BA.Services
{
    /// <summary>
    /// Klasifikuje podlaží na nadzemní / podzemní dle PSP §2 písm. g).
    /// Podzemní podlaží: úroveň podlahy níže než 800 mm pod průměrným upraveným terénem.
    /// </summary>
    public sealed class HPPClassifier
    {
        /// <summary>
        /// Práh dle PSP §2 g) v milimetrech (relativně k průměrnému UT).
        /// </summary>
        private const double PodzemniThresholdMm = -800.0;

        /// <summary>
        /// Klasifikuje Level jako nadzemní nebo podzemní.
        /// </summary>
        /// <param name="level">Revit Level element.</param>
        /// <param name="averageTerenElevationMm">
        /// Průměrná výška upraveného terénu v mm v souřadnicích Revit projektu.
        /// Kladná hodnota = terén nad projektovou nulou.
        /// </param>
        public FloorClassification Classify(Level level, double averageTerenElevationMm)
        {
            double levelElevationMm = UnitUtils.ConvertFromInternalUnits(
                level.Elevation,
                UnitTypeId.Millimeters);

            double deltaFromTeren = levelElevationMm - averageTerenElevationMm;

            return deltaFromTeren < PodzemniThresholdMm
                ? FloorClassification.Podzemni
                : FloorClassification.Nadzemni;
        }
    }
}


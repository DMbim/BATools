namespace BA.UI.Core.Finishes
{
    internal static class UnitUtil
    {
        private const double FtToMm = 304.8;
        public static double MmToFt(double mm) => mm / FtToMm;
        public static double FtToMmVal(double ft) => ft * FtToMm;
    }
}
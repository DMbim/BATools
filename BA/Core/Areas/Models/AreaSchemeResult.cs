using Autodesk.Revit.DB;

namespace BA.Core.AreaSchemes.Models
{
    public sealed class AreaSchemeResult
    {
        public Level Level { get; init; } = null!;
        public double LA { get; set; }
        public double NLA { get; set; }
        public double GFA { get; set; }
        public double ECA { get; set; }
        public double IFA { get; set; }
        public double ICA { get; set; }
        public double NFA { get; set; }
        public double PWA { get; set; }
        public double NRA { get; set; }

        public void Compute()
        {
            GFA = LA - NLA;
            IFA = GFA - ECA;
            NFA = IFA - ICA;
            NRA = NFA - PWA;
        }
    }
}
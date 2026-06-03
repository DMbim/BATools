namespace BA.Core.AreaSchemes.Constants
{
    public static class AreaSchemeConstants
    {
        // Area scheme names — must match exactly what's in Revit
        public const string LA = "1_Level Area (LA)";
        public const string NLA = "2_Non-Functional Level Area (NLA)";
        public const string GFA = "3_Gross Area (GFA)";
        public const string ECA = "4_Exterior Construction Area (ECA)";
        public const string IFA = "5_Internal Floor Area (IFA)";
        public const string ICA = "6_Interior Construction Area (ICA)";
        public const string NFA = "7_Net Floor Are (NFA)";
        public const string PWA = "8_Partition Wall Area (PWA)";
        public const string NRA = "9_Net Room Area (NRA)";

        // Ordered list for wizard steps
        public static readonly string[] OrderedSchemes = new[]
        {
            LA, NLA, GFA, ECA, IFA, ICA, NFA, PWA, NRA
        };

        // Schemes where user picks elements (plugin draws boundaries)
        public static readonly string[] ElementPickSchemes = new[]
        {
            ECA, ICA, PWA
        };

        // Schemes that are purely arithmetic (no user input, no boundary drawing)
        // GFA = LA - NLA
        // IFA = GFA - ECA
        // NFA = IFA - ICA
        // NRA = NFA - PWA
        public static readonly string[] ComputedSchemes = new[]
        {
            GFA, IFA, NFA, NRA
        };

        // BA_AreaType values written to wall/column parameters
        public const string AreaTypeECA = "ECA";
        public const string AreaTypeICA = "ICA";
        public const string AreaTypePWA = "PWA";

        // Shared parameter names — written to Level elements
        public const string ParamLA = "BA_LA";
        public const string ParamNLA = "BA_NLA";
        public const string ParamGFA = "BA_GFA";
        public const string ParamECA = "BA_ECA";
        public const string ParamIFA = "BA_IFA";
        public const string ParamICA = "BA_ICA";
        public const string ParamNFA = "BA_NFA";
        public const string ParamPWA = "BA_PWA";
        public const string ParamNRA = "BA_NRA";

        // Shared parameter on walls/columns
        public const string ParamAreaType = "BA_AreaType";
    }
}
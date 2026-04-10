namespace BA.Services.Computation
{
    /// <summary>
    /// Názvy sdílených parametrů CZA add-inu.
    /// Tyto řetězce musí přesně odpovídat definicím v shared parameter souboru.
    /// </summary>
    public static class SharedParameterConstants
    {
        // Výstupy výpočtu
        public const string PodlahovaPlochaNV366 = "CZA_PodlahovaPloCha_NV366_m2";
        public const string HPPNadzemni = "CZA_HPP_Nadzemni_m2";
        public const string HPPPodzemni = "CZA_HPP_Podzemni_m2";
        public const string PodlahovaPlochaSZ = "CZA_PodlahovaPloCha_SZ_m2";
        public const string ZastavenaPlochaSZ = "CZA_ZastavenaPloCha_SZ_m2";

        // Audit metadata
        public const string LastComputationDate = "CZA_LastComputation_Date";
        public const string AppliedNormCitation = "CZA_AppliedNorm_Citation";
        public const string NormValidFrom = "CZA_Norm_ValidFrom";
        public const string ComputationMethod = "CZA_Computation_Method";
        public const string ComputationStatus = "CZA_Computation_Status";

        // Vstupní klasifikace
        public const string SpaceTypeCzech = "CZA_SpaceType_Czech";
        public const string UpravenyTerenMmNN = "CZA_UpravenyTeren_mmNN";
    }
}

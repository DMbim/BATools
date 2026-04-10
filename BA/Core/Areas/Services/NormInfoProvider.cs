using System;
using BA.Core.Enums;
using BA.Core.Interfaces;
using BA.Core.Models;

namespace BA.Services
{
    /// <summary>
    /// Poskytuje informace o aplikovaných normách.
    /// Pouze informativní — neblokuje výpočet.
    /// Datum ValidFrom = datum účinnosti aktuálního znění.
    /// </summary>
    public sealed class NormInfoProvider : INormInfoProvider
    {
        public NormInfo GetNormInfo(AreaType areaType) => areaType switch
        {
            AreaType.PodlahovaPlochaNV366 => new NormInfo
            {
                DisplayName = "Podlahová plocha",
                LegalCitation = "Nařízení vlády č. 366/2013 Sb. ve znění NV č. 432/2022 Sb.",
                ValidFrom = new DateOnly(2023, 1, 1),
                ScopeNote = null
            },
            AreaType.HPPNadzemni => new NormInfo
            {
                DisplayName = "HPP nadzemní",
                LegalCitation = "Pražské stavební předpisy, Nař. HMP č. 10/2016 Sb. HMP " +
                                "ve znění č. 14/2023 Sb. HMP — §2 písm. c), §2 písm. g)",
                ValidFrom = new DateOnly(2024, 1, 1),
                ScopeNote = "Platí pouze v administrativních hranicích hl. m. Prahy"
            },
            AreaType.HPPPodzemni => new NormInfo
            {
                DisplayName = "HPP podzemní",
                LegalCitation = "Pražské stavební předpisy, Nař. HMP č. 10/2016 Sb. HMP " +
                                "ve znění č. 14/2023 Sb. HMP — §2 písm. c), §2 písm. g)",
                ValidFrom = new DateOnly(2024, 1, 1),
                ScopeNote = "Platí pouze v administrativních hranicích hl. m. Prahy"
            },
            AreaType.PodlahovaPlochaSZ => new NormInfo
            {
                DisplayName = "Podlahová plocha (SZ)",
                LegalCitation = "Zákon č. 283/2021 Sb., stavební zákon — §13 písm. n)",
                ValidFrom = new DateOnly(2024, 7, 1),
                ScopeNote = null
            },
            AreaType.ZastavenaPlochaSZ => new NormInfo
            {
                DisplayName = "Zastavěná plocha",
                LegalCitation = "Zákon č. 283/2021 Sb., stavební zákon — §13 písm. o)",
                ValidFrom = new DateOnly(2024, 7, 1),
                ScopeNote = null
            },
            _ => throw new ArgumentOutOfRangeException(nameof(areaType),
                     $"Neznámý AreaType: {areaType}")
        };
    }
}

using BA.Core.Enums;
using BA.Core.Models;

namespace BA.UI.ViewModels
{
    public sealed class ResultRowViewModel : ViewModelBase
    {
        public required AreaComputationResult Result { get; init; }

        public string ElementName => Result.SourceElementName;
        public string AreaTypeName => GetAreaTypeDisplayName(Result.AreaType);
        public string AreaM2 => Result.ComputedAreaM2.ToString("F2") + " m²";
        public string NormCitation => Result.Audit.AppliedNormCitation;
        public string NormValidFrom => Result.Audit.NormValidFrom.ToString("d. M. yyyy");
        public string StatusText => GetStatusText(Result.Status);
        public bool IsSuccess => Result.Status == ComputationStatus.Success;
        public string? ErrorMessage => Result.ErrorMessage;

        public string FloorClassText => Result.FloorClassification.HasValue
            ? (Result.FloorClassification.Value == FloorClassification.Podzemni
                ? "Podzemní" : "Nadzemní")
            : string.Empty;

        // Detail výpočtu pro expanded view
        public string DeductionSummary
        {
            get
            {
                if (!Result.Deductions.Any())
                    return "Žádné odečty";

                var lines = Result.Deductions
                    .Select(d => $"  − {d.DeductedAreaM2:F3} m²  [{d.LegalBasis}]");

                return string.Join("\n", lines);
            }
        }

        private static string GetAreaTypeDisplayName(AreaType type) => type switch
        {
            AreaType.PodlahovaPlochaNV366 => "Podlahová plocha (NV 366/2013)",
            AreaType.HPPNadzemni => "HPP nadzemní (PSP)",
            AreaType.HPPPodzemni => "HPP podzemní (PSP)",
            AreaType.PodlahovaPlochaSZ => "Podlahová plocha (SZ §13n)",
            AreaType.ZastavenaPlochaSZ => "Zastavěná plocha (SZ §13o)",
            _ => type.ToString()
        };

        private static string GetStatusText(ComputationStatus status) => status switch
        {
            ComputationStatus.Success => "✓ OK",
            ComputationStatus.SkippedNotPlaced => "⊘ Neumístěno",
            ComputationStatus.SkippedInsufficientGeometry => "⚠ Geometrie",
            ComputationStatus.SkippedExcludedByISOCategory => "⊘ Vyloučeno",
            ComputationStatus.Failed => "✕ Chyba",
            _ => status.ToString()
        };
    }
}
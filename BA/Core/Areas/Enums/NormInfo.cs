using System;

namespace BA.Core.Models
{
    /// <summary>
    /// Informační záznam o normě/právním předpisu.
    /// Pouze informativní — neblokuje výpočet.
    /// </summary>
    public sealed record NormInfo
    {
        public required string DisplayName { get; init; }
        public required string LegalCitation { get; init; }
        public required DateOnly ValidFrom { get; init; }
        public string? ScopeNote { get; init; }
    }
}

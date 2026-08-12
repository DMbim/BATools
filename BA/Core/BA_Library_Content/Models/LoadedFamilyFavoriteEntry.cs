using System.Collections.Generic;

namespace BA.Core.Content.Models
{
    public sealed class LoadedFamilyFavoriteEntry
    {
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }
        public List<string> Tags { get; set; } = new();
    }
}
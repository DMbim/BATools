using System.Collections.Generic;

namespace BA.SelectionManager.Models
{
    public class FavoriteFamiliesProfile
    {
        public List<FamilyFavGroupDefinition> Groups { get; set; } = new();
    }
}
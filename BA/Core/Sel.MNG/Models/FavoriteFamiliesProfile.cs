using System.Collections.Generic;

namespace BATools.SelectionManager.Models
{
    public class FavoriteFamiliesProfile
    {
        public List<FamilyFavGroupDefinition> Groups { get; set; } = new();
    }
}
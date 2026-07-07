using System.Collections.Generic;

namespace BATools.SelectionManager.Models
{
    public class ToolbarProfile
    {
        public string ProfileName { get; set; } = "Default";
        public List<ToolbarTabDefinition> Tabs { get; set; } = new();

        public static ToolbarProfile CreateDefault() => new ToolbarProfile
        {
            ProfileName = "Default",
            Tabs = new List<ToolbarTabDefinition>
            {
                new ToolbarTabDefinition
                 {
                    Name = "View",
                    PinnedActionIds = new List<string>(),  // empty — user pins manually
                    Groups = new List<ToolbarGroupDefinition>
                    {
                        new ToolbarGroupDefinition
                        {
                            Name = "Visibility",
                            ActionIds = new List<string>
                                { "isolate_temporary", "hide_elements", "reset_isolation" }
                        },
                        new ToolbarGroupDefinition
                        {
                            Name = "Override",
                            ActionIds = new List<string> { "override_red", "reset_overrides" }
                        }
                    }
                },
                new ToolbarTabDefinition
                {
                    Name = "Sets",
                    PinnedActionIds = new List<string>(),  // empty — user pins manually
                    Groups = new List<ToolbarGroupDefinition>
                    {
                        new ToolbarGroupDefinition
                        {
                            Name = "Selection",
                            ActionIds = new List<string> { "save_selection" }
                        }
                    }
                }
            }
        };
    }
}
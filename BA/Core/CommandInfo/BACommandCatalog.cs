using System.Collections.Generic;
using BA.IssueReporter.Models;

namespace BA.Core.CommandCatalog
{
    public static class BACommandCatalog
    {
        public static IReadOnlyList<BACommandInfo> All { get; } =
            new List<BACommandInfo>
            {
                new BACommandInfo
                {
                    Key = "RoomNumberToElements",
                    DisplayName = "Room Number To Elements",
                    Category = IssueCategories.Plugin,
                    FullClassName = "BA.Commands.RoomNumberToElementsCommand",
                    SmallIconResourceName = "Plugin_RoomNumberToElements_16.png",
                    LargeIconResourceName = "Plugin_RoomNumberToElements_32.png"
                },

                new BACommandInfo
                {
                    Key = "ParameterManager",
                    DisplayName = "Parameter Manager",
                    Category = IssueCategories.Plugin,
                    FullClassName = "BA.Commands.ParameterManagerCommand",
                    SmallIconResourceName = "Plugin_ParameterManager_16.png",
                    LargeIconResourceName = "Plugin_ParameterManager_32.png"
                },

                new BACommandInfo
                {
                    Key = "CopyColumn",
                    DisplayName = "Copy Column",
                    Category = IssueCategories.Plugin,
                    FullClassName = "BA.Commands.CopyColumnCommand",
                    SmallIconResourceName = "Plugin_CopyColumn_16.png",
                    LargeIconResourceName = "Plugin_CopyColumn_32.png"
                }
            };
    }
}
// File: BA.Core/ViewFilters/FilterGroupModels.cs
using System.Collections.Generic;

namespace BA.Core.ViewFilters
{
    // A named, reusable set of filter names, native or BA managed, assembled by
    // checking rows in either pane of the View Template tab. Stored by NAME, not
    // ElementId, same reasoning as SchemeDto: a group needs to resolve against
    // whatever document/template it's applied to later, and ElementIds are not
    // portable across that. "Turn it on/off on different views" means toggling
    // each member filter's visibility on whichever template is currently selected,
    // it does not mean the group itself is tied to one template. // <- NEW
    public sealed class FilterGroupDto
    {
        public string GroupName { get; set; } = string.Empty;
        public List<string> FilterNames { get; set; } = new();
    }

    public sealed record FilterGroupSummary(string GroupName, int FilterCount, string FileName);
}
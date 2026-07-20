// File: BA.Core/ViewFilters/ViewFilterColorManagerModels.cs
using Autodesk.Revit.DB;

namespace BA.Core.ViewFilters
{
    public sealed record ViewTemplateInfo(ElementId Id, string Name, string ViewType);

    public sealed record FilterInfo(
        ElementId FilterId,
        string Name,
        string CategorySummary,
        bool IsVisible,
        byte? CutR, byte? CutG, byte? CutB,
        byte? ProjR, byte? ProjG, byte? ProjB
    );

    // FilterColorAssignment removed from this file. It now lives at the
    // bottom of ViewFilterColorManagerService.cs, alongside the
    // ResolvePatternId logic it's tightly coupled to, since it gained the
    // optional PatternId field. Keeping one definition, not two. // <- CHANGED
}
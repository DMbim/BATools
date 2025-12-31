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

    public sealed record FilterColorAssignment(
        ElementId FilterId,
        byte? CutR, byte? CutG, byte? CutB,
        byte? ProjR, byte? ProjG, byte? ProjB
    );
}

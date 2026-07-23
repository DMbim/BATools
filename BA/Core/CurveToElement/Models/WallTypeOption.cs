// File: BA/Core/CurveToElement/Models/WallTypeOption.cs
// Action: CREATE NEW

using Autodesk.Revit.DB;

namespace BA.Core.CurveToElement.Models
{
    public class WallTypeOption
    {
        public ElementId Id { get; }
        public string Name { get; }
        public WallKind Kind { get; }

        public WallTypeOption(ElementId id, string name, WallKind kind)
        {
            Id = id;
            Name = name;
            Kind = kind;
        }
    }
}
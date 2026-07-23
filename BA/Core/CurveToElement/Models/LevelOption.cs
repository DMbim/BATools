// File: BA/Core/CurveToElement/Models/LevelOption.cs
// Action: CREATE NEW

using Autodesk.Revit.DB;

namespace BA.Core.CurveToElement.Models
{
    public class LevelOption
    {
        public ElementId Id { get; }
        public string Name { get; }
        public double Elevation { get; }

        public LevelOption(ElementId id, string name, double elevation)
        {
            Id = id;
            Name = name;
            Elevation = elevation;
        }
    }
}
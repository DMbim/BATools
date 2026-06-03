// FILE: BA_Tools/Core/Graphics/BaLinePatternDefinition.cs
// No changes needed, included for completeness.
using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace BA.Core.Graphics
{
    public class BaLinePatternDefinition
    {
        public string Name { get; }
        public IList<LinePatternSegment> Segments { get; }

        public BaLinePatternDefinition(string name, IList<LinePatternSegment> segments)
        {
            Name = name;
            Segments = segments;
        }
    }
}
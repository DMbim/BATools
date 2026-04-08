using Autodesk.Revit.DB;

namespace BA.Core.Graphics
{
    public class BaLineStyleDefinition
    {
        public string Name { get; }
        public string PatternName { get; }
        public int LineWeight { get; }
        public Color Color { get; }

        public BaLineStyleDefinition(string name, string patternName, int lineWeight, Color color)
        {
            Name = name;
            PatternName = patternName;
            LineWeight = lineWeight;
            Color = color;
        }
    }
}

using Autodesk.Revit.DB;

namespace BA.Core.Views.ScopeBoxes
{
    public sealed class ScopeBoxInfo
    {
        public ElementId Id { get; }
        public string Name { get; }

        public ScopeBoxInfo(ElementId id, string name)
        {
            Id = id;
            Name = name ?? string.Empty;
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace BA.Core.ViewTemplates
{
    public sealed class ViewTemplateItem
    {
        public ElementId Id { get; }
        public string Name { get; }
        public ViewType ViewType { get; }

        public ViewTemplateItem(ElementId id, string name, ViewType viewType)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? string.Empty;
            ViewType = viewType;
        }

        public override string ToString()
        {
            return $"{Name} [{ViewType}]";
        }
    }

    public sealed class TemplatePropertyItem
    {
        public ElementId ParameterId { get; }
        public string Name { get; }
        public bool IsSelected { get; set; }

        public TemplatePropertyItem(ElementId parameterId, string name, bool isSelected = false)
        {
            ParameterId = parameterId ?? throw new ArgumentNullException(nameof(parameterId));
            Name = name ?? string.Empty;
            IsSelected = isSelected;
        }

        public override string ToString()
        {
            return Name;
        }
    }

    public sealed class ApplyTemplatePropertiesResult
    {
        public string SourceTemplateName { get; set; } = string.Empty;
        public int RequestedTargets { get; set; }
        public int UpdatedTargets { get; set; }
        public int SkippedTargets { get; set; }
        public List<string> Messages { get; } = new List<string>();
    }
}
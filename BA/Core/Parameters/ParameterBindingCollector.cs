using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.Parameters
{
    public static class ParameterBindingCollector
    {
        public static List<ParameterRow> Collect(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var rows = new List<ParameterRow>();
            var map = doc.ParameterBindings;
            if (map == null) return rows;

            var it = map.ForwardIterator();
            it.Reset();

            while (it.MoveNext())
            {
                var def = it.Key as Definition;
                var binding = it.Current as ElementBinding;
                if (def == null || binding == null) continue;

                bool isInstance = binding is InstanceBinding;
                var cats = binding.Categories?.Cast<Category>().Where(c => c != null).ToList() ?? new List<Category>();

                var specId = def.GetDataType();
                var groupId = def.GetGroupTypeId();

                var guid = (def as ExternalDefinition)?.GUID ?? Guid.Empty;

                rows.Add(new ParameterRow
                {
                    Name = def.Name,
                    IsShared = guid != Guid.Empty,
                    Guid = guid == Guid.Empty ? "" : guid.ToString(),
                    InstanceOrType = isInstance ? "Instance" : "Type",
                    SpecLabel = RevitApiCompat.SafeSpecLabel(specId),
                    GroupLabel = RevitApiCompat.SafeGroupLabel(groupId),
                    CategoriesCsv = string.Join(", ", cats.Select(c => c.Name).OrderBy(n => n)),
                    Definition = def,
                    GroupId = groupId,
                    CategoryIds = cats.Select(c => c.Id).ToList()
                });
            }

            return rows;
        }
    }
}

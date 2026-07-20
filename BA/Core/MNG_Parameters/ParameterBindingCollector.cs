using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.Parameters
{
    public static class ParameterBindingCollector
    {
        public static IList<ParameterRow> Collect(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var list = new List<ParameterRow>();
            var map = doc.ParameterBindings;
            var it = map.ForwardIterator();
            it.Reset();

            while (it.MoveNext())
            {
                var def = it.Key;
                if (def == null) continue;
                if (it.Current is not ElementBinding binding) continue;

                var catNames = new List<string>();
                var catIds = new List<long>();
                foreach (Category c in binding.Categories)
                {
                    if (c == null) continue;
                    catNames.Add(c.Name);
                    catIds.Add(c.Id.Value); // Revit 2026
                }
                catNames.Sort(StringComparer.OrdinalIgnoreCase);
                catIds.Sort();

                bool isShared = false;
                string guid = "";
                if (def is InternalDefinition internalDef)
                {
                    ElementId sharedParamElemId = internalDef.Id;
                    isShared = sharedParamElemId != ElementId.InvalidElementId;
                    if (isShared)
                    {
                        var spElem = doc.GetElement(sharedParamElemId) as SharedParameterElement;
                        guid = spElem != null ? spElem.GuidValue.ToString() : "";
                    }
                }

                var instanceOrType = binding is InstanceBinding ? "Instance" : "Type";

                string specLabel = "";
                try { specLabel = LabelUtils.GetLabelForSpec(def.GetDataType()); } catch { }

                ForgeTypeId groupId = GroupCatalog.DefaultGroupId;
                try { groupId = def.GetGroupTypeId(); } catch { }

                string groupLabel = "";
                try { groupLabel = LabelUtils.GetLabelForGroup(groupId); } catch { }

                list.Add(new ParameterRow
                {
                    Name = def.Name,
                    IsShared = isShared,
                    Guid = guid,
                    InstanceOrType = instanceOrType,
                    SpecLabel = specLabel,
                    GroupId = groupId,
                    GroupLabel = groupLabel,
                    CategoryIdValues = catIds,
                    CategoriesCsv = string.Join(", ", catNames)
                });
            }

            return list;
        }
    }
}
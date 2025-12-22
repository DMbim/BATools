using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace BA.Core.Rooms
{
    public static class LinkResolver
    {
        public static RevitLinkInstance? FindLinkInstance(Document doc, string? uniqueId, string? nameFallback)
        {
            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            if (!string.IsNullOrWhiteSpace(uniqueId))
            {
                var byUid = links.FirstOrDefault(x => x.UniqueId.Equals(uniqueId, StringComparison.OrdinalIgnoreCase));
                if (byUid != null) return byUid;
            }

            if (!string.IsNullOrWhiteSpace(nameFallback))
            {
                var byName = links.FirstOrDefault(x => x.Name.Equals(nameFallback, StringComparison.OrdinalIgnoreCase));
                if (byName != null) return byName;

                // last resort: contains
                return links.FirstOrDefault(x => x.Name.IndexOf(nameFallback, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return null;
        }
    }
}

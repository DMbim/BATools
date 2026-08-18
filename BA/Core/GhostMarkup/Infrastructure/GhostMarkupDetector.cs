// BA/Core/GhostMarkup/GhostMarkupDetector.cs
using System;
using Autodesk.Revit.DB;

namespace BA.Core.GhostMarkup
{
    /// <summary>
    /// Classifies a single element as Ghost Markup or not, based on the
    /// BA_NPLT Type Name prefix. Covers Text Notes, Detail Curves (lines,
    /// arcs, ellipses, splines drawn with the BA_NPLT line style), and
    /// Detail Item family instances.
    /// </summary>
    public static class GhostMarkupDetector
    {
        public static bool IsGhostMarkup(Element element, Document doc)
        {
            if (element == null || doc == null)
            {
                return false;
            }

            switch (element)
            {
                case TextNote textNote:
                    return IsGhostTextNote(textNote, doc);

                case DetailCurve detailCurve:
                    return IsGhostDetailCurve(detailCurve);

                case FamilyInstance familyInstance:
                    return IsGhostDetailItem(familyInstance);

                default:
                    return false;
            }
        }

        private static bool IsGhostTextNote(TextNote textNote, Document doc)
        {
            var typeId = textNote.GetTypeId();
            if (typeId == ElementId.InvalidElementId)
            {
                return false;
            }

            var noteType = doc.GetElement(typeId) as TextNoteType;
            return StartsWithGhostPrefix(noteType?.Name);
        }

        private static bool IsGhostDetailCurve(DetailCurve detailCurve)
        {
            var style = detailCurve.LineStyle as GraphicsStyle;
            return StartsWithGhostPrefix(style?.Name);
        }

        private static bool IsGhostDetailItem(FamilyInstance familyInstance)
        {
            var category = familyInstance.Category;
            if (category == null || category.Id.Value != (long)BuiltInCategory.OST_DetailComponents)
            {
                return false;
            }

            var symbol = familyInstance.Symbol;
            if (symbol == null)
            {
                return false;
            }

            if (StartsWithGhostPrefix(symbol.Name))
            {
                return true;
            }

            return StartsWithGhostPrefix(symbol.Family?.Name);
        }

        private static bool StartsWithGhostPrefix(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            return name.StartsWith(GhostMarkupConstants.PrefixToken, StringComparison.OrdinalIgnoreCase);
        }
    }
}
using Autodesk.Revit.DB;
using System;
using System.Linq;

namespace BA.Keyplan
{
    public static class KeyplanTitleBlockUtils
    {
        public static FamilyInstance GetFirstTitleBlockOnSheet(Document doc, ViewSheet sheet)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));

            return new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .FirstOrDefault();
        }
    }
}
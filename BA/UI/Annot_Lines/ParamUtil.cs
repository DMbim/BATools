using Autodesk.Revit.DB;
using System;

namespace BA.UI.TextHub
{
    public static class ParamUtil
    {
        // Revit stores TEXT_SIZE in internal length units.
        public static bool TryGetTextSizeMm(Element typeElem, out double mm)
        {
            mm = 0.0;
            if (typeElem == null) return false;

            var p = GetTextSizeParam(typeElem);
            if (p == null) return false;

            try
            {
                if (p.StorageType != StorageType.Double) return false;
                var internalVal = p.AsDouble();
                mm = UnitUtil.InternalToMm(typeElem.Document, internalVal);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetTextFont(Element typeElem, out string font)
        {
            font = "";
            if (typeElem == null) return false;

            var p = GetTextFontParam(typeElem);
            if (p == null) return false;

            try
            {
                if (p.StorageType != StorageType.String) return false;
                font = p.AsString() ?? "";
                return !string.IsNullOrWhiteSpace(font);
            }
            catch
            {
                return false;
            }
        }

        public static bool HasWritableTextSize(Element typeElem)
        {
            var p = GetTextSizeParam(typeElem);
            return p != null && !p.IsReadOnly && p.StorageType == StorageType.Double;
        }

        public static bool HasWritableTextFont(Element typeElem)
        {
            var p = GetTextFontParam(typeElem);
            return p != null && !p.IsReadOnly && p.StorageType == StorageType.String;
        }

        public static bool TrySetTextSizeMm(Element typeElem, double mm)
        {
            if (typeElem == null) return false;
            var p = GetTextSizeParam(typeElem);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.Double) return false;

            var internalVal = UnitUtil.MmToInternal(typeElem.Document, mm);
            return p.Set(internalVal);
        }

        public static bool TrySetTextFont(Element typeElem, string font)
        {
            if (typeElem == null) return false;
            var p = GetTextFontParam(typeElem);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) return false;

            return p.Set(font ?? "");
        }

        private static Parameter? GetTextSizeParam(Element e)
        {
            // Most relevant built-in params for types that control text.
            // TEXT_SIZE is the main one for TextNoteType, many DimensionType, SpotDimensionType.
            var p = e.get_Parameter(BuiltInParameter.TEXT_SIZE);
            if (p != null) return p;

            // Some types expose different built-ins depending on context; keep a robust fallback:
            // Try lookup by name (localized risk), but better than nothing.
            // (Users can still see N/A if not found.)
            p = e.LookupParameter("Text Size");
            if (p != null) return p;

            p = e.LookupParameter("Textsize");
            if (p != null) return p;

            return null;
        }

        private static Parameter? GetTextFontParam(Element e)
        {
            var p = e.get_Parameter(BuiltInParameter.TEXT_FONT);
            if (p != null) return p;

            p = e.LookupParameter("Text Font");
            if (p != null) return p;

            p = e.LookupParameter("Font");
            if (p != null) return p;

            return null;
        }
    }
}
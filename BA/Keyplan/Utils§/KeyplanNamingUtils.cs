using System;
using System.Text;

namespace BA.Keyplan
{
    public static class KeyplanNamingUtils
    {
        public static string BuildSheetSpecificViewName(string prefix, string sheetNumber, string zoneCode)
        {
            string cleanSheet = Sanitize(sheetNumber);
            string cleanZone = Sanitize(zoneCode);

            return $"{prefix}{cleanSheet}_{cleanZone}";
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "UNDEFINED";

            StringBuilder sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')
                    sb.Append(c);
                else
                    sb.Append('_');
            }

            return sb.ToString();
        }
    }
}
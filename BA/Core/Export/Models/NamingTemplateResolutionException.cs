using System;

namespace BA.Core.Export.Models
{
    /// <summary>
    /// Thrown when a naming or output folder template references a token that
    /// cannot be resolved for a specific sheet, either an unknown built-in
    /// token name or no parameter with that name on the sheet or Project Information.
    /// </summary>
    public class NamingTemplateResolutionException : Exception
    {
        public string TokenName { get; }
        public string SheetNumber { get; }

        public NamingTemplateResolutionException(string tokenName, string sheetNumber, string message)
            : base(message)
        {
            TokenName = tokenName;
            SheetNumber = sheetNumber;
        }
    }
}

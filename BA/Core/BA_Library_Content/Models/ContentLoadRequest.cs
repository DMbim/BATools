using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BA.Core.Content.Models
{
    public sealed class ContentLoadRequest
    {
        public string FamilyPath { get; set; } = string.Empty;
        public bool PlaceAfterLoad { get; set; }
        public bool ActivateFirstSymbol { get; set; }
    }
}

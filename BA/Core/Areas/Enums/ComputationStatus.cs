using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BA.Core.Enums
{
    public enum ComputationStatus
    {
        Success,
        SkippedInsufficientGeometry,    // otevřená hranice, nulový objem
        SkippedNotPlaced,               // Room.Area == 0
        SkippedExcludedByISOCategory,   // A2/A4 vyloučeny z užitné plochy
        Failed                          // neočekávaná výjimka — detail v ErrorMessage
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BA.Core.Enums
{
    /// <summary>
    /// Typ prostoru pro účely NV 366/2013 §5 (koeficienty plochy).
    /// </summary>
    public enum SpaceTypeCzech
    {
        Standard,   // koef. 1.0
        Lodzie,     // koef. 1.0
        Balkon,     // koef. 0.5
        Terasa      // koef. 0.25
    }
}
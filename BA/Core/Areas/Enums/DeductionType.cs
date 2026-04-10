using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BA.Core.Enums
{
    public enum DeductionType
    {
        StructuralColumn,
        StructuralWallInternal,
        HeightReductionHalf,    // výška 1.2–2.0 m → koef. 0.5 (NV 366/2013 §4 odst. 2)
        HeightZeroExclusion,    // výška < 1.2 m → koef. 0.0 (NV 366/2013 §4 odst. 2)
        SpaceTypeMultiplier     // lodžie/balkon/terasa (NV 366/2013 §5)
    }
}

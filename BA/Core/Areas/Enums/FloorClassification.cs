using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BA.Core.Enums
{
    /// <summary>
    /// Klasifikace podlaží dle PSP §2 písm. g).
    /// Podzemní podlaží = podlaha níže než 800 mm pod průměrným upraveným terénem.
    /// </summary>
    public enum FloorClassification
    {
        Nadzemni,
        Podzemni
    }
}


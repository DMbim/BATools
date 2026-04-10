using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BA.Core.Enums
{
    public enum AreaType
    {
        PodlahovaPlochaNV366,   // NV č. 366/2013 Sb.
        HPPNadzemni,            // PSP §2 písm. c) + §2 písm. g) — nadzemní
        HPPPodzemni,            // PSP §2 písm. c) + §2 písm. g) — podzemní
        PodlahovaPlochaSZ,      // SZ č. 283/2021 §13 písm. n)
        ZastavenaPlochaSZ       // SZ č. 283/2021 §13 písm. o)
    }
}

using BA.Core.Enums;
using BA.Core.Models;

namespace BA.Core.Interfaces
{
    public interface INormInfoProvider
    {
        NormInfo GetNormInfo(AreaType areaType);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace BA.UI.KeyplanGrid
{
    public sealed class AxisPreviewInfo
    {
        public AxisOrientation Orientation { get; set; }
        public int InteriorIndex { get; set; }
        public double Normalized { get; set; }
        public double CanvasPosition { get; set; }
    }
}

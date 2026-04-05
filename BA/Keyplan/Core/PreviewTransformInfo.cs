using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BA.UI.KeyplanGrid
{
    public sealed class PreviewTransformInfo
    {
        public double ModelMinX { get; set; }
        public double ModelMinY { get; set; }
        public double ModelMaxX { get; set; }
        public double ModelMaxY { get; set; }

        public double CanvasWidth { get; set; }
        public double CanvasHeight { get; set; }
        public double Padding { get; set; }
        public double Scale { get; set; }
    }
}

namespace BA.UI.KeyplanGrid
{
    public static class KeyplanGeometryTolerance
    {
        public const double Epsilon = 1e-9;

        public const double Point = 1e-5;
        public const double Edge = 1e-5;
        public const double TinyEdgeStrict = 1e-4;

        public const double CollinearArea2 = 1e-8;
        public const double SegmentIntersection = 1e-9;
        public const double Parameter = 1e-9;

        public const double PolygonArea = 1e-6;
        public const double FilledRegionArea = 1e-4;

        public const double CurveConnection = 1e-4;
        public const double PointOnSegment = 1e-6;

        public const double KeyRounding = 1e-6;
        public const double FaceSplitPoint = 1e-5;
        public const double FaceSnap = 1e-5;

        public const double MinModelSegment = 1e-6;

                  // THE NEW UNIFIED TOLERANCE: Replaces 1e-6 and 1e-3. 
        // 1e-4 feet is ~0.03 mm, well within Revit's Short Curve Tolerance, 
        // but large enough to heal micro-gaps from boundary extraction.
        public const double VertexMerge = 1e-4;
    }
}
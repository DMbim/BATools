using Autodesk.Revit.DB;

namespace BA.BIM.Core.Annotations
{
    public readonly struct ViewPlane2D
    {
        public XYZ Origin { get; }
        public XYZ Right { get; }
        public XYZ Up { get; }

        private ViewPlane2D(XYZ origin, XYZ right, XYZ up)
        {
            Origin = origin;
            Right = right;
            Up = up;
        }

        public static ViewPlane2D FromView(Autodesk.Revit.DB.View view)
        {
            return new ViewPlane2D(
                view.Origin,
                view.RightDirection.Normalize(),
                view.UpDirection.Normalize());
        }

        public UV ToUV(XYZ p)
        {
            XYZ v = p - Origin;
            return new UV(v.DotProduct(Right), v.DotProduct(Up));
        }

        public XYZ DeltaToXYZ(UV delta)
        {
            return (Right * delta.U) + (Up * delta.V);
        }
    }
}
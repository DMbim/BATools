using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.Core.AreaSchemes.Constants;

namespace BA.Core.AreaSchemes.Services
{
    public static class AreaBoundaryService
    {
        /// <summary>
        /// Draws Area boundary lines in the given view from the exterior
        /// finished faces of the provided walls.
        /// Must be called inside an open Transaction.
        /// </summary>
        public static int DrawBoundariesFromWalls(
            Document doc,
            ViewPlan view,
            IReadOnlyList<Wall> walls)
        {
            int drawn = 0;
            var sketchPlane = GetOrCreateSketchPlane(doc, view);

            foreach (var wall in walls)
            {
                var curves = GetFinishedFaceCurves(wall, view);
                foreach (var curve in curves)
                {
                    try
                    {
                        doc.Create.NewAreaBoundaryLine(sketchPlane, curve, view);
                        drawn++;
                    }
                    catch { }
                }
            }

            return drawn;
        }

        /// <summary>
        /// Draws Area boundary lines from column footprints.
        /// Must be called inside an open Transaction.
        /// </summary>
        public static int DrawBoundariesFromColumns(
            Document doc,
            ViewPlan view,
            IReadOnlyList<FamilyInstance> columns)
        {
            int drawn = 0;
            var sketchPlane = GetOrCreateSketchPlane(doc, view);

            foreach (var column in columns)
            {
                var curves = GetColumnFootprintCurves(column, view);
                foreach (var curve in curves)
                {
                    try
                    {
                        doc.Create.NewAreaBoundaryLine(sketchPlane, curve, view);
                        drawn++;
                    }
                    catch { }
                }
            }

            return drawn;
        }

        /// <summary>
        /// Returns the BA_AreaType parameter value for a wall or column.
        /// Returns null if the parameter doesn't exist or is empty.
        /// </summary>
        public static string? GetAreaType(Element element)
        {
            var param = element.LookupParameter(AreaSchemeConstants.ParamAreaType);
            if (param == null || param.StorageType != StorageType.String)
                return null;

            var value = param.AsString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>
        /// Sets BA_AreaType on a wall or column.
        /// Must be called inside an open Transaction.
        /// </summary>
        public static void SetAreaType(Element element, string areaType)
        {
            var param = element.LookupParameter(AreaSchemeConstants.ParamAreaType);
            if (param == null || param.IsReadOnly) return;
            if (param.StorageType != StorageType.String) return;

            param.Set(areaType);
        }

        // --------------------------------------------------------
        // Private helpers
        // --------------------------------------------------------

        private static SketchPlane GetOrCreateSketchPlane(Document doc, ViewPlan view)
        {
            double elevation = view.GenLevel?.Elevation ?? 0.0;
            var plane = Plane.CreateByNormalAndOrigin(
                XYZ.BasisZ,
                new XYZ(0, 0, elevation));

            return SketchPlane.Create(doc, plane);
        }

        private static IReadOnlyList<Curve> GetFinishedFaceCurves(
            Wall wall,
            ViewPlan view)
        {
            var curves = new List<Curve>();

            try
            {
                // Get exterior finished face references
                var exteriorFaceRefs = HostObjectUtils.GetSideFaces(
                    wall, ShellLayerType.Exterior);

                var interiorFaceRefs = HostObjectUtils.GetSideFaces(
                    wall, ShellLayerType.Interior);

                var allRefs = exteriorFaceRefs.Concat(interiorFaceRefs);

                double levelZ = view.GenLevel?.Elevation ?? 0.0;
                double tolFt = 500.0 / 304.8; // 500mm tolerance
                double minLenFt = 1.0 / 304.8;  // 1mm minimum

                foreach (var faceRef in allRefs)
                {
                    var geoObj = wall.GetGeometryObjectFromReference(faceRef);
                    if (geoObj is not Face face) continue;

                    foreach (EdgeArray edgeArray in face.EdgeLoops)
                    {
                        foreach (Edge edge in edgeArray)
                        {
                            var curve = edge.AsCurve();
                            var p0 = curve.GetEndPoint(0);
                            var p1 = curve.GetEndPoint(1);

                            // Only horizontal edges near the level elevation
                            if (Math.Abs(p0.Z - p1.Z) > minLenFt) continue;
                            if (Math.Abs(p0.Z - levelZ) > tolFt) continue;
                            if (curve.Length < minLenFt) continue;

                            // Project to level elevation
                            var flat0 = new XYZ(p0.X, p0.Y, levelZ);
                            var flat1 = new XYZ(p1.X, p1.Y, levelZ);

                            if (flat0.DistanceTo(flat1) < minLenFt) continue;

                            curves.Add(Line.CreateBound(flat0, flat1));
                        }
                    }
                }
            }
            catch { }

            return curves;
        }

        private static IReadOnlyList<Curve> GetColumnFootprintCurves(
            FamilyInstance column,
            ViewPlan view)
        {
            var curves = new List<Curve>();
            double levelZ = view.GenLevel?.Elevation ?? 0.0;
            double minLenFt = 1.0 / 304.8;

            try
            {
                var options = new Options
                {
                    ComputeReferences = false,
                    DetailLevel = ViewDetailLevel.Fine,
                    IncludeNonVisibleObjects = false
                };

                var geometry = column.get_Geometry(options);
                if (geometry == null) return curves;

                foreach (GeometryObject obj in geometry)
                {
                    Solid? solid = obj as Solid;

                    if (solid == null && obj is GeometryInstance gi)
                        solid = gi.GetInstanceGeometry()
                            .OfType<Solid>()
                            .OrderByDescending(s => s.Volume)
                            .FirstOrDefault();

                    if (solid == null || solid.Volume <= 0) continue;

                    // Find the bottom face and extract its edges
                    foreach (Face face in solid.Faces)
                    {
                        if (face is not PlanarFace pf) continue;
                        if (pf.FaceNormal.Z > -0.99) continue;

                        foreach (EdgeArray edgeArray in pf.EdgeLoops)
                        {
                            foreach (Edge edge in edgeArray)
                            {
                                var curve = edge.AsCurve();
                                if (curve.Length < minLenFt) continue;

                                var p0 = curve.GetEndPoint(0);
                                var p1 = curve.GetEndPoint(1);
                                var flat0 = new XYZ(p0.X, p0.Y, levelZ);
                                var flat1 = new XYZ(p1.X, p1.Y, levelZ);

                                if (flat0.DistanceTo(flat1) < minLenFt) continue;
                                curves.Add(Line.CreateBound(flat0, flat1));
                            }
                        }
                    }
                }
            }
            catch { }

            return curves;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using BA.Core.Interfaces;
using Clipper2Lib;
using RevitLevel = Autodesk.Revit.DB.Level;
using RevitColor = Autodesk.Revit.DB.Color;
using ClipperFillRule = Clipper2Lib.FillRule;
using ClipperClipType = Clipper2Lib.ClipType;

// Explicitní aliasy — eliminují veškeré ambiguity



namespace BA.Services.Geometry
{
    public sealed class GeometryEngine : IGeometryEngine
    {
        private const double OneMmInFeet = 1.0 / 304.8;
        private const double JoinToleranceFt = 5.0 / 304.8;
        private const double MinSegmentLengthFt = 1.0 / 304.8;

        // --------------------------------------------------------
        // ExtractFloorProjectionLoops
        // --------------------------------------------------------

        public IReadOnlyList<CurveLoop> ExtractFloorProjectionLoops(Solid roomSolid)
        {
            if (roomSolid is null)
                throw new ArgumentNullException(nameof(roomSolid));

            var loops = new List<(double z, CurveLoop loop)>();

            foreach (Face face in roomSolid.Faces)
            {
                if (face is not PlanarFace planarFace)
                    continue;

                if (planarFace.FaceNormal.Z > -0.99)
                    continue;

                double faceZ = planarFace.Origin.Z;

                foreach (EdgeArray edgeArray in planarFace.EdgeLoops)
                {
                    var curves = new List<Curve>();
                    foreach (Edge edge in edgeArray)
                    {
                        var curve = edge.AsCurve();
                        if (curve.Length > MinSegmentLengthFt)
                            curves.Add(curve);
                    }

                    if (curves.Count < 3)
                        continue;

                    try
                    {
                        var loop = CurveLoop.Create(curves);

                        if (!loop.IsCounterclockwise(XYZ.BasisZ))
                            loop.Flip();

                        loops.Add((faceZ, loop));
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException)
                    {
                        // Nevalidní loop — přeskočíme
                    }
                }
            }

            if (!loops.Any())
                return Array.Empty<CurveLoop>();

            double minZ = loops.Min(l => l.z);
            return loops
                .Where(l => Math.Abs(l.z - minZ) < JoinToleranceFt)
                .Select(l => l.loop)
                .ToList();
        }

        // --------------------------------------------------------
        // ComputeLoopAreaM2
        // --------------------------------------------------------

        public double ComputeLoopAreaM2(CurveLoop loop)
        {
            if (loop is null)
                throw new ArgumentNullException(nameof(loop));

            try
            {
                double areaFt2 = ExporterIFCUtils.ComputeAreaOfCurveLoops(
                    new List<CurveLoop> { loop });

                return UnitUtils.ConvertFromInternalUnits(areaFt2, UnitTypeId.SquareMeters);
            }
            catch
            {
                return ComputeLoopAreaShoelaceM2(loop);
            }
        }

        private static double ComputeLoopAreaShoelaceM2(CurveLoop loop)
        {
            var points = new List<XYZ>();

            foreach (Curve curve in loop)
            {
                var tessellated = curve.Tessellate();
                for (int i = 0; i < tessellated.Count - 1; i++)
                    points.Add(tessellated[i]);
            }

            if (points.Count < 3)
                return 0.0;

            double area = 0.0;
            int n = points.Count;

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += points[i].X * points[j].Y;
                area -= points[j].X * points[i].Y;
            }

            double areaFt2 = Math.Abs(area) / 2.0;
            return UnitUtils.ConvertFromInternalUnits(areaFt2, UnitTypeId.SquareMeters);
        }

        // --------------------------------------------------------
        // BuildOuterShellFootprintAsync
        // --------------------------------------------------------

        public async Task<IReadOnlyList<CurveLoop>> BuildOuterShellFootprintAsync(
            IReadOnlyList<Wall> walls,
            RevitLevel level,           // ← alias
            Document document)
        {
            return await Task.Run(() =>
            {
                var exteriorCurves = new List<Curve>();

                foreach (var wall in walls)
                {
                    var faceCurves = GetWallExteriorFaceCurves(wall, level, document);
                    exteriorCurves.AddRange(faceCurves);
                }

                if (!exteriorCurves.Any())
                    return Array.Empty<CurveLoop>();

                return BuildClosedLoopsFromCurves(exteriorCurves);
            });
        }

        private static IReadOnlyList<Curve> GetWallExteriorFaceCurves(
            Wall wall,
            RevitLevel level,           // ← alias
            Document document)
        {
            var curves = new List<Curve>();

            try
            {
                var exteriorFaceRefs = HostObjectUtils.GetSideFaces(
                    wall, ShellLayerType.Exterior);

                if (!exteriorFaceRefs.Any())
                    return curves;

                var geoObj = wall.GetGeometryObjectFromReference(
                    exteriorFaceRefs.First());

                if (geoObj is not Face exteriorFace)
                    return curves;

                double levelZ = level.Elevation;
                double levelTolFt = 200.0 / 304.8;

                foreach (EdgeArray edgeArray in exteriorFace.EdgeLoops)
                {
                    foreach (Edge edge in edgeArray)
                    {
                        var curve = edge.AsCurve();
                        double z0 = curve.GetEndPoint(0).Z;
                        double z1 = curve.GetEndPoint(1).Z;

                        if (Math.Abs(z0 - levelZ) < levelTolFt &&
                            Math.Abs(z1 - levelZ) < levelTolFt &&
                            curve.Length > MinSegmentLengthFt)
                        {
                            var p0 = new XYZ(
                                curve.GetEndPoint(0).X,
                                curve.GetEndPoint(0).Y,
                                levelZ);
                            var p1 = new XYZ(
                                curve.GetEndPoint(1).X,
                                curve.GetEndPoint(1).Y,
                                levelZ);

                            if (p0.DistanceTo(p1) > MinSegmentLengthFt)
                                curves.Add(Line.CreateBound(p0, p1));
                        }
                    }
                }
            }
            catch
            {
                // Stěna bez platné geometrie — přeskočíme
            }

            return curves;
        }

        // --------------------------------------------------------
        // BuildBuildingFootprintAsync
        // --------------------------------------------------------

        public async Task<IReadOnlyList<CurveLoop>> BuildBuildingFootprintAsync(
            IReadOnlyList<Element> groundFloorElements,
            double overhangThresholdM,
            Document document)
        {
            return await Task.Run(() =>
            {
                var projectedCurves = new List<Curve>();
                double overhangThresholdFt = overhangThresholdM / 0.3048;

                foreach (var element in groundFloorElements)
                {
                    var solid = GetLargestSolid(element);
                    if (solid is null)
                        continue;

                    foreach (Edge edge in solid.Edges)
                    {
                        var curve = edge.AsCurve();
                        var p0 = curve.GetEndPoint(0);
                        var p1 = curve.GetEndPoint(1);

                        if (Math.Abs(p0.Z - p1.Z) > OneMmInFeet)
                            continue;

                        if (p0.Z > overhangThresholdFt)
                            continue;

                        var flat0 = new XYZ(p0.X, p0.Y, 0);
                        var flat1 = new XYZ(p1.X, p1.Y, 0);

                        if (flat0.DistanceTo(flat1) > MinSegmentLengthFt)
                            projectedCurves.Add(Line.CreateBound(flat0, flat1));
                    }
                }

                return BuildClosedLoopsFromCurves(projectedCurves);
            });
        }

        // --------------------------------------------------------
        // SubtractHolesFromLoop — Clipper2
        // --------------------------------------------------------

        public CurveLoop? SubtractHolesFromLoop(
            CurveLoop outerLoop,
            IReadOnlyList<CurveLoop> holes,
            double elevationFt)
        {
            if (!holes.Any())
                return outerLoop;

            var subjectPaths = new PathsD { CurveLoopToClipperPath(outerLoop) };

            var clipPaths = new PathsD();
            foreach (var hole in holes)
                clipPaths.Add(CurveLoopToClipperPath(hole));

            var clipper = new ClipperD(8);
            clipper.AddSubject(subjectPaths);
            clipper.AddClip(clipPaths);

            var solution = new PathsD();

            // Explicitní aliasy — žádná ambiguita
            clipper.Execute(ClipperClipType.Difference, ClipperFillRule.NonZero, solution);

            if (!solution.Any())
                return null;

            return ClipperPathToCurveLoop(solution.First(), elevationFt);
        }

        // --------------------------------------------------------
        // BuildClosedLoopsFromCurves
        // --------------------------------------------------------

        private static IReadOnlyList<CurveLoop> BuildClosedLoopsFromCurves(
            List<Curve> curves)
        {
            if (!curves.Any())
                return Array.Empty<CurveLoop>();

            var snapped = curves
                .Select(SnapCurveEndpoints)
                .Where(c => c is not null && c.Length > MinSegmentLengthFt)
                .ToList();

            var remaining = new List<Curve>(snapped!);
            var result = new List<CurveLoop>();

            while (remaining.Count > 0)
            {
                var chain = new List<Curve> { remaining[0] };
                remaining.RemoveAt(0);

                bool extended = true;
                while (extended)
                {
                    extended = false;
                    XYZ chainEnd = chain.Last().GetEndPoint(1);

                    for (int i = 0; i < remaining.Count; i++)
                    {
                        var candidate = remaining[i];
                        double d0 = chainEnd.DistanceTo(candidate.GetEndPoint(0));
                        double d1 = chainEnd.DistanceTo(candidate.GetEndPoint(1));

                        if (d0 < JoinToleranceFt)
                        {
                            chain.Add(candidate);
                            remaining.RemoveAt(i);
                            extended = true;
                            break;
                        }
                        else if (d1 < JoinToleranceFt)
                        {
                            chain.Add(candidate.CreateReversed());
                            remaining.RemoveAt(i);
                            extended = true;
                            break;
                        }
                    }
                }

                if (chain.Count < 3)
                    continue;

                XYZ first = chain.First().GetEndPoint(0);
                XYZ last = chain.Last().GetEndPoint(1);

                if (first.DistanceTo(last) > JoinToleranceFt)
                    continue;

                try
                {
                    var loop = CurveLoop.Create(chain);
                    if (!loop.IsCounterclockwise(XYZ.BasisZ))
                        loop.Flip();

                    result.Add(loop);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // Nevalidní loop — přeskočíme
                }
            }

            return result;
        }

        private static Curve? SnapCurveEndpoints(Curve curve)
        {
            try
            {
                const double snapGrid = 0.1 / 304.8;

                var p0 = SnapPoint(curve.GetEndPoint(0), snapGrid);
                var p1 = SnapPoint(curve.GetEndPoint(1), snapGrid);

                if (p0.DistanceTo(p1) < MinSegmentLengthFt)
                    return null;

                return Line.CreateBound(p0, p1);
            }
            catch
            {
                return null;
            }
        }

        private static XYZ SnapPoint(XYZ point, double grid) =>
            new XYZ(
                Math.Round(point.X / grid) * grid,
                Math.Round(point.Y / grid) * grid,
                Math.Round(point.Z / grid) * grid);

        // --------------------------------------------------------
        // Clipper2 helpers
        // --------------------------------------------------------

        private static PathD CurveLoopToClipperPath(CurveLoop loop)
        {
            var path = new PathD();

            foreach (Curve curve in loop)
            {
                var tessellated = curve.Tessellate();
                for (int i = 0; i < tessellated.Count - 1; i++)
                {
                    path.Add(new PointD(
                        tessellated[i].X * 10000.0,
                        tessellated[i].Y * 10000.0));
                }
            }

            return path;
        }

        private static CurveLoop ClipperPathToCurveLoop(PathD path, double elevationFt)
        {
            var curves = new List<Curve>();

            for (int i = 0; i < path.Count; i++)
            {
                int j = (i + 1) % path.Count;

                var p0 = new XYZ(path[i].x / 10000.0, path[i].y / 10000.0, elevationFt);
                var p1 = new XYZ(path[j].x / 10000.0, path[j].y / 10000.0, elevationFt);

                if (p0.DistanceTo(p1) > MinSegmentLengthFt)
                    curves.Add(Line.CreateBound(p0, p1));
            }

            return CurveLoop.Create(curves);
        }

        // --------------------------------------------------------
        // Utility
        // --------------------------------------------------------

        internal static Solid? GetLargestSolid(Element element)
        {
            var options = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = false
            };

            Solid? largest = null;
            double maxVolume = 0;

            var geometry = element.get_Geometry(options);
            if (geometry is null)
                return null;

            foreach (GeometryObject obj in geometry)
            {
                switch (obj)
                {
                    case Solid solid when solid.Volume > maxVolume:
                        maxVolume = solid.Volume;
                        largest = solid;
                        break;

                    case GeometryInstance instance:
                        foreach (GeometryObject instObj in instance.GetInstanceGeometry())
                        {
                            if (instObj is Solid instSolid && instSolid.Volume > maxVolume)
                            {
                                maxVolume = instSolid.Volume;
                                largest = instSolid;
                            }
                        }
                        break;
                }
            }

            return largest;
        }
    }
}
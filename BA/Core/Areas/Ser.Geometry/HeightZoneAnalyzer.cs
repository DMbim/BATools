using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using BA.Core.Interfaces;
using BA.Core.Models;

namespace BA.Services.Geometry
{
    /// <summary>
    /// Analyzuje výškové zóny místnosti grid-based raycastingem.
    ///
    /// NV 366/2013 §4 odst. 2:
    ///   >= 2000 mm → plná plocha (koef. 1.0)
    ///   1200–1999 mm → poloviční plocha (koef. 0.5)
    ///   < 1200 mm  → nezapočítává se (koef. 0.0)
    ///
    /// Přístup: Grid 100mm × 100mm přes bounding box místnosti.
    /// Pro každý bod: svislý paprsek od podlahy → první strop/šikmina.
    /// Buňky seskupíme do zón a konvertujeme na CurveLoop obdélníky.
    /// </summary>
    public sealed class HeightZoneAnalyzer : IHeightZoneAnalyzer
    {
        // Rozlišení gridu v mm
        private const double GridResolutionMm = 100.0;

        // Prahové výšky dle NV 366/2013 §4 odst. 2 (v mm)
        private const double FullHeightThresholdMm = 2000.0;
        private const double HalfHeightThresholdMm = 1200.0;

        private readonly IGeometryEngine _geometryEngine;

        public HeightZoneAnalyzer(IGeometryEngine geometryEngine)
        {
            _geometryEngine = geometryEngine
                ?? throw new ArgumentNullException(nameof(geometryEngine));
        }

        public async Task<HeightZoneResult> AnalyzeAsync(
            Room room,
            Solid roomSolid,
            IReadOnlyList<CurveLoop> floorLoops,
            Document document)
        {
            return await Task.Run(() => AnalyzeInternal(room, roomSolid, floorLoops));
        }

        private HeightZoneResult AnalyzeInternal(
            Room room,
            Solid roomSolid,
            IReadOnlyList<CurveLoop> floorLoops)
        {
            var bbox = room.get_BoundingBox(null);
            if (bbox is null)
                return EmptyResult();

            double gridFt = GridResolutionMm / 304.8;

            // Najdeme výšku podlahy — nejnižší Z v solidu
            double floorElevationFt = GetFloorElevation(roomSolid);

            // Klasifikace buněk gridu
            var fullCells = new List<GridCell>();
            var halfCells = new List<GridCell>();
            var zeroCells = new List<GridCell>();

            for (double x = bbox.Min.X; x < bbox.Max.X; x += gridFt)
            {
                for (double y = bbox.Min.Y; y < bbox.Max.Y; y += gridFt)
                {
                    // Střed buňky pro point-in-polygon test
                    double cx = x + gridFt / 2.0;
                    double cy = y + gridFt / 2.0;
                    var testPoint = new XYZ(cx, cy, floorElevationFt);

                    if (!IsPointInsideAnyLoop(testPoint, floorLoops))
                        continue;

                    double heightMm = MeasureClearHeightMm(testPoint, roomSolid, floorElevationFt);

                    var cell = new GridCell(x, y, gridFt);

                    if (heightMm >= FullHeightThresholdMm)
                        fullCells.Add(cell);
                    else if (heightMm >= HalfHeightThresholdMm)
                        halfCells.Add(cell);
                    else
                        zeroCells.Add(cell);
                }
            }

            // Konverze buněk na CurveLoop — každá buňka = jeden obdélníkový loop
            // Produkce: union sousedních buněk přes Clipper2 (zde simplified)
            var fullLoops = ConvertCellsToLoops(fullCells, floorElevationFt);
            var halfLoops = ConvertCellsToLoops(halfCells, floorElevationFt);
            var zeroLoops = ConvertCellsToLoops(zeroCells, floorElevationFt);

            double fullAreaM2 = fullLoops.Sum(l => _geometryEngine.ComputeLoopAreaM2(l));
            double halfAreaM2 = halfLoops.Sum(l => _geometryEngine.ComputeLoopAreaM2(l));
            double zeroAreaM2 = zeroLoops.Sum(l => _geometryEngine.ComputeLoopAreaM2(l));

            return new HeightZoneResult
            {
                FullZoneLoops = fullLoops,
                HalfZoneLoops = halfLoops,
                ZeroZoneLoops = zeroLoops,
                FullZoneAreaM2 = fullAreaM2,
                HalfZoneAreaM2 = halfAreaM2,
                ZeroZoneAreaM2 = zeroAreaM2
            };
        }

        private static double GetFloorElevation(Solid roomSolid)
        {
            double minZ = double.MaxValue;

            foreach (Face face in roomSolid.Faces)
            {
                if (face is PlanarFace pf && pf.FaceNormal.Z < -0.99)
                {
                    double z = pf.Origin.Z;
                    if (z < minZ)
                        minZ = z;
                }
            }

            return minZ == double.MaxValue ? 0.0 : minZ;
        }

        private static double MeasureClearHeightMm(
            XYZ floorPoint,
            Solid roomSolid,
            double floorElevationFt)
        {
            // Svislý paprsek 100 ft nahoru
            var ray = Line.CreateBound(
                floorPoint,
                new XYZ(floorPoint.X, floorPoint.Y, floorPoint.Z + 100.0));

            double maxCeilingZ = floorElevationFt;

            foreach (Face face in roomSolid.Faces)
            {
                // Stropní plochy: normála mířící nahoru (Z > 0.1)
                if (face is PlanarFace pf && pf.FaceNormal.Z < 0.1)
                    continue;

                var result = face.Intersect(ray, out var intersections);

                if (result != SetComparisonResult.Overlap || intersections is null)
                    continue;

                foreach (IntersectionResult ir in intersections)
                {
                    if (ir.XYZPoint.Z > maxCeilingZ)
                        maxCeilingZ = ir.XYZPoint.Z;
                }
            }

            double heightFt = maxCeilingZ - floorElevationFt;
            return UnitUtils.ConvertFromInternalUnits(heightFt, UnitTypeId.Millimeters);
        }

        private static bool IsPointInsideAnyLoop(XYZ point, IReadOnlyList<CurveLoop> loops)
        {
            foreach (var loop in loops)
            {
                if (IsPointInsideLoop(point, loop))
                    return true;
            }
            return false;
        }

        private static bool IsPointInsideLoop(XYZ point, CurveLoop loop)
        {
            int crossings = 0;

            foreach (Curve curve in loop)
            {
                var p0 = curve.GetEndPoint(0);
                var p1 = curve.GetEndPoint(1);

                bool straddles = (p0.Y <= point.Y && p1.Y > point.Y) ||
                                 (p1.Y <= point.Y && p0.Y > point.Y);

                if (!straddles)
                    continue;

                double t = (point.Y - p0.Y) / (p1.Y - p0.Y);
                double xIntersect = p0.X + t * (p1.X - p0.X);

                if (point.X < xIntersect)
                    crossings++;
            }

            return crossings % 2 == 1;
        }

        private static IReadOnlyList<CurveLoop> ConvertCellsToLoops(
            List<GridCell> cells,
            double elevationFt)
        {
            if (!cells.Any())
                return Array.Empty<CurveLoop>();

            var loops = new List<CurveLoop>();

            foreach (var cell in cells)
            {
                try
                {
                    var loop = CurveLoop.Create(new List<Curve>
                    {
                        Line.CreateBound(
                            new XYZ(cell.X,              cell.Y,              elevationFt),
                            new XYZ(cell.X + cell.Size,  cell.Y,              elevationFt)),
                        Line.CreateBound(
                            new XYZ(cell.X + cell.Size,  cell.Y,              elevationFt),
                            new XYZ(cell.X + cell.Size,  cell.Y + cell.Size,  elevationFt)),
                        Line.CreateBound(
                            new XYZ(cell.X + cell.Size,  cell.Y + cell.Size,  elevationFt),
                            new XYZ(cell.X,              cell.Y + cell.Size,  elevationFt)),
                        Line.CreateBound(
                            new XYZ(cell.X,              cell.Y + cell.Size,  elevationFt),
                            new XYZ(cell.X,              cell.Y,              elevationFt))
                    });

                    loops.Add(loop);
                }
                catch
                {
                    // Degenerovaná buňka — přeskočíme
                }
            }

            return loops;
        }

        private static HeightZoneResult EmptyResult() => new HeightZoneResult
        {
            FullZoneLoops = Array.Empty<CurveLoop>(),
            HalfZoneLoops = Array.Empty<CurveLoop>(),
            ZeroZoneLoops = Array.Empty<CurveLoop>(),
            FullZoneAreaM2 = 0,
            HalfZoneAreaM2 = 0,
            ZeroZoneAreaM2 = 0
        };

        private readonly record struct GridCell(double X, double Y, double Size);
    }
}
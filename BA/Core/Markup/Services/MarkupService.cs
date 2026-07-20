// BA/Markup/Services/MarkupService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using BA.Core.Parameters;
using BA.Markup.Models;
using BA.Markup.Settings;
using View = Autodesk.Revit.DB.View;

namespace BA.Markup.Services
{
    public sealed class MarkupService
    {
        private readonly Document _doc;
        private readonly UIDocument _uiDoc;
        private readonly MarkupSettings _settings;

        private const string ParamBaType = "BA_Type";
        private const string ParamBaComments = "BA_Comments";
        private const string ParamBaMarkupDate = "BA_Markup_Date";
        private const string ParamBaMarkupAuthor = "BA_Markup_Author";
        private const string ParamX = "x";
        private const string ParamY = "y";

        public MarkupService(UIDocument uiDoc, MarkupSettings settings)
        {
            _uiDoc = uiDoc ?? throw new ArgumentNullException(nameof(uiDoc));
            _doc = uiDoc.Document;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        // ================================================================== //
        //  PUBLIC ENTRY POINTS
        // ================================================================== //

        public FamilyInstance PlaceInternalMarkup(
            MarkupInputModel input,
            BoundingBoxXYZ boundingBox,
            View activeView)
        {
            ValidateViewSupportsDetailItems(activeView);

            var symbol = GetOrLoadDetailItemSymbol();
            EnsureSymbolActive(symbol);

            var centre = GetBoundingBoxCentre(boundingBox);

            var instance = _doc.Create.NewFamilyInstance(
                centre,
                symbol,
                activeView);

            SetLengthParameter(instance, ParamX, boundingBox.Max.X - boundingBox.Min.X);
            SetLengthParameter(instance, ParamY, boundingBox.Max.Y - boundingBox.Min.Y);

            SetStringParameter(instance, ParamBaType, input.BaType);
            SetStringParameter(instance, ParamBaComments, input.BaComments);
            SetStringParameter(instance, ParamBaMarkupDate, input.BaDate);
            SetStringParameter(instance, ParamBaMarkupAuthor, input.BaAuthor);

            PlaceMarkupTag(instance, boundingBox, activeView);

            return instance;
        }

        public RevisionCloud PlaceRevisionCloud(
            MarkupInputModel input,
            BoundingBoxXYZ boundingBox,
            View activeView)
        {
            ValidateViewSupportsRevisionClouds(activeView);

            if (input.RevisionElementId < 0)
                throw new InvalidOperationException(
                    "No revision selected. A valid Revit Revision must be selected for Official Revision mode.");

            var revisionId = new ElementId(input.RevisionElementId);

            if (_doc.GetElement(revisionId) is not Revision)
                throw new InvalidOperationException(
                    $"Revision with ElementId {input.RevisionElementId} no longer exists in the document.");

            // <- CHANGED: RevisionCloud.Create takes IList<Curve>, not IList<CurveLoop>.
            // Use the flat curve list builder, not the CurveLoop builder.
            var curves = BuildCurvesFromBoundingBox(boundingBox);

            var cloud = RevisionCloud.Create(
                _doc,
                activeView,
                revisionId,
                curves);  // <- CHANGED: was "loops" (IList<CurveLoop>), now "curves" (IList<Curve>)

            SetStringParameterIfExists(cloud, ParamBaType, input.BaType);
            SetStringParameterIfExists(cloud, ParamBaComments, input.BaComments);
            SetStringParameterIfExists(cloud, ParamBaMarkupDate, input.BaDate);
            SetStringParameterIfExists(cloud, ParamBaMarkupAuthor, input.BaAuthor);

            return cloud;
        }

        // ================================================================== //
        //  BOUNDING BOX
        // ================================================================== //

        public BoundingBoxXYZ GetBoundingBoxFromSelection(
            IList<ElementId> elementIds,
            View activeView)
        {
            if (elementIds == null || elementIds.Count == 0)
                throw new ArgumentException("No elements provided for bounding box calculation.");

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            double z = 0.0;

            foreach (var id in elementIds)
            {
                var el = _doc.GetElement(id);
                if (el == null) continue;

                var bb = el.get_BoundingBox(activeView);
                if (bb == null) continue;

                if (bb.Min.X < minX) minX = bb.Min.X;
                if (bb.Min.Y < minY) minY = bb.Min.Y;
                if (bb.Max.X > maxX) maxX = bb.Max.X;
                if (bb.Max.Y > maxY) maxY = bb.Max.Y;
                z = bb.Min.Z;
            }

            if (minX == double.MaxValue)
                throw new InvalidOperationException(
                    "Could not compute a bounding box from the selected elements in the active view. " +
                    "Elements may not be visible in this view.");

            return ExpandBoundingBox(minX, minY, maxX, maxY, z);
        }

        public BoundingBoxXYZ GetBoundingBoxFromPoints(XYZ pointA, XYZ pointB)
        {
            double minX = Math.Min(pointA.X, pointB.X);
            double minY = Math.Min(pointA.Y, pointB.Y);
            double maxX = Math.Max(pointA.X, pointB.X);
            double maxY = Math.Max(pointA.Y, pointB.Y);
            double z = Math.Min(pointA.Z, pointB.Z);

            return ExpandBoundingBox(minX, minY, maxX, maxY, z);
        }

        // ================================================================== //
        //  VIEW VALIDATION
        // ================================================================== //

        public static void ValidateViewSupportsDetailItems(View view)
        {
            var supported = new[]
            {
                ViewType.FloorPlan,
                ViewType.CeilingPlan,
                ViewType.Section,
                ViewType.Elevation,
                ViewType.Detail,
                ViewType.DraftingView
            };

            if (!supported.Contains(view.ViewType))
                throw new InvalidOperationException(
                    $"Internal markups cannot be placed in view type '{view.ViewType}'. " +
                    "Supported: Floor Plan, Ceiling Plan, Section, Elevation, Detail, Drafting.");
        }

        public static void ValidateViewSupportsRevisionClouds(View view)
        {
            var unsupported = new[]
            {
                ViewType.ThreeD,
                ViewType.Schedule,
                ViewType.ColumnSchedule,
                ViewType.PanelSchedule
            };

            if (unsupported.Contains(view.ViewType))
                throw new InvalidOperationException(
                    $"Revision clouds cannot be placed in view type '{view.ViewType}'.");
        }

        // ================================================================== //
        //  FAMILY LOADING
        // ================================================================== //

        private FamilySymbol GetOrLoadDetailItemSymbol()
        {
            var name = _settings.DetailItemFamilyName;
            var symbol = FindFamilySymbol(name, BuiltInCategory.OST_DetailComponents);
            if (symbol != null) return symbol;

            var primaryPath = Path.Combine(_settings.FamilySearchRoot, name + ".rfa");
            if (TryLoadFamily(primaryPath, out _))
            {
                var s = FindFamilySymbol(name, BuiltInCategory.OST_DetailComponents);
                if (s != null) return s;
            }

            throw new InvalidOperationException(
                $"Family '{name}' could not be found in the project or loaded from:\n{primaryPath}\n" +
                "Please load the family manually and retry.");
        }

        private FamilySymbol GetOrLoadTagSymbol()
        {
            var name = _settings.TagFamilyName;
            var symbol = FindFamilySymbol(name, BuiltInCategory.OST_DetailComponentTags);
            if (symbol != null) return symbol;

            var primaryPath = Path.Combine(_settings.FamilySearchRoot, name + ".rfa");
            if (TryLoadFamily(primaryPath, out _))
            {
                var s = FindFamilySymbol(name, BuiltInCategory.OST_DetailComponentTags);
                if (s != null) return s;
            }

            throw new InvalidOperationException(
                $"Tag family '{name}' could not be found in the project or loaded from:\n{primaryPath}");
        }

        private FamilySymbol? FindFamilySymbol(string familyName, BuiltInCategory category)
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(category)
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.FamilyName == familyName);
        }

        private bool TryLoadFamily(string path, out Family? family)
        {
            family = null;
            if (!File.Exists(path)) return false;
            return _doc.LoadFamily(path, out family);
        }

        private static void EnsureSymbolActive(FamilySymbol symbol)
        {
            if (!symbol.IsActive)
                symbol.Activate();
        }

        // ================================================================== //
        //  TAG PLACEMENT
        // ================================================================== //

        private void PlaceMarkupTag(
            FamilyInstance markup,
            BoundingBoxXYZ boundingBox,
            View activeView)
        {
            FamilySymbol tagSymbol;
            try
            {
                tagSymbol = GetOrLoadTagSymbol();
                EnsureSymbolActive(tagSymbol);
            }
            catch
            {
                return;
            }

            double offsetX = UnitUtils.ConvertToInternalUnits(
                _settings.TagOffsetXMm, UnitTypeId.Millimeters);
            double offsetY = UnitUtils.ConvertToInternalUnits(
                _settings.TagOffsetYMm, UnitTypeId.Millimeters);

            var tagPoint = new XYZ(
                boundingBox.Max.X + offsetX,
                boundingBox.Max.Y + offsetY,
                boundingBox.Min.Z);

            IndependentTag.Create(
                _doc,
                tagSymbol.Id,
                activeView.Id,
                new Reference(markup),
                false,
                TagOrientation.Horizontal,
                tagPoint);
        }

        // ================================================================== //
        //  GEOMETRY HELPERS
        // ================================================================== //

        private BoundingBoxXYZ ExpandBoundingBox(
            double minX, double minY,
            double maxX, double maxY,
            double z)
        {
            double offset = UnitUtils.ConvertToInternalUnits(
                _settings.BoundingBoxOffsetMm, UnitTypeId.Millimeters);

            var bb = new BoundingBoxXYZ();
            bb.Min = new XYZ(minX - offset, minY - offset, z);
            bb.Max = new XYZ(maxX + offset, maxY + offset, z + 1.0);
            return bb;
        }

        private static XYZ GetBoundingBoxCentre(BoundingBoxXYZ bb)
            => new XYZ(
                (bb.Min.X + bb.Max.X) / 2.0,
                (bb.Min.Y + bb.Max.Y) / 2.0,
                bb.Min.Z);

        // <- CHANGED: renamed from BuildCurveLoopFromBoundingBox and now returns IList<Curve>.
        // RevisionCloud.Create requires a flat IList<Curve>, not IList<CurveLoop>.
        // The four boundary lines are passed directly without wrapping in a CurveLoop.
        private static IList<Curve> BuildCurvesFromBoundingBox(BoundingBoxXYZ bb)
        {
            double x0 = bb.Min.X, y0 = bb.Min.Y;
            double x1 = bb.Max.X, y1 = bb.Max.Y;
            double z = bb.Min.Z;

            var p0 = new XYZ(x0, y0, z);
            var p1 = new XYZ(x1, y0, z);
            var p2 = new XYZ(x1, y1, z);
            var p3 = new XYZ(x0, y1, z);

            return new List<Curve>
            {
                Line.CreateBound(p0, p1),
                Line.CreateBound(p1, p2),
                Line.CreateBound(p2, p3),
                Line.CreateBound(p3, p0)
            };
        }

        // <- CHANGED: kept as a separate method returning IList<CurveLoop>
        // for any future FilledRegion.Create usage which does require CurveLoop.
        // Not currently called but retained so it is available without reconstruction.
        private static IList<CurveLoop> BuildCurveLoopFromBoundingBox(BoundingBoxXYZ bb)
        {
            double x0 = bb.Min.X, y0 = bb.Min.Y;
            double x1 = bb.Max.X, y1 = bb.Max.Y;
            double z = bb.Min.Z;

            var p0 = new XYZ(x0, y0, z);
            var p1 = new XYZ(x1, y0, z);
            var p2 = new XYZ(x1, y1, z);
            var p3 = new XYZ(x0, y1, z);

            var loop = CurveLoop.Create(new List<Curve>
            {
                Line.CreateBound(p0, p1),
                Line.CreateBound(p1, p2),
                Line.CreateBound(p2, p3),
                Line.CreateBound(p3, p0)
            });

            return new List<CurveLoop> { loop };
        }

        // ================================================================== //
        //  PARAMETER HELPERS
        // ================================================================== //

        private static void SetLengthParameter(Element element, string paramName, double internalValue)
        {
            var param = element.LookupParameter(paramName);
            if (param == null || param.IsReadOnly)
                throw new InvalidOperationException(
                    $"Parameter '{paramName}' not found or is read-only on element {element.Id}.");
            param.Set(internalValue);
        }

        private void SetStringParameter(Element element, string paramName, string value)
        {
            var param = element.LookupParameter(paramName);

            if (param == null)
            {
                // Required parameter missing on this element -- attempt to create it
                // from the shared parameter file and bind it to the element's category
                // as an Instance parameter under Text, then retry the lookup once.
                EnsureSharedParameterBound(element.Category, paramName);
                param = element.LookupParameter(paramName);
            }

            if (param == null)
                throw new InvalidOperationException(
                    $"Shared parameter '{paramName}' could not be found or created for category " +
                    $"'{element.Category?.Name}' on element {element.Id}. " +
                    "Check the shared parameter file path in Markup settings and that the file is reachable.");

            if (param.IsReadOnly)
                throw new InvalidOperationException(
                    $"Shared parameter '{paramName}' is read-only on element {element.Id}.");

            param.Set(value);
        }

        /// <summary>
        /// Creates the named shared parameter from the configured shared parameter
        /// file if it doesn't already exist there, then binds it to the given
        /// category as an Instance parameter under the Text parameter group.
        /// No GUID hint is used -- matched/created purely by name. Any failure
        /// here (file unreachable, bind rejected, etc.) propagates as-is from
        /// SharedParameterBinder, which already throws clear, specific
        /// InvalidOperationExceptions -- not swallowed here, so the real reason
        /// reaches whatever catches this instead of being replaced by a generic
        /// "not found" message from the retry above.
        /// </summary>
        private void EnsureSharedParameterBound(Category? category, string paramName)
        {
            if (category == null) return;

            var app = _uiDoc.Application.Application;

            SharedParameterBinder.BindSharedParameter(
                app,
                _doc,
                _settings.SharedParameterFilePath,
                paramName,
                Guid.Empty,
                GroupTypeId.Text,
                isInstance: true,
                categories: new List<Category> { category },
                createIfMissing: true);
        }

        private static void SetStringParameterIfExists(Element element, string paramName, string value)
        {
            var param = element.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) return;
            param.Set(value);
        }
    }
}
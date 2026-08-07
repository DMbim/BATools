// BA/Markup/Services/MarkupService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Markup.Models;
using BA.Markup.Settings;

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
        private const string ParamBaAssignedUser = "BA.Tls_AssignedUser"; // <- NEW
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

            // <- NEW: BA_Tls_AssignedUser. Empty string is a valid value, means
            //    unassigned, matching MarkupInputModel.AssignedUser's own convention.
            //    BA_Tls_WIP and BA_Tls_Solved are intentionally NOT set here — they
            //    default to their family's own default value (false/unchecked) and
            //    are only ever toggled later via MarkupNotificationViewModel.
            SetStringParameter(instance, ParamBaAssignedUser, input.AssignedUser);

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
                    "No revision selected. A valid Revit Revision must be selected " +
                    "for Official Revision mode.");

            var revisionId = new ElementId(input.RevisionElementId);

            if (_doc.GetElement(revisionId) is not Revision)
                throw new InvalidOperationException(
                    $"Revision with ElementId {input.RevisionElementId} no longer " +
                    "exists in the document.");

            var curves = BuildCurvesFromBoundingBox(boundingBox);

            var cloud = RevisionCloud.Create(
                _doc,
                activeView,
                revisionId,
                curves);

            SetStringParameterIfExists(cloud, ParamBaType, input.BaType);
            SetStringParameterIfExists(cloud, ParamBaComments, input.BaComments);
            SetStringParameterIfExists(cloud, ParamBaMarkupDate, input.BaDate);
            SetStringParameterIfExists(cloud, ParamBaMarkupAuthor, input.BaAuthor);

            // <- NEW: lenient, same convention as the other BA_* writes on RevisionCloud
            //    above. If BA_Tls_AssignedUser isn't bound to the Revision Clouds
            //    category this silently no-ops rather than failing cloud placement.
            SetStringParameterIfExists(cloud, ParamBaAssignedUser, input.AssignedUser);

            // Place the BA_TAG_Revision tag on the cloud inside the same transaction.
            PlaceRevisionTag(cloud, boundingBox, activeView);

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
                throw new ArgumentException(
                    "No elements provided for bounding box calculation.");

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
                    "Could not compute a bounding box from the selected elements " +
                    "in the active view. Elements may not be visible in this view.");

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

            var path = Path.Combine(_settings.FamilySearchRoot, name + ".rfa");
            if (TryLoadFamily(path, out _))
            {
                var s = FindFamilySymbol(name, BuiltInCategory.OST_DetailComponents);
                if (s != null) return s;
            }

            throw new InvalidOperationException(
                $"Family '{name}' could not be found in the project or loaded from:" +
                $"\n{path}\nPlease load the family manually and retry.");
        }

        private FamilySymbol GetOrLoadTagSymbol()
        {
            var name = _settings.TagFamilyName;
            var symbol = FindFamilySymbol(name, BuiltInCategory.OST_DetailComponentTags);
            if (symbol != null) return symbol;

            var path = Path.Combine(_settings.FamilySearchRoot, name + ".rfa");
            if (TryLoadFamily(path, out _))
            {
                var s = FindFamilySymbol(name, BuiltInCategory.OST_DetailComponentTags);
                if (s != null) return s;
            }

            throw new InvalidOperationException(
                $"Tag family '{name}' could not be found in the project or loaded " +
                $"from:\n{path}");
        }

        // Loads BA_TAG_Revision from the same FamilySearchRoot.
        // Revision cloud tags are OST_RevisionCloudTags, not OST_DetailComponentTags.
        private FamilySymbol GetOrLoadRevisionTagSymbol()
        {
            var name = _settings.RevisionTagFamilyName;
            var symbol = FindFamilySymbol(name, BuiltInCategory.OST_RevisionCloudTags);
            if (symbol != null) return symbol;

            var path = Path.Combine(_settings.FamilySearchRoot, name + ".rfa");
            if (TryLoadFamily(path, out _))
            {
                var s = FindFamilySymbol(name, BuiltInCategory.OST_RevisionCloudTags);
                if (s != null) return s;
            }

            throw new InvalidOperationException(
                $"Revision tag family '{name}' could not be found in the project " +
                $"or loaded from:\n{path}");
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
                // Tag family missing — non-fatal for internal markup placement.
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

        // Places BA_TAG_Revision on a RevisionCloud element.
        // Called from PlaceRevisionCloud inside the same transaction.
        // Non-fatal if the tag family is missing — logs the skip silently.
        // RevisionCloud is tagged via IndependentTag.Create using a Reference
        // to the cloud element. The tag point is offset from the top-right
        // corner of the bounding box, same convention as the markup tag.
        private void PlaceRevisionTag(
            RevisionCloud cloud,
            BoundingBoxXYZ boundingBox,
            View activeView)
        {
            FamilySymbol tagSymbol;
            try
            {
                tagSymbol = GetOrLoadRevisionTagSymbol();
                EnsureSymbolActive(tagSymbol);
            }
            catch
            {
                // Tag family missing — non-fatal. Cloud is placed without a tag.
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
                new Reference(cloud),
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

        // Winding order is counter-clockwise (CCW) so Revit computes an upward-facing
        // normal and draws cloud arcs on the correct side.
        private static IList<Curve> BuildCurvesFromBoundingBox(BoundingBoxXYZ bb)
        {
            double x0 = bb.Min.X, y0 = bb.Min.Y;
            double x1 = bb.Max.X, y1 = bb.Max.Y;
            double z = bb.Min.Z;

            var p0 = new XYZ(x0, y0, z); // bottom-left
            var p1 = new XYZ(x0, y1, z); // top-left
            var p2 = new XYZ(x1, y1, z); // top-right
            var p3 = new XYZ(x1, y0, z); // bottom-right

            return new List<Curve>
    {
        Line.CreateBound(p0, p1), // left edge, bottom to top
        Line.CreateBound(p1, p2), // top edge, left to right
        Line.CreateBound(p2, p3), // right edge, top to bottom
        Line.CreateBound(p3, p0)  // bottom edge, right to left
    };
        }


        // ================================================================== //
        //  PARAMETER HELPERS
        // ================================================================== //

        private static void SetLengthParameter(
            Element element, string paramName, double internalValue)
        {
            var param = element.LookupParameter(paramName);
            if (param == null || param.IsReadOnly)
                throw new InvalidOperationException(
                    $"Parameter '{paramName}' not found or is read-only on " +
                    $"element {element.Id}.");
            param.Set(internalValue);
        }

        private static void SetStringParameter(
            Element element, string paramName, string value)
        {
            var param = element.LookupParameter(paramName);
            if (param == null)
                throw new InvalidOperationException(
                    $"Shared parameter '{paramName}' not found on element {element.Id}. " +
                    "Verify the parameter is bound to the correct category.");
            if (param.IsReadOnly)
                throw new InvalidOperationException(
                    $"Shared parameter '{paramName}' is read-only on element {element.Id}.");
            param.Set(value);
        }

        private static void SetStringParameterIfExists(
            Element element, string paramName, string value)
        {
            var param = element.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) return;
            param.Set(value);
        }
    }
}
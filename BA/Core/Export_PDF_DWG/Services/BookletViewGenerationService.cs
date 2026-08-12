using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace BA.Core.Export.Services
{
    /// <summary>
    /// Creates a floor plan, a section, and an isometric 3D view around
    /// one placed FamilyInstance. Must be called from a valid Revit API
    /// thread context, inside an open transaction.
    ///
    /// The floor plan and section share one orientation/extent computation
    /// (ComputeOrientedExtents), only the section's depth (Z) range
    /// differs, spanning the wall thickness so the cut actually happens.
    /// ViewSection.CreateSection requires its ViewFamilyType to actually
    /// be ViewFamily.Section, confirmed by a live Revit exception during
    /// testing, not documentation alone.
    ///
    /// The 3D view needs no orientation computation at all,
    /// View3D.CreateIsometric already picks Revit's own default isometric
    /// angle, this only isolates it to the instance via a world-aligned
    /// SetSectionBox, confirmed against the live API docs.
    ///
    /// Orientation sign for the section is a genuine residual uncertainty:
    /// which physical face Wall.Orientation points toward (interior or
    /// exterior) depends on how the wall was drawn, not something this can
    /// know in general. If a generated section looks mirrored or is
    /// facing the wrong way, flip the sign in ComputeViewDirection.
    /// </summary>
    public static class BookletViewGenerationService
    {
        private const double DefaultDepthClearanceFeet = 0.5;

        public static (ViewPlan FloorPlan, ViewSection Section, View3D Isometric, string ErrorMessage) CreateViews(
            Document doc,
            FamilyInstance instance,
            double cropMarginMm,
            int viewScale,
            ViewDetailLevel detailLevel)
        {
            var marginFeet = UnitUtils.ConvertToInternalUnits(cropMarginMm, UnitTypeId.Millimeters);

            var (floorPlanView, floorPlanError) = CreateFloorPlan(doc, instance, marginFeet, viewScale, detailLevel);

            if (floorPlanView == null)
            {
                return (null, null, null, floorPlanError);
            }

            var (sectionView, sectionError) = CreateSection(doc, instance, marginFeet, viewScale, detailLevel);

            if (sectionView == null)
            {
                return (null, null, null, sectionError);
            }

            var (isometricView, isometricError) = CreateIsometricView(doc, instance, marginFeet, viewScale, detailLevel);

            if (isometricView == null)
            {
                return (null, null, null, isometricError);
            }

            return (floorPlanView, sectionView, isometricView, string.Empty);
        }

        private static (ViewSection View, string ErrorMessage) CreateSection(
            Document doc,
            FamilyInstance instance,
            double marginFeet,
            int viewScale,
            ViewDetailLevel detailLevel)
        {
            var extents = ComputeOrientedExtents(instance, marginFeet, out var wallWidthFeet, out var errorMessage);

            if (extents == null)
            {
                return (null, errorMessage);
            }

            var sectionType = FindViewFamilyType(doc, ViewFamily.Section);

            if (sectionType == null)
            {
                return (null, "No Section ViewFamilyType exists in this document's templates, cannot create the section view.");
            }

            // Box spans the wall thickness (or a reasonable default depth
            // for non wall hosted instances), so the cut plane actually
            // passes through the assembly, unlike the elevation's box in
            // the earlier version of this service, this one is meant to
            // cut.
            var halfDepth = (wallWidthFeet > 0 ? wallWidthFeet / 2 : 0.5) + DefaultDepthClearanceFeet;

            var sectionBox = new BoundingBoxXYZ
            {
                Transform = extents.Transform,
                Min = new XYZ(extents.MinX, extents.MinY, -halfDepth),
                Max = new XYZ(extents.MaxX, extents.MaxY, halfDepth)
            };

            try
            {
                var view = ViewSection.CreateSection(doc, sectionType.Id, sectionBox);

                // A freshly created view's properties are not reliably
                // settable until the document regenerates, confirmed
                // during live testing, same class of trap already known
                // in this project for Wall.Orientation needing
                // Regenerate() right after Wall.Create().
                doc.Regenerate();

                ApplyViewSettings(view, viewScale, detailLevel);
                return (view, string.Empty);
            }
            catch (Exception ex)
            {
                return (null, $"Section view creation failed: {ex.Message}");
            }
        }

        private static (View3D View, string ErrorMessage) CreateIsometricView(
            Document doc,
            FamilyInstance instance,
            double marginFeet,
            int viewScale,
            ViewDetailLevel detailLevel)
        {
            var boundingBox = instance.get_BoundingBox(null);

            if (boundingBox == null)
            {
                return (null, "The representative instance has no geometry in the current view context (get_BoundingBox returned null).");
            }

            var threeDType = FindViewFamilyType(doc, ViewFamily.ThreeDimensional);

            if (threeDType == null)
            {
                return (null, "No 3D ViewFamilyType exists in this document's templates, cannot create the isometric view.");
            }

            View3D view;

            try
            {
                view = View3D.CreateIsometric(doc, threeDType.Id);
                doc.Regenerate();
            }
            catch (Exception ex)
            {
                return (null, $"Isometric view creation failed: {ex.Message}");
            }

            try
            {
                var margin = new XYZ(marginFeet, marginFeet, marginFeet);

                view.SetSectionBox(new BoundingBoxXYZ
                {
                    Transform = Transform.Identity,
                    Min = boundingBox.Min - margin,
                    Max = boundingBox.Max + margin
                });

                view.IsSectionBoxActive = true;
            }
            catch (Exception ex)
            {
                return (null, $"Failed to isolate the isometric view to the instance: {ex.Message}");
            }

            ApplyViewSettings(view, viewScale, detailLevel);
            return (view, string.Empty);
        }

        private static (ViewPlan View, string ErrorMessage) CreateFloorPlan(
            Document doc,
            FamilyInstance instance,
            double marginFeet,
            int viewScale,
            ViewDetailLevel detailLevel)
        {
            var levelId = ResolveLevelId(instance);

            if (levelId == ElementId.InvalidElementId)
            {
                return (null, "Could not determine this instance's level, cannot find a floor plan to base the booklet plan on.");
            }

            var existingPlan = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan && v.GenLevel != null && v.GenLevel.Id == levelId);

            if (existingPlan == null)
            {
                return (null, "No floor plan view exists for this instance's level, cannot generate a plan for this type.");
            }

            ViewPlan newPlan;

            try
            {
                var newViewId = existingPlan.Duplicate(ViewDuplicateOption.WithDetailing);
                newPlan = doc.GetElement(newViewId) as ViewPlan;

                if (newPlan == null)
                {
                    return (null, "Duplicating the floor plan did not produce a usable view.");
                }

                doc.Regenerate();
            }
            catch (Exception ex)
            {
                return (null, $"Failed to duplicate the floor plan: {ex.Message}");
            }

            try
            {
                newPlan.Name = $"BA Booklet - {instance.Id.Value} - Plan";
            }
            catch
            {
                // Non fatal, a name collision just leaves the default
                // "Copy of ..." name, the view itself is still usable.
            }

            var boundingBox = instance.get_BoundingBox(null);

            if (boundingBox != null)
            {
                try
                {
                    var existingCropBox = newPlan.CropBox;
                    var inverse = existingCropBox.Transform.Inverse;

                    double minX = double.MaxValue, maxX = double.MinValue;
                    double minY = double.MaxValue, maxY = double.MinValue;

                    foreach (var corner in GetCorners(boundingBox))
                    {
                        var local = inverse.OfPoint(corner);
                        minX = Math.Min(minX, local.X);
                        maxX = Math.Max(maxX, local.X);
                        minY = Math.Min(minY, local.Y);
                        maxY = Math.Max(maxY, local.Y);
                    }

                    newPlan.CropBox = new BoundingBoxXYZ
                    {
                        Transform = existingCropBox.Transform,
                        Min = new XYZ(minX - marginFeet, minY - marginFeet, existingCropBox.Min.Z),
                        Max = new XYZ(maxX + marginFeet, maxY + marginFeet, existingCropBox.Max.Z)
                    };

                    newPlan.CropBoxActive = true;
                    newPlan.CropBoxVisible = false;
                }
                catch
                {
                    // A crop failure still leaves a usable, if uncropped,
                    // floor plan view, not worth failing the whole type
                    // over.
                }
            }

            ApplyViewSettings(newPlan, viewScale, detailLevel);

            return (newPlan, string.Empty);
        }

        /// <summary>
        /// Shared orientation and crop extent computation for the section
        /// view. Kept as its own helper since the elevation-style
        /// workaround this project no longer uses and the section both
        /// needed this exact math, only their depth range differed.
        /// </summary>
        private class OrientedExtents
        {
            public Transform Transform;
            public double MinX, MaxX, MinY, MaxY;
        }

        private static OrientedExtents ComputeOrientedExtents(FamilyInstance instance, double marginFeet, out double wallWidthFeet, out string errorMessage)
        {
            errorMessage = string.Empty;
            wallWidthFeet = 0;

            var boundingBox = instance.get_BoundingBox(null);

            if (boundingBox == null)
            {
                errorMessage = "The representative instance has no geometry in the current view context (get_BoundingBox returned null).";
                return null;
            }

            var viewDirection = ComputeViewDirection(instance, out wallWidthFeet);
            var up = XYZ.BasisZ;

            var right = viewDirection.CrossProduct(up);

            if (right.IsZeroLength())
            {
                right = XYZ.BasisX;
            }

            right = right.Normalize();
            up = right.CrossProduct(viewDirection).Normalize();

            var transform = Transform.Identity;
            transform.Origin = (boundingBox.Min + boundingBox.Max) * 0.5;
            transform.BasisX = right;
            transform.BasisY = up;
            transform.BasisZ = viewDirection;

            var inverse = transform.Inverse;

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            foreach (var corner in GetCorners(boundingBox))
            {
                var local = inverse.OfPoint(corner);
                minX = Math.Min(minX, local.X);
                maxX = Math.Max(maxX, local.X);
                minY = Math.Min(minY, local.Y);
                maxY = Math.Max(maxY, local.Y);
            }

            return new OrientedExtents
            {
                Transform = transform,
                MinX = minX - marginFeet,
                MaxX = maxX + marginFeet,
                MinY = minY - marginFeet,
                MaxY = maxY + marginFeet
            };
        }

        private static void ApplyViewSettings(View view, int viewScale, ViewDetailLevel detailLevel)
        {
            if (viewScale > 0)
            {
                view.Scale = viewScale;
            }

            view.DetailLevel = detailLevel;
        }

        private static ElementId ResolveLevelId(FamilyInstance instance)
        {
            if (instance.LevelId != null && instance.LevelId != ElementId.InvalidElementId)
            {
                return instance.LevelId;
            }

            if (instance.Host is Wall wall && wall.LevelId != ElementId.InvalidElementId)
            {
                return wall.LevelId;
            }

            return ElementId.InvalidElementId;
        }

        private static XYZ ComputeViewDirection(FamilyInstance instance, out double wallWidthFeet)
        {
            wallWidthFeet = 0;

            if (instance.Host is Wall wall)
            {
                wallWidthFeet = wall.Width;
                return wall.Orientation.Normalize();
            }

            if (instance.FacingOrientation != null && !instance.FacingOrientation.IsZeroLength())
            {
                return instance.FacingOrientation.Normalize();
            }

            return XYZ.BasisY;
        }

        private static ViewFamilyType FindViewFamilyType(Document doc, ViewFamily family)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == family);
        }

        private static XYZ[] GetCorners(BoundingBoxXYZ box)
        {
            return new[]
            {
                new XYZ(box.Min.X, box.Min.Y, box.Min.Z),
                new XYZ(box.Max.X, box.Min.Y, box.Min.Z),
                new XYZ(box.Min.X, box.Max.Y, box.Min.Z),
                new XYZ(box.Max.X, box.Max.Y, box.Min.Z),
                new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
            };
        }
    }
}

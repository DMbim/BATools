using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.QA.FamilyVersioning.Models;

namespace BA.QA.FamilyVersioning.Dashboard
{
    public sealed class ReviewRequest
    {
        public string FamilyName { get; }
        public string CategoryName { get; }
        public List<Building> TargetBuildings { get; }

        /// <summary>
        /// The document path of the model in which review views should be created.
        /// Captured at request time from the active document so the handler does not
        /// rely on ActiveUIDocument at execution time, which may point to a different
        /// document if the user switched focus between clicking Review and Revit
        /// processing the ExternalEvent.
        /// </summary>
        public string TargetDocumentPath { get; }

        public ReviewRequest(
            string familyName,
            string categoryName,
            List<Building> targetBuildings,
            string targetDocumentPath)
        {
            FamilyName = familyName ?? throw new ArgumentNullException(nameof(familyName));
            CategoryName = categoryName ?? throw new ArgumentNullException(nameof(categoryName));
            TargetBuildings = targetBuildings ?? throw new ArgumentNullException(nameof(targetBuildings));
            TargetDocumentPath = targetDocumentPath ?? throw new ArgumentNullException(nameof(targetDocumentPath));
        }
    }

    public sealed class FamilyReviewEventHandler : IExternalEventHandler
    {
        private static readonly Color[] BuildingColors =
        {
            new Color(220, 80,  120),
            new Color(80,  160, 220),
            new Color(80,  200, 120),
            new Color(220, 160, 60),
            new Color(160, 100, 220),
            new Color(60,  200, 200),
        };

        private ReviewRequest? _pendingRequest;
        public List<ElementId> CreatedViewIds { get; } = new();

        public void SetRequest(ReviewRequest request)
        {
            _pendingRequest = request ?? throw new ArgumentNullException(nameof(request));
        }

        public string GetName() => "BA.FamilyVersioning.ReviewFamily";

        public void Execute(UIApplication app)
        {
            var request = _pendingRequest;
            if (request == null) return;
            _pendingRequest = null;

            // Resolve the target document by path, not by ActiveUIDocument.
            // This ensures views are always created in the correct document
            // regardless of which document is active when the event fires.
            Document? doc = null;
            foreach (Document openDoc in app.Application.Documents)
            {
                if (string.Equals(openDoc.PathName, request.TargetDocumentPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    doc = openDoc;
                    break;
                }
            }

            if (doc == null)
            {
                AppLogger.LogError("FamilyReviewEventHandler",
                    new InvalidOperationException(
                        $"Target document '{request.TargetDocumentPath}' is not open. " +
                        "Cannot create review views."));
                return;
            }

            // Get a UIDocument for this specific document so we can set ActiveView.
            var uiDoc = GetUIDocument(app, doc);
            if (uiDoc == null)
            {
                AppLogger.LogError("FamilyReviewEventHandler",
                    new InvalidOperationException(
                        $"Could not get UIDocument for '{request.TargetDocumentPath}'."));
                return;
            }

            try
            {
                var default3DView = FindDefault3DView(doc);
                if (default3DView == null)
                {
                    TaskDialog.Show("Family Review",
                        "No default {3D} view found in the target document. " +
                        "Create a default 3D view first.");
                    return;
                }

                var allLinkInstances = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .ToList();

                var createdViews = new List<(View3D View, string BuildingName)>();
                var colorIndex = 0;

                foreach (var building in request.TargetBuildings)
                {
                    var linkInstance = FindLinkInstanceForBuilding(allLinkInstances, building);
                    if (linkInstance == null)
                    {
                        AppLogger.LogInfo(
                            $"[FamilyReview] No loaded link found for building '{building.BuildingName}' " +
                            $"at path '{building.CentralModelPath}'. Skipping.");
                        continue;
                    }

                    var linkDoc = linkInstance.GetLinkDocument();
                    if (linkDoc == null)
                    {
                        AppLogger.LogInfo(
                            $"[FamilyReview] Link document for '{building.BuildingName}' returned null.");
                        continue;
                    }

                    var instanceInLink = FindFirstFamilyInstance(linkDoc, request.FamilyName);
                    if (instanceInLink == null)
                    {
                        AppLogger.LogInfo(
                            $"[FamilyReview] No instance of '{request.FamilyName}' found in " +
                            $"'{building.BuildingName}'.");
                        continue;
                    }

                    var boundingBoxInLink = instanceInLink.get_BoundingBox(null);
                    if (boundingBoxInLink == null) continue;

                    var transform = linkInstance.GetTotalTransform();
                    var coordModelBBox = TransformBoundingBox(boundingBoxInLink, transform);
                    var buildingColor = BuildingColors[colorIndex % BuildingColors.Length];
                    colorIndex++;

                    using (var tx = new Transaction(doc,
                        $"BA Review View - {request.FamilyName} - {building.BuildingName}"))
                    {
                        tx.Start();

                        // Generate a unique view name. A timestamp suffix avoids the
                        // "Name must be unique" exception when Review is clicked
                        // multiple times for the same family before cleanup runs.
                        var baseName = $"BA_Review_{request.FamilyName}_{building.BuildingName}"
                            .Replace(" ", "_")
                            .Replace("/", "_")
                            .Replace("\\", "_");

                        var viewName = EnsureUniqueName(doc, baseName);

                        var newView = (View3D)doc.GetElement(
                            default3DView.Duplicate(ViewDuplicateOption.Duplicate));

                        newView.Name = viewName;

                        const double paddingFeet = 3.0;
                        newView.SetSectionBox(PadBoundingBox(coordModelBBox, paddingFeet));
                        newView.IsSectionBoxActive = true;

                        var overrideSettings = new OverrideGraphicSettings();
                        overrideSettings.SetProjectionLineColor(buildingColor);
                        overrideSettings.SetSurfaceForegroundPatternColor(buildingColor);
                        overrideSettings.SetSurfaceForegroundPatternVisible(true);
                        newView.SetElementOverrides(linkInstance.Id, overrideSettings);

                        tx.Commit();

                        createdViews.Add((newView, building.BuildingName));
                        CreatedViewIds.Add(newView.Id);

                        AppLogger.LogInfo(
                            $"[FamilyReview] Created view '{viewName}' in '{doc.PathName}'.");
                    }
                }

                if (createdViews.Count == 0)
                {
                    TaskDialog.Show("Family Review",
                        $"No instances of '{request.FamilyName}' were found in any loaded building link.");
                    return;
                }

                foreach (var (view, _) in createdViews)
                {
                    uiDoc.ActiveView = view;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("FamilyReviewEventHandler.Execute", ex);
                TaskDialog.Show("Family Review Error", ex.Message);
            }
        }
        private static UIDocument? GetUIDocument(UIApplication app, Document doc)
        {
            // Try to find the UIDocument that wraps the given Document.
            // If the document is not the active one, this will return null.
            // There is no public API to get a UIDocument for an arbitrary Document,
            // so we must check if it's the active document.
            if (app.ActiveUIDocument != null && app.ActiveUIDocument.Document.Equals(doc))
            {
                return app.ActiveUIDocument;
            }
            return null;
        }
        /// <summary>
        /// Returns a view name that does not already exist in the document. If the
        /// base name is taken, appends an incrementing integer suffix until a unique
        /// name is found. This prevents the ArgumentException from Element.set_Name
        /// when Review is triggered multiple times for the same family.
        /// </summary>
        private static string EnsureUniqueName(Document doc, string baseName)
        {
            var existingNames = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .Where(v => !v.IsTemplate)
                .Select(v => v.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!existingNames.Contains(baseName))
            {
                return baseName;
            }

            var counter = 1;
            while (existingNames.Contains($"{baseName}_{counter}"))
            {
                counter++;
            }

            return $"{baseName}_{counter}";
        }

        private static View3D? FindDefault3DView(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name == "{3D}" && !v.IsCallout);
        }

        private static RevitLinkInstance? FindLinkInstanceForBuilding(
            List<RevitLinkInstance> linkInstances, Building building)
        {
            foreach (var link in linkInstances)
            {
                var linkType = link.Document.GetElement(link.GetTypeId()) as RevitLinkType;
                if (linkType == null) continue;

                var extRef = linkType.GetExternalFileReference();
                if (extRef == null) continue;

                var absolutePath = ModelPathUtils.ConvertModelPathToUserVisiblePath(
                    extRef.GetAbsolutePath());

                if (string.Equals(absolutePath, building.CentralModelPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return link;
                }
            }

            return null;
        }

        private static FamilyInstance? FindFirstFamilyInstance(Document linkDoc, string familyName)
        {
            return new FilteredElementCollector(linkDoc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .FirstOrDefault(fi =>
                    string.Equals(fi.Symbol?.Family?.Name, familyName,
                        StringComparison.OrdinalIgnoreCase));
        }

        private static BoundingBoxXYZ TransformBoundingBox(BoundingBoxXYZ bbox, Transform transform)
        {
            var corners = new[]
            {
                new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Min.Z),
                new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Min.Z),
                new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Min.Z),
                new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Min.Z),
                new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Max.Z),
                new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Max.Z),
                new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Max.Z),
                new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Max.Z),
            };

            var transformedCorners = corners.Select(c => transform.OfPoint(c)).ToList();

            return new BoundingBoxXYZ
            {
                Min = new XYZ(
                    transformedCorners.Min(c => c.X),
                    transformedCorners.Min(c => c.Y),
                    transformedCorners.Min(c => c.Z)),
                Max = new XYZ(
                    transformedCorners.Max(c => c.X),
                    transformedCorners.Max(c => c.Y),
                    transformedCorners.Max(c => c.Z))
            };
        }

        private static BoundingBoxXYZ PadBoundingBox(BoundingBoxXYZ bbox, double padding)
        {
            return new BoundingBoxXYZ
            {
                Min = new XYZ(bbox.Min.X - padding, bbox.Min.Y - padding, bbox.Min.Z - padding),
                Max = new XYZ(bbox.Max.X + padding, bbox.Max.Y + padding, bbox.Max.Z + padding)
            };
        }
    }

    public sealed class ReviewViewCleanupEventHandler : IExternalEventHandler
    {
        private List<ElementId>? _viewIdsToDelete;
        private string? _targetDocumentPath;

        public void SetViewIds(List<ElementId> viewIds, string targetDocumentPath)
        {
            _viewIdsToDelete = viewIds;
            _targetDocumentPath = targetDocumentPath;
        }

        public string GetName() => "BA.FamilyVersioning.CleanupReviewViews";

        public void Execute(UIApplication app)
        {
            var viewIds = _viewIdsToDelete;
            var targetPath = _targetDocumentPath;

            if (viewIds == null || viewIds.Count == 0) return;
            _viewIdsToDelete = null;
            _targetDocumentPath = null;

            // Resolve the target document by path, same rationale as the review handler.
            Document? doc = null;
            foreach (Document openDoc in app.Application.Documents)
            {
                if (string.Equals(openDoc.PathName, targetPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    doc = openDoc;
                    break;
                }
            }

            if (doc == null)
            {
                AppLogger.LogInfo(
                    $"[FamilyReview] Cleanup skipped: target document '{targetPath}' is not open.");
                return;
            }

            AppLogger.LogInfo(
                $"[FamilyReview] Cleanup triggered. Document: '{doc.PathName}', " +
                $"ViewIds to process: {viewIds.Count}");

            try
            {
                var existingIds = viewIds
                    .Where(id => doc.GetElement(id) != null)
                    .ToList();

                AppLogger.LogInfo(
                    $"[FamilyReview] Existing view IDs found in document: {existingIds.Count}");

                if (existingIds.Count == 0) return;

                // Cannot delete the currently active view. Switch to the default
                // {3D} view first if any of the views to delete is currently active.
                var uiDoc = GetUIDocument(app, doc);
                if (uiDoc != null)
                {
                    var activeViewId = uiDoc.ActiveView?.Id;
                    if (activeViewId != null && existingIds.Any(id => id == activeViewId))
                    {
                        var fallback = new FilteredElementCollector(doc)
                            .OfClass(typeof(View3D))
                            .Cast<View3D>()
                            .FirstOrDefault(v => !v.IsTemplate &&
                                                  v.Name == "{3D}" &&
                                                  !existingIds.Contains(v.Id));

                        if (fallback != null)
                        {
                            uiDoc.ActiveView = fallback;
                        }
                    }
                }

                using (var tx = new Transaction(doc, "BA Delete Review Views"))
                {
                    tx.Start();
                    doc.Delete(existingIds);
                    tx.Commit();
                }

                AppLogger.LogInfo(
                    $"[FamilyReview] Deleted {existingIds.Count} temporary review view(s).");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ReviewViewCleanupEventHandler.Execute", ex);
            }
        }
        private static UIDocument? GetUIDocument(UIApplication app, Document doc)
        {
            // Try to find the UIDocument that wraps the given Document.
            // If the document is not the active one, this will return null.
            // There is no public API to get a UIDocument for an arbitrary Document,
            // so we must check if it's the active document.
            if (app.ActiveUIDocument != null && app.ActiveUIDocument.Document.Equals(doc))
            {
                return app.ActiveUIDocument;
            }
            return null;
        }
    }
}

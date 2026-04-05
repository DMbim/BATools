using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;

namespace BA.Core.ViewTemplates
{
    public sealed class ApplyCategoryGraphicsRequest
    {
        public ElementId SourceTemplateId { get; set; } = ElementId.InvalidElementId;
        public List<ElementId> TargetTemplateIds { get; } = new List<ElementId>();
        public List<ElementId> SelectedCategoryIds { get; } = new List<ElementId>();

        public bool CopyVisibility { get; set; } = true;
        public bool CopyProjectionOverrides { get; set; } = true;
        public bool CopyCutOverrides { get; set; } = true;

        public Action<ApplyCategoryGraphicsResult>? OnSuccess { get; set; }
        public Action<Exception>? OnError { get; set; }
    }

    public sealed class ApplyCategoryGraphicsHandler : IExternalEventHandler
    {
        private ApplyCategoryGraphicsRequest? _pending;

        public void Raise(ApplyCategoryGraphicsRequest request)
        {
            _pending = request ?? throw new ArgumentNullException(nameof(request));
        }

        public void Execute(UIApplication app)
        {
            ApplyCategoryGraphicsRequest? request = _pending;
            _pending = null;

            if (request == null)
                return;

            try
            {
                Document doc = app.ActiveUIDocument?.Document
                    ?? throw new InvalidOperationException("No active Revit document.");

                ApplyCategoryGraphicsResult result =
                    ViewTemplateGraphicsTransferService.ApplySelectedModelCategoryGraphics(
                        doc,
                        request.SourceTemplateId,
                        request.TargetTemplateIds,
                        request.SelectedCategoryIds,
                        request.CopyVisibility,
                        request.CopyProjectionOverrides,
                        request.CopyCutOverrides);

                request.OnSuccess?.Invoke(result);
            }
            catch (Exception ex)
            {
                request.OnError?.Invoke(ex);
            }
        }

        public string GetName()
        {
            return "Apply Selected Category Graphics To Templates";
        }
    }
}
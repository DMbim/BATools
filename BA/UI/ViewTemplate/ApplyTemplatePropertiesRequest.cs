using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.ViewTemplates
{
    public sealed class ApplyTemplatePropertiesRequest
    {
        public ElementId SourceTemplateId { get; set; } = ElementId.InvalidElementId;
        public List<ElementId> TargetTemplateIds { get; } = new List<ElementId>();
        public List<ElementId> SelectedParameterIds { get; } = new List<ElementId>();

        public Action<ApplyTemplatePropertiesResult>? OnSuccess { get; set; }
        public Action<Exception>? OnError { get; set; }
    }

    public sealed class ApplyTemplatePropertiesHandler : IExternalEventHandler
    {
        private ApplyTemplatePropertiesRequest? _pending;

        public void Raise(ApplyTemplatePropertiesRequest request)
        {
            _pending = request ?? throw new ArgumentNullException(nameof(request));
        }

        public void Execute(UIApplication app)
        {
            ApplyTemplatePropertiesRequest? request = _pending;
            _pending = null;

            if (request == null)
                return;

            try
            {
                Document doc = app.ActiveUIDocument?.Document
                    ?? throw new InvalidOperationException("No active Revit document.");

                ApplyTemplatePropertiesResult result =
                    ViewTemplateTransferService.ApplySelectedProperties(
                        doc,
                        request.SourceTemplateId,
                        request.TargetTemplateIds,
                        request.SelectedParameterIds);

                request.OnSuccess?.Invoke(result);
            }
            catch (Exception ex)
            {
                request.OnError?.Invoke(ex);
            }
        }

        public string GetName()
        {
            return "Apply Selected View Template Properties";
        }
    }
}
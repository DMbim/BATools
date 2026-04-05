using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;

namespace BA.Core.ViewTemplates
{
    public sealed class ApplyViewFiltersRequest
    {
        public ElementId SourceTemplateId { get; set; } = ElementId.InvalidElementId;
        public List<ElementId> TargetTemplateIds { get; } = new List<ElementId>();
        public List<ElementId> SelectedFilterIds { get; } = new List<ElementId>();

        public bool CopyEnabledState { get; set; } = true;
        public bool CopyVisibility { get; set; } = true;
        public bool CopyOverrides { get; set; } = true;
        public bool PreserveOrder { get; set; } = false;

        public Action<ApplyViewFiltersResult>? OnSuccess { get; set; }
        public Action<Exception>? OnError { get; set; }
    }

    public sealed class ApplyViewFiltersHandler : IExternalEventHandler
    {
        private ApplyViewFiltersRequest? _pending;

        public void Raise(ApplyViewFiltersRequest request)
        {
            _pending = request ?? throw new ArgumentNullException(nameof(request));
        }

        public void Execute(UIApplication app)
        {
            ApplyViewFiltersRequest? request = _pending;
            _pending = null;

            if (request == null)
                return;

            try
            {
                Document doc = app.ActiveUIDocument?.Document
                    ?? throw new InvalidOperationException("No active Revit document.");

                ApplyViewFiltersResult result =
                    ViewTemplateFilterTransferService.ApplySelectedFilters(
                        doc,
                        request.SourceTemplateId,
                        request.TargetTemplateIds,
                        request.SelectedFilterIds,
                        request.CopyEnabledState,
                        request.CopyVisibility,
                        request.CopyOverrides,
                        request.PreserveOrder);

                request.OnSuccess?.Invoke(result);
            }
            catch (Exception ex)
            {
                request.OnError?.Invoke(ex);
            }
        }

        public string GetName()
        {
            return "Apply Selected View Filters To Templates";
        }
    }
}
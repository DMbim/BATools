using Autodesk.Revit.UI;
using BA.Core.Content.Models;
using BA.Core.Content.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BA.Core.Content.Revit
{
    public sealed class ContentPreviewExportExternalHandler : IExternalEventHandler
    {
        private ContentPreviewExportRequest? _request;
        private string _lastError = string.Empty;
        private List<ContentPreviewExportItemResult> _lastResults = new();

        public void SetRequest(ContentPreviewExportRequest request)
        {
            _request = request ?? throw new ArgumentNullException(nameof(request));
            _lastError = string.Empty;
            _lastResults.Clear();
        }

        public string ConsumeLastError()
        {
            string error = _lastError;
            _lastError = string.Empty;
            return error;
        }

        public IReadOnlyList<ContentPreviewExportItemResult> ConsumeLastResults()
        {
            List<ContentPreviewExportItemResult> copy = _lastResults.ToList();
            _lastResults.Clear();
            return copy;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                if (_request == null)
                {
                    _lastError = "Preview export request was null.";
                    return;
                }

                if (_request.FamilyPaths == null || _request.FamilyPaths.Count == 0)
                {
                    _lastError = "Preview export request contains no family paths.";
                    return;
                }

                var service = new FamilyPreviewExportService(app);
                IReadOnlyList<ContentPreviewExportItemResult> results =
                    service.ExportPreviews(_request.FamilyPaths, _request.OverwriteExisting);

                _lastResults = results.ToList();

                List<string> errors = new();

                foreach (ContentPreviewExportItemResult result in results)
                {
                    if (!result.Success)
                        errors.Add($"{Path.GetFileName(result.FamilyPath)} -> {result.Message}");
                }

                if (errors.Count > 0)
                    _lastError = string.Join(Environment.NewLine, errors);
            }
            catch (Exception ex)
            {
                _lastError = ex.ToString();
                _lastResults = new List<ContentPreviewExportItemResult>();
            }
            finally
            {
                _request = null;
            }
        }

        public string GetName()
        {
            return "BA Content Preview Export External Handler";
        }
    }
}
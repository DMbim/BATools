using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using BA.Telemetry.Models;

namespace BA.Telemetry.Services
{
    public class TelemetryService : IDisposable
    {
        private readonly UIControlledApplication _uiControlledApp;
        private readonly TelemetryRepository _repository;
        private UIApplication _uiApp;
        private bool _disposed = false;

        public TelemetryService(UIControlledApplication uiControlledApp)
        {
            _uiControlledApp = uiControlledApp
                ?? throw new ArgumentNullException(nameof(uiControlledApp));

            _repository = new TelemetryRepository();
        }

        public void Start()
        {
            _uiControlledApp.ControlledApplication.DocumentOpened += OnDocumentOpened;
            _uiControlledApp.ControlledApplication.DocumentClosed += OnDocumentClosed;
            _uiControlledApp.ControlledApplication.DocumentSaved += OnDocumentSaved;
            _uiControlledApp.ControlledApplication.DocumentSavedAs += OnDocumentSavedAs;
            _uiControlledApp.ControlledApplication.DocumentSynchronizedWithCentral += OnDocumentSynced;
            _uiControlledApp.ViewActivated += OnViewActivated;
        }

        public void Stop()
        {
            _uiControlledApp.ControlledApplication.DocumentOpened -= OnDocumentOpened;
            _uiControlledApp.ControlledApplication.DocumentClosed -= OnDocumentClosed;
            _uiControlledApp.ControlledApplication.DocumentSaved -= OnDocumentSaved;
            _uiControlledApp.ControlledApplication.DocumentSavedAs -= OnDocumentSavedAs;
            _uiControlledApp.ControlledApplication.DocumentSynchronizedWithCentral -= OnDocumentSynced;
            _uiControlledApp.ViewActivated -= OnViewActivated;
        }

        public void LogCustomButtonClick(string buttonName, Document document = null)
        {
            var evt = BuildEvent(TelemetryEventType.CustomButtonClicked, document);
            evt.CommandName = buttonName;
            evt.ExecutionMethod = "RibbonButton";
            _repository.Append(evt);
        }

        public void LogCommandExecuted(string commandName, string executionMethod, Document document = null)
        {
            var evt = BuildEvent(TelemetryEventType.CommandExecuted, document);
            evt.CommandName = commandName;
            evt.ExecutionMethod = executionMethod;
            _repository.Append(evt);
        }

        private void OnDocumentOpened(object sender, DocumentOpenedEventArgs e)
        {
            try
            {
                var evt = BuildEvent(TelemetryEventType.DocumentOpened, e.Document);
                _repository.Append(evt);
            }
            catch { }
        }

        private void OnDocumentClosed(object sender, DocumentClosedEventArgs e)
        {
            try
            {
                var evt = new TelemetryEventModel
                {
                    EventType = TelemetryEventType.DocumentClosed,
                    Notes = $"DocumentId: {e.DocumentId}"
                };
                _repository.Append(evt);
            }
            catch { }
        }

        private void OnDocumentSaved(object sender, DocumentSavedEventArgs e)
        {
            try
            {
                var evt = BuildEvent(TelemetryEventType.DocumentSaved, e.Document);
                _repository.Append(evt);
            }
            catch { }
        }

        private void OnDocumentSavedAs(object sender, DocumentSavedAsEventArgs e)
        {
            try
            {
                var evt = BuildEvent(TelemetryEventType.DocumentSaved, e.Document);
                evt.Notes = "SavedAs";
                _repository.Append(evt);
            }
            catch { }
        }

        private void OnDocumentSynced(object sender, DocumentSynchronizedWithCentralEventArgs e)
        {
            try
            {
                var evt = BuildEvent(TelemetryEventType.DocumentSynced, e.Document);
                _repository.Append(evt);
            }
            catch { }
        }

        private void OnViewActivated(object sender, ViewActivatedEventArgs e)
        {
            try
            {
                var evt = BuildEvent(TelemetryEventType.ViewActivated, e.Document);
                evt.CommandName = e.CurrentActiveView?.ViewType.ToString() ?? "Unknown";
                evt.Notes = e.CurrentActiveView?.Name ?? string.Empty;
                _repository.Append(evt);
            }
            catch { }
        }

        private TelemetryEventModel BuildEvent(TelemetryEventType eventType, Document document)
        {
            var evt = new TelemetryEventModel
            {
                EventType = eventType
            };

            if (document != null)
            {
                try
                {
                    evt.DocumentPath = document.PathName ?? string.Empty;
                    evt.ProjectName = document.Title ?? string.Empty;

                    if (document.IsWorkshared)
                    {
                        var modelPath = document.GetWorksharingCentralModelPath();
                        evt.CentralModelPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
                    }
                }
                catch { }
            }

            return evt;
        }

        public TelemetryRepository Repository => _repository;

        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
                _disposed = true;
            }
        }
    }
}
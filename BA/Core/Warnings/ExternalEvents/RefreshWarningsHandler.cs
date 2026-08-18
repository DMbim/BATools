// FILE: BA_Tools/Warnings/ExternalEvents/RefreshWarningsHandler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Warnings.Models;

namespace BA.Warnings.ExternalEvents
{
    public sealed class RefreshWarningsHandler : IExternalEventHandler
    {
        private static ExternalEvent _event;
        private Action<List<WarningItem>> _onCompleted;

        public static RefreshWarningsHandler Instance { get; } = new RefreshWarningsHandler();

        private RefreshWarningsHandler() { }

        public void RequestRefresh(Action<List<WarningItem>> onCompleted)
        {
            _onCompleted = onCompleted;
            _event ??= ExternalEvent.Create(this);
            _event.Raise();
        }

        public void Execute(UIApplication app)
        {
            var result = new List<WarningItem>();
            try
            {
                UIDocument uiDoc = app.ActiveUIDocument;
                if (uiDoc == null) return;

                Document doc = uiDoc.Document;
                IList<FailureMessage> warnings = doc.GetWarnings();

                foreach (FailureMessage w in warnings)
                {
                    result.Add(new WarningItem
                    {
                        Description = w.GetDescriptionText(),
                        Severity = w.GetSeverity(),
                        FailureDefinitionId = w.GetFailureDefinitionId(),
                        FailingElementIds = w.GetFailingElements()?.ToList() ?? new List<ElementId>(),
                        AdditionalElementIds = w.GetAdditionalElements()?.ToList() ?? new List<ElementId>(),
                        ResolutionCaption = SafeGetResolutionCaption(w)
                    });
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("RefreshWarningsHandler.Execute", ex);
            }
            finally
            {
                _onCompleted?.Invoke(result);
                _onCompleted = null;
            }
        }

        private static string SafeGetResolutionCaption(FailureMessage w)
        {
            try { return w.GetDefaultResolutionCaption(); }
            catch { return string.Empty; }
        }

        public string GetName() => "BA Refresh Warnings";
    }
}
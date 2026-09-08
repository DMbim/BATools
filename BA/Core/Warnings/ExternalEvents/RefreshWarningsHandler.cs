// FILE: BA_Tools/Warnings/ExternalEvents/RefreshWarningsHandler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.UI.ExternalEvents;
using BA.Warnings.Models;
using BA.Warnings.Services;

namespace BA.Warnings.ExternalEvents
{
    // Routes through the app-wide AppExternalInvoker singleton instead of
    // owning its own ExternalEvent. This was the one handler that happened to
    // work before, because its first-ever call ran inside the ViewModel
    // constructor, itself inside Cmd_OpenWarningsDashboard.Run, a valid API
    // context. Converted anyway for consistency: six different lifecycle
    // patterns for one window is its own maintenance problem even when they
    // all function.
    public static class RefreshWarningsHandler
    {
        public static void RequestRefresh(Action<List<WarningItem>> onCompleted)
        {
            AppExternalInvoker.Instance.Run(
                app => BuildWarningItems(app),
                onCompleted,
                ex => AppLogger.LogError("RefreshWarningsHandler.RequestRefresh", ex));
        }

        private static List<WarningItem> BuildWarningItems(UIApplication app)
        {
            var result = new List<WarningItem>();

            UIDocument uiDoc = app.ActiveUIDocument;
            if (uiDoc == null) return result;

            Document doc = uiDoc.Document;
            IList<FailureMessage> warnings = doc.GetWarnings();

            foreach (FailureMessage w in warnings)
            {
                var item = new WarningItem
                {
                    Description = w.GetDescriptionText(),
                    Severity = w.GetSeverity(),
                    FailureDefinitionId = w.GetFailureDefinitionId(),
                    FailingElementIds = w.GetFailingElements()?.ToList() ?? new List<ElementId>(),
                    AdditionalElementIds = w.GetAdditionalElements()?.ToList() ?? new List<ElementId>(),
                    ResolutionCaption = SafeGetResolutionCaption(w)
                };

                if (FailureClassificationService.Instance.TryGetClassification(
                        item.FailureDefinitionId.Guid, item.Description, out FailureClassificationEntry classification))
                {
                    item.ClassifiedSeverity = classification.Severity;
                    item.Category = classification.Category;
                }

                result.Add(item);
            }

            return result;
        }

        private static string SafeGetResolutionCaption(FailureMessage w)
        {
            try { return w.GetDefaultResolutionCaption(); }
            catch { return string.Empty; }
        }
    }
}
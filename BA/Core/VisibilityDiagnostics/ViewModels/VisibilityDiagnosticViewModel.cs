// File: BA_Tools/VisibilityDiagnostics/ViewModels/VisibilityDiagnosticViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BA.UI.ExternalEvents;
using BA.UI.Mvvm;
using BA.VisibilityDiagnostics.Models;
using BA.VisibilityDiagnostics.Services;

namespace BA.VisibilityDiagnostics.ViewModels
{
    /// <summary>
    /// ViewModel for the modeless Visibility Diagnostic window. Never touches the
    /// Revit API directly (per project rule A3) - every Revit-side operation is
    /// routed through BA.UI.ExternalEvents.AppExternalInvoker, which raises an
    /// ExternalEvent and marshals the result back to this (WPF UI) thread.
    /// </summary>
    public sealed class VisibilityDiagnosticViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _elementIdInput = string.Empty;
        private string _statusMessage = "Enter an Element Id / Unique Id, or use Pick / Use Current Selection.";
        private string _elementSummary = string.Empty;
        private bool _isBusy;
        private bool? _visibleToViewCollector;

        public VisibilityDiagnosticViewModel()
        {
            DiagnoseCommand = new BA.UI.Mvvm.AsyncRelayCommand(ExecuteDiagnoseAsync, () => !IsBusy);
            PickElementCommand = new BA.UI.Mvvm.AsyncRelayCommand(ExecutePickAsync, () => !IsBusy);
            UseCurrentSelectionCommand = new BA.UI.Mvvm.AsyncRelayCommand(ExecuteUseSelectionAsync, () => !IsBusy);
        }

        public string ElementIdInput
        {
            get => _elementIdInput;
            set => SetProperty(ref _elementIdInput, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public string ElementSummary
        {
            get => _elementSummary;
            private set => SetProperty(ref _elementSummary, value);
        }

        /// <summary>
        /// Ground-truth result for the last diagnosis: whether a view-scoped
        /// FilteredElementCollector would include this element (see
        /// VisibilityDiagnosticReport.VisibleToViewCollector for exactly what this
        /// does and does not cover). Null before the first run.
        /// </summary>
        public bool? VisibleToViewCollector
        {
            get => _visibleToViewCollector;
            private set => SetProperty(ref _visibleToViewCollector, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    DiagnoseCommand.RaiseCanExecuteChanged();
                    PickElementCommand.RaiseCanExecuteChanged();
                    UseCurrentSelectionCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<VisibilityCheckItem> CheckResults { get; } = new();

        public BA.UI.Mvvm.AsyncRelayCommand DiagnoseCommand { get; }
        public BA.UI.Mvvm.AsyncRelayCommand PickElementCommand { get; }
        public BA.UI.Mvvm.AsyncRelayCommand UseCurrentSelectionCommand { get; }

        /// <summary>
        /// Called once by VisibilityDiagnosticCommand immediately after construction,
        /// synchronously from within Execute() (a valid Revit API context) - safe to
        /// call directly without an ExternalEvent because it only reads the
        /// already-captured selection ElementId, no live API access here.
        /// </summary>
        public void PrefillFromInitialSelection(ElementId? singleSelectedElementId)
        {
            if (singleSelectedElementId is not null && singleSelectedElementId != ElementId.InvalidElementId)
            {
                ElementIdInput = singleSelectedElementId.Value.ToString();
                StatusMessage = "Prefilled from current selection. Click Diagnose, or edit the Id first.";
            }
        }

        private async Task ExecuteDiagnoseAsync()
        {
            IsBusy = true;
            StatusMessage = "Running diagnostics...";
            try
            {
                string input = ElementIdInput?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(input))
                {
                    StatusMessage = "Enter an Element Id or Unique Id, or use Pick / Use Current Selection.";
                    return;
                }

                VisibilityDiagnosticReport report = await RunOnRevitAsync(uiApp =>
                {
                    UIDocument uiDoc = uiApp.ActiveUIDocument ?? throw new InvalidOperationException("No active document.");
                    Document doc = uiDoc.Document;
                    View view = doc.ActiveView ?? throw new InvalidOperationException("No active view.");

                    if (!ElementReferenceResolver.TryResolve(doc, input, out Element? element, out string? resolveError) || element is null)
                    {
                        throw new InvalidOperationException(resolveError ?? "Element could not be resolved.");
                    }

                    return VisibilityDiagnosticService.Instance.Diagnose(doc, view, element);
                }).ConfigureAwait(true);

                ApplyReport(report);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                CheckResults.Clear();
                ElementSummary = string.Empty;
                VisibleToViewCollector = null;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExecutePickAsync()
        {
            IsBusy = true;
            StatusMessage = "Pick an element in the model (Esc to cancel)...";
            try
            {
                // Pick returns either a plain host-document element, or (when the user
                // clicks geometry belonging to a Revit Link) a Reference whose
                // LinkedElementId identifies the element INSIDE the link's own
                // document, with ElementId identifying the RevitLinkInstance in the
                // host document. Route accordingly - see
                // VisibilityDiagnosticService.DiagnoseLinkedElement for why linked
                // elements take a separate, narrower path instead of the full checklist.
                PickResult pick = await RunOnRevitAsync(uiApp =>
                {
                    UIDocument uiDoc = uiApp.ActiveUIDocument ?? throw new InvalidOperationException("No active document.");
                    Document doc = uiDoc.Document;
                    Reference reference = uiDoc.Selection.PickObject(ObjectType.Element, "Select an element to diagnose");

                    if (reference.LinkedElementId != ElementId.InvalidElementId
                        && doc.GetElement(reference.ElementId) is RevitLinkInstance linkInstance)
                    {
                        Document? linkedDoc = linkInstance.GetLinkDocument();
                        if (linkedDoc is null)
                        {
                            throw new InvalidOperationException(
                                $"Link '{linkInstance.Name}' is not currently loaded, so its element cannot be inspected.");
                        }

                        Element? linkedElement = linkedDoc.GetElement(reference.LinkedElementId);
                        if (linkedElement is null)
                        {
                            throw new InvalidOperationException("Could not resolve the picked element inside the link.");
                        }

                        View view = doc.ActiveView ?? throw new InvalidOperationException("No active view.");
                        VisibilityDiagnosticReport linkedReport = VisibilityDiagnosticService.Instance.DiagnoseLinkedElement(
                            doc, view, linkInstance, linkedDoc, linkedElement);
                        return PickResult.ForLink(linkedReport);
                    }

                    return PickResult.ForHost(reference.ElementId.Value);
                }).ConfigureAwait(true);

                if (pick.LinkedReport is not null)
                {
                    ElementIdInput = $"(linked) {pick.LinkedReport.ElementId}";
                    ApplyReport(pick.LinkedReport);
                }
                else
                {
                    ElementIdInput = pick.HostElementId.ToString();
                    await ExecuteDiagnoseAsync().ConfigureAwait(true);
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                StatusMessage = "Pick cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Small carrier so RunOnRevitAsync's single generic return value can be either outcome of a pick.</summary>
        private sealed class PickResult
        {
            public long HostElementId { get; private init; }
            public VisibilityDiagnosticReport? LinkedReport { get; private init; }

            public static PickResult ForHost(long elementId) => new() { HostElementId = elementId };
            public static PickResult ForLink(VisibilityDiagnosticReport report) => new() { LinkedReport = report };
        }

        private async Task ExecuteUseSelectionAsync()
        {
            IsBusy = true;
            StatusMessage = "Reading current selection...";
            try
            {
                (long id, int count) result = await RunOnRevitAsync(uiApp =>
                {
                    UIDocument uiDoc = uiApp.ActiveUIDocument ?? throw new InvalidOperationException("No active document.");
                    ICollection<ElementId> ids = uiDoc.Selection.GetElementIds();
                    if (ids.Count == 0)
                    {
                        throw new InvalidOperationException("Nothing is currently selected in the model.");
                    }

                    return (ids.First().Value, ids.Count);
                }).ConfigureAwait(true);

                if (result.count > 1)
                {
                    StatusMessage = $"{result.count} elements selected - diagnosing the first (Id {result.id}). " +
                        "Select exactly one element for a specific result.";
                }

                ElementIdInput = result.id.ToString();
                await ExecuteDiagnoseAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ApplyReport(VisibilityDiagnosticReport report)
        {
            CheckResults.Clear();
            foreach (VisibilityCheckItem item in report.Checks)
            {
                CheckResults.Add(item);
            }

            string displayName = string.IsNullOrEmpty(report.ElementName) ? "(unnamed)" : report.ElementName;
            ElementSummary = $"Id {report.ElementId} | {report.ElementCategoryName} | {report.ElementTypeName} | " +
                $"{displayName} | View: {report.ViewName} ({report.ViewType})";

            VisibleToViewCollector = report.VisibleToViewCollector;

            StatusMessage = report.UnexplainedHidden
                ? "Revit's own view collector reports this element as NOT visible, but none of the implemented checks " +
                  "explain why. Remaining possibilities: a View-Template-locked setting (see the View Template row " +
                  "above), a Detail Level restriction defined inside the family (not reliably readable via the public " +
                  "API - see the Detail Level row), or a geometric edge case in the View Range / Crop Region " +
                  "bounding-box approximations."
                : "Diagnosis complete.";
        }

        /// <summary>
        /// Bridges the callback-based AppExternalInvoker.Run into an awaitable Task,
        /// so ExternalEvent-marshaled Revit API calls compose naturally with
        /// AsyncRelayCommand. apiFunc runs inside RevitActionQueueHandler.Execute -
        /// i.e. on Revit's own API thread, inside a valid API context.
        /// </summary>
        private static Task<T> RunOnRevitAsync<T>(Func<UIApplication, T> apiFunc)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            AppExternalInvoker.Instance.Run(
                apiFunc,
                onCompleted: result => tcs.TrySetResult(result),
                onError: ex => tcs.TrySetException(ex));
            return tcs.Task;
        }

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}

// File: BA/ViewModels/CurveToElement/CurveToElementWindowViewModel.cs
// Action: CREATE NEW

using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.Core.CurveToElement.Infrastructure;
using BA.Core.CurveToElement.Models;
using BA.Core.CurveToElement.Services;
using BA.Core.Mvvm;
using BA.UI.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace BA.ViewModels.CurveToElement
{
    /// <summary>
    /// Container ViewModel for the Curve-to-Element (detail line -> wall) settings window.
    /// Owns the per-group ViewModels, routes async preview results from
    /// WallFaceOffsetPreviewHandler back to the correct group, aggregates validation across
    /// all groups, and hands off a fully-validated generation payload via RequestGenerate.
    ///
    /// Deliberately does NOT call any Revit API method that writes to the document, and does
    /// not open a transaction itself - RequestGenerate is a caller-supplied delegate, wired by
    /// the command/window that owns this instance, exactly as RequestClose/BrowseForFilePath
    /// are caller-supplied hooks in LedgerSettingsViewModel. This keeps the actual Wall.Create
    /// transaction logic (WallGenerationService, not yet built) fully decoupled from this class.
    /// </summary>
    public class CurveToElementWindowViewModel : BA.UI.Mvvm.ObservableObject, IDisposable
    {
        private readonly WallFaceOffsetPreviewHandler _previewHandler;
        private bool _isGenerating;
        private string _statusMessage = string.Empty;
        private bool _isDisposed;

        public CurveToElementWindowViewModel(
            IReadOnlyList<CurveTypeGroup> classifiedGroups,
            ObservableCollection<WallTypeOption> availableWallTypes,
            ObservableCollection<LevelOption> availableLevels,
            Units documentUnits,
            WallFaceOffsetPreviewHandler previewHandler)
        {
            if (classifiedGroups == null) throw new ArgumentNullException(nameof(classifiedGroups));
            AvailableWallTypes = availableWallTypes ?? throw new ArgumentNullException(nameof(availableWallTypes));
            AvailableLevels = availableLevels ?? throw new ArgumentNullException(nameof(availableLevels));
            _previewHandler = previewHandler ?? throw new ArgumentNullException(nameof(previewHandler));

            if (documentUnits == null) throw new ArgumentNullException(nameof(documentUnits));

            var chainBuilder = new CurveChainBuilder();
            Groups = new ObservableCollection<CurveTypeGroupViewModel>();

            foreach (CurveTypeGroup group in classifiedGroups)
            {
                List<CurveChain> chains = chainBuilder.BuildChains(group.Curves);

                var groupViewModel = new CurveTypeGroupViewModel(
                    group,
                    chains,
                    AvailableWallTypes,
                    AvailableLevels,
                    documentUnits,
                    RequestPreviewForGroup);

                Groups.Add(groupViewModel);
            }

            _previewHandler.ResultReady += OnPreviewResultReady;

            GenerateCommand = new BA.Core.Mvvm.RelayCommand(ExecuteGenerate, CanExecuteGenerate);
            CancelCommand = new BA.Core.Mvvm.RelayCommand(ExecuteCancel);
        }

        /// <summary>
        /// Window code-behind sets this to close itself, matching LedgerSettingsViewModel's
        /// RequestClose convention exactly.
        /// </summary>
        public Action<bool?> RequestClose { get; set; }

        /// <summary>
        /// Caller-supplied hook that actually performs generation (expected to go through
        /// RevitExternalInvoker/ExternalEvent, per project convention - not implemented here).
        /// The callback receives the outcome once Revit-side work completes.
        /// </summary>
        public Action<IReadOnlyList<GroupGenerationRequest>, Action<GenerationResult>> RequestGenerate { get; set; }

        public ObservableCollection<CurveTypeGroupViewModel> Groups { get; }
        public ObservableCollection<WallTypeOption> AvailableWallTypes { get; }
        public ObservableCollection<LevelOption> AvailableLevels { get; }

        public bool IsGenerating
        {
            get => _isGenerating;
            private set => SetProperty(ref _isGenerating, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public ICommand GenerateCommand { get; }
        public ICommand CancelCommand { get; }

        private void RequestPreviewForGroup(Guid groupId, ElementId wallTypeId)
        {
            _previewHandler.RequestPreview(groupId, wallTypeId);
        }

        private void OnPreviewResultReady(WallPreviewResult result)
        {
            if (result == null)
                return;

            // WallFaceOffsetPreviewHandler.Execute always runs on Revit's main thread, the same
            // thread the window's WPF dispatcher pumps on for a properly modeless/modal-owned
            // window - so this is a direct property update, not a Dispatcher.Invoke marshal.
            // If a future window hosting change makes that assumption false, this is the first
            // place a cross-thread InvalidOperationException on PropertyChanged would surface.
            CurveTypeGroupViewModel target = Groups.FirstOrDefault(g => g.GroupId == result.GroupId);
            target?.ApplyPreviewResult(result);
        }

        private bool CanExecuteGenerate()
        {
            return Groups.Count > 0 && !IsGenerating;
        }

        private void ExecuteGenerate()
        {
            AppLogger.LogInfo("CurveToElementWindowViewModel.ExecuteGenerate: command invoked.");

            if (RequestGenerate == null)
            {
                AppLogger.LogInfo("CurveToElementWindowViewModel.ExecuteGenerate: RequestGenerate delegate is null, window never wired it.");
                return;
            }

            var requests = new List<GroupGenerationRequest>();
            var validationErrors = new List<string>();

            foreach (CurveTypeGroupViewModel groupViewModel in Groups)
            {
                if (groupViewModel.TryBuildSettings(out WallGroupSettings settings, out string validationError))
                {
                    requests.Add(new GroupGenerationRequest(groupViewModel.Group, groupViewModel.Chains, settings));
                }
                else
                {
                    validationErrors.Add(validationError);
                }
            }

            if (validationErrors.Count > 0)
            {
                var messageBuilder = new StringBuilder();
                messageBuilder.AppendLine("Please fix the following before generating:");
                messageBuilder.AppendLine();
                foreach (string error in validationErrors)
                    messageBuilder.AppendLine($"- {error}");

                MessageBox.Show(
                    messageBuilder.ToString(),
                    "Curve to Element",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            IsGenerating = true;
            StatusMessage = "Generating walls...";

            RequestGenerate(requests, result =>
            {
                IsGenerating = false;

                if (result == null)
                {
                    StatusMessage = string.Empty;
                    AppLogger.LogInfo("CurveToElementWindowViewModel.ExecuteGenerate: RequestGenerate callback returned null result.");
                    return;
                }

                StatusMessage = result.Success
                    ? $"Created {result.CreatedWallCount} wall(s)."
                    : string.Empty;

                if (result.Success)
                {
                    string summary = result.Warnings.Count > 0
                        ? $"Created {result.CreatedWallCount} wall(s) with {result.Warnings.Count} warning(s):\n\n" + string.Join("\n", result.Warnings)
                        : $"Created {result.CreatedWallCount} wall(s).";

                    MessageBox.Show(
                        summary,
                        "Curve to Element",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    RequestClose?.Invoke(true);
                }
                else
                {
                    MessageBox.Show(
                        result.Message ?? "Wall generation failed. Check the log for details.",
                        "Curve to Element",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            });
        }

        private void ExecuteCancel()
        {
            AppLogger.LogInfo("CurveToElementWindowViewModel.ExecuteCancel: command invoked.");
            RequestClose?.Invoke(false);
        }

        /// <summary>
        /// Must be called by the window's Closed handler. Unsubscribes from
        /// WallFaceOffsetPreviewHandler.ResultReady - without this, every window open/close
        /// cycle leaks a subscription against a handler instance that likely outlives the
        /// window itself, and stale preview results would keep attempting to route into
        /// disposed-of group ViewModels.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            _previewHandler.ResultReady -= OnPreviewResultReady;
            _isDisposed = true;
        }
    }
}
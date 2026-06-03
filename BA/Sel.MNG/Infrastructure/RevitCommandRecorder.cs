using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using BATools.SelectionManager.Services;

namespace BATools.SelectionManager.Infrastructure
{
    /// <summary>
    /// Registers AddInCommandBindings for every available PostableCommand
    /// and records executions to RecentActionsService.
    /// Must be initialized once after UIApplication is available.
    /// </summary>
    public sealed class RevitCommandRecorder
    {
        private static readonly RevitCommandRecorder _instance = new();
        public static RevitCommandRecorder Instance => _instance;

        private UIApplication? _uiApp;
        private bool _initialized;

        // Keyed by PostableCommand so Unregister can remove by RevitCommandId.
        private readonly Dictionary<PostableCommand, (RevitCommandId Id, AddInCommandBinding Binding)>
            _registrations = new();

        private RevitCommandRecorder() { }

        /// <summary>
        /// Call once from OnFirstViewActivated. No-op after first call.
        /// </summary>
        public void Initialize(UIApplication uiApp)
        {
            if (_initialized) return;
            _initialized = true;
            _uiApp = uiApp;

            RegisterAllBindings();
        }

        private void RegisterAllBindings()
        {
            if (_uiApp == null) return;

            int registered = 0;
            int skipped = 0;

            foreach (PostableCommand postableCommand in Enum.GetValues(typeof(PostableCommand)))
            {
                try
                {
                    RevitCommandId commandId =
                        RevitCommandId.LookupPostableCommandId(postableCommand);

                    if (commandId == null) { skipped++; continue; }

                    AddInCommandBinding binding =
                        _uiApp.CreateAddInCommandBinding(commandId);

                    PostableCommand captured = postableCommand;

                    binding.BeforeExecuted += (_, args) =>
                        OnBeforeExecuted(captured, args);

                    _registrations[postableCommand] = (commandId, binding);
                    registered++;
                }
                catch (Exception ex)
                {
                    skipped++;
                    Debug.WriteLine(
                        $"[RevitCommandRecorder] Skip {postableCommand}: {ex.Message}");
                }
            }

            Debug.WriteLine(
                $"[RevitCommandRecorder] Registered {registered}, skipped {skipped}.");
        }

        private static void OnBeforeExecuted(
            PostableCommand command,
            BeforeExecutedEventArgs args)
        {
            try
            {
                string actionId = RevitCommandCatalog.ActionIdFor(command); // <- CHANGED
                RecentActionsService.Record(actionId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[RevitCommandRecorder.OnBeforeExecuted] {command}: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes all registered bindings. Call from SelectionManagerActivator.Dispose().
        /// </summary>
        public void Unregister()
        {
            if (_uiApp == null) return;

            foreach (var (postableCommand, (commandId, _)) in _registrations)
            {
                try
                {
                    _uiApp.RemoveAddInCommandBinding(commandId);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"[RevitCommandRecorder.Unregister] {postableCommand}: {ex.Message}");
                }
            }

            _registrations.Clear();
            _initialized = false;
            _uiApp = null;
        }
    }
}
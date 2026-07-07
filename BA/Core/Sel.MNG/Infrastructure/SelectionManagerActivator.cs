using System;
using System.Runtime.InteropServices;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using BATools.SelectionManager.Models;
using BATools.SelectionManager.Panes;
using BATools.SelectionManager.Services;
using BATools.SelectionManager.ViewModels;
using BATools.SelectionManager.Views;

namespace BATools.SelectionManager.Infrastructure
{
    public sealed class SelectionManagerActivator : IDisposable
    {
        private static readonly SelectionManagerActivator _instance = new();
        public static SelectionManagerActivator Instance => _instance;

        // ── Keyboard hook ─────────────────────────────────────────────────────
        private GlobalKeyboardHook? _hook;
        private DoubleKeyDetector? _toggleDetector;
        private KeyHoldDetector? _freezeDetector;

        // ── Windows ───────────────────────────────────────────────────────────
        private QuickToolbarWindow? _toolbarWindow;
        private QuickToolbarViewModel? _toolbarViewModel;
        private RecentToolbarWindow? _recentWindow;
        private RecentToolbarViewModel? _recentViewModel;

        // ── Infrastructure ────────────────────────────────────────────────────
        private SelectionManagerDockablePane? _dockablePane;
        private UIControlledApplication? _controlledApp;
        private bool _initialized;
        private bool _disposed;

        private SelectionManagerActivator() { }

        public SelectionManagerDockablePane? DockablePane => _dockablePane;

        // ═════════════════════════════════════════════════════════════════════
        // INITIALIZATION
        // ═════════════════════════════════════════════════════════════════════

        public void Initialize(UIControlledApplication controlledApp)
        {
            if (_initialized) return;
            _initialized = true;
            _controlledApp = controlledApp;

            // ── 1. Keyboard hook — no Revit API, must be first ────────────────
            try
            {
                ApplySettings(HotkeySettingsService.Load());

                _hook = new GlobalKeyboardHook();
                _hook.KeyDown += OnHookKeyDown;
                _hook.KeyUp += OnHookKeyUp;
                _hook.Install();

                System.Diagnostics.Debug.WriteLine(
                    "[SelectionManagerActivator] Keyboard hook installed.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SelectionManagerActivator] Hook install FAILED: {ex}");
            }

            // ── 2. Dockable pane ──────────────────────────────────────────────
            try
            {
                _dockablePane = new SelectionManagerDockablePane();
                controlledApp.RegisterDockablePane(
                    SelectionManagerDockablePane.PaneId,
                    "BA Selection Manager",
                    _dockablePane);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SelectionManagerActivator] RegisterDockablePane FAILED: {ex.Message}");
            }

            // ── 3. Document events ────────────────────────────────────────────
            try
            {
                controlledApp.ControlledApplication.DocumentOpened += OnDocumentOpened;
                controlledApp.ControlledApplication.DocumentClosed += OnDocumentClosed;
                controlledApp.ControlledApplication.DocumentChanged += OnDocumentChanged;
                controlledApp.ApplicationClosing += OnApplicationClosing;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SelectionManagerActivator] Event wiring FAILED: {ex.Message}");
            }

            // ── 4. Bridge init via one-shot ViewActivated ─────────────────────
            try
            {
                controlledApp.ViewActivated += OnFirstViewActivated;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SelectionManagerActivator] ViewActivated wiring FAILED: {ex.Message}");
            }
        }

        // ── One-shot: capture UIApplication for ExternalEvent creation ────────
        // In OnFirstViewActivated — ADD one line after SelectionManagerBridge.Instance.Initialize(uiApp)
        private void OnFirstViewActivated(object sender, ViewActivatedEventArgs e)
        {
            try
            {
                if (_controlledApp != null)
                    _controlledApp.ViewActivated -= OnFirstViewActivated;

                if (sender is UIApplication uiApp)
                {
                    SelectionManagerBridge.Instance.Initialize(uiApp);
                    RevitCommandRecorder.Instance.Initialize(uiApp); // <- NEW

                    System.Diagnostics.Debug.WriteLine(
                        "[SelectionManagerActivator] Bridge and recorder initialized.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[OnFirstViewActivated] {ex.Message}");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // SETTINGS
        // ═════════════════════════════════════════════════════════════════════

        public void ReloadHotkeySettings()
        {
            DisposeDetectors();
            var settings = HotkeySettingsService.Load();
            ApplySettings(settings);

            var width = settings.ToolbarWidth;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                new Action(() => _toolbarWindow?.ApplyWidth(width)));
        }

        private void ApplySettings(HotkeySettings settings)
        {
            try
            {
                _toggleDetector = new DoubleKeyDetector(
                    HotkeySettingsService.GetTogglePredicate(settings.Toggle),
                    settings.ToggleWindowMs);
                _toggleDetector.DoubleKeyDetected += OnToggleGesture;

                var freezePredicate =
                    HotkeySettingsService.GetFreezePredicate(settings.Freeze);
                if (freezePredicate != null)
                {
                    _freezeDetector = new KeyHoldDetector(
                        freezePredicate, settings.FreezeHoldMs);
                    _freezeDetector.KeyHeld += OnFreezeGesture;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SelectionManagerActivator.ApplySettings] {ex}");
            }
        }

        private void DisposeDetectors()
        {
            if (_toggleDetector != null)
            {
                _toggleDetector.DoubleKeyDetected -= OnToggleGesture;
                _toggleDetector = null;
            }
            if (_freezeDetector != null)
            {
                _freezeDetector.KeyHeld -= OnFreezeGesture;
                _freezeDetector.Cancel();
                _freezeDetector = null;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // KEYBOARD HOOK FORWARDING
        // ═════════════════════════════════════════════════════════════════════

        private void OnHookKeyDown(uint vk)
        {
            try
            {
                _toggleDetector?.OnKeyDown(vk);
                _freezeDetector?.OnKeyDown(vk);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OnHookKeyDown] {ex.Message}");
            }
        }

        private void OnHookKeyUp(uint vk)
        {
            try
            {
                _toggleDetector?.OnKeyUp(vk);
                _freezeDetector?.OnKeyUp(vk);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OnHookKeyUp] {ex.Message}");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // GESTURE HANDLERS
        // ═════════════════════════════════════════════════════════════════════

        private void OnToggleGesture()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    _freezeDetector?.Cancel();
                    EnsureToolbarCreated();

                    if (_toolbarViewModel == null) return;

                    if (_toolbarViewModel.IsVisible)
                    {
                        _toolbarViewModel.IsFrozen = false;
                        _toolbarViewModel.Deactivate();
                    }
                    else
                    {
                        _toolbarViewModel.Activate(); // <- THIS LINE opens the toolbar
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OnToggleGesture] {ex}");
                }
            }));
        }

        private void OnFreezeGesture()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_toolbarViewModel == null || !_toolbarViewModel.IsVisible)
                        return;
                    _toolbarViewModel.IsFrozen = !_toolbarViewModel.IsFrozen;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OnFreezeGesture] {ex.Message}");
                }
            }));
        }

        // ═════════════════════════════════════════════════════════════════════
        // WINDOW CREATION (LAZY)
        // ═════════════════════════════════════════════════════════════════════

        private void EnsureToolbarCreated()
        {
            if (_toolbarWindow != null) return;
            try
            {
                EnsureRecentWindowCreated();

                _toolbarViewModel = new QuickToolbarViewModel(ShowSaveDialog);
                _toolbarWindow = new QuickToolbarWindow(
                    _toolbarViewModel, _recentViewModel!);

                var savedWidth = HotkeySettingsService.Load().ToolbarWidth;
                _toolbarWindow.ApplyWidth(savedWidth);

                System.Diagnostics.Debug.WriteLine(
                    "[SelectionManagerActivator] Toolbar window created.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[EnsureToolbarCreated] {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine(ex.StackTrace);
                _toolbarWindow = null;
                _toolbarViewModel = null;
            }
        }

        private void EnsureRecentWindowCreated()
        {
            if (_recentWindow != null) return;
            try
            {
                var actions = Actions.QuickActionRegistry.CreateDefault(ShowSaveDialog);
                _recentViewModel = new RecentToolbarViewModel(actions);
                _recentWindow = new RecentToolbarWindow(_recentViewModel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[EnsureRecentWindowCreated] {ex.Message}");
                _recentWindow = null;
                _recentViewModel = null;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // PUBLIC API — called by OpenRecentsCommand
        // ═════════════════════════════════════════════════════════════════════

        public void OpenRecents()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    EnsureRecentWindowCreated();
                    if (_recentViewModel == null || _recentWindow == null) return;

                    _recentViewModel.RefreshAndShow();

                    if (GetCursorPos(out ACTIVATOR_POINT pt))
                        _recentWindow.ShowAt(           // <- THIS opens the recents window
                            new System.Windows.Point(pt.X, pt.Y));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[SelectionManagerActivator.OpenRecents] {ex.Message}");
                }
            }));
        }

        // ═════════════════════════════════════════════════════════════════════
        // DOCUMENT EVENTS
        // ═════════════════════════════════════════════════════════════════════

        private void OnDocumentOpened(object? sender, DocumentOpenedEventArgs e)
        {
            try
            {
                var doc = e.Document;
                if (doc == null) return;
                string fp = DocumentFingerprint.Compute(doc.PathName, doc.Title);
                SetRepository.Instance.LoadForDocument(fp);
                ElementIdResolver.Instance.InvalidateCache();
                RecentActionsService.Reset(); // <- NEW

                System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                    new Action(() => _dockablePane?.LoadForDocument(fp)));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OnDocumentOpened] {ex.Message}");
            }
        }

        private void OnDocumentClosed(object? sender, DocumentClosedEventArgs e)
        {
            try
            {
                SetRepository.Instance.FlushAll();
                ElementIdResolver.Instance.InvalidateCache();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OnDocumentClosed] {ex.Message}");
            }
        }

        private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
        {
            try { SetHealthMonitor.Instance.OnDocumentChanged(sender, e); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OnDocumentChanged] {ex.Message}");
            }
        }

        private void OnApplicationClosing(object? sender, ApplicationClosingEventArgs e)
            => Dispose();

        // ═════════════════════════════════════════════════════════════════════
        // DIALOGS
        // ═════════════════════════════════════════════════════════════════════

        private void ShowSaveDialog()
        {
            try
            {
                _toolbarViewModel?.Deactivate();
                var dialog = new SaveSetDialog();
                if (dialog.ShowDialog() != true) return;
                string name = dialog.SetName;
                if (string.IsNullOrWhiteSpace(name)) return;
                SelectionManagerBridge.Instance.RequestSaveCurrentSelection(
                    name, newSet => _dockablePane?.ViewModel?.RebuildList());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShowSaveDialog] {ex.Message}");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // WIN32
        // ═════════════════════════════════════════════════════════════════════

        [StructLayout(LayoutKind.Sequential)]
        private struct ACTIVATOR_POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out ACTIVATOR_POINT pt);

        // ═════════════════════════════════════════════════════════════════════
        // DISPOSAL
        // ═════════════════════════════════════════════════════════════════════

        // In Dispose() — ADD one line before _hook disposal
        public void Dispose()
        {
            if (_disposed) return;
            try
            {
                DisposeDetectors();

                RevitCommandRecorder.Instance.Unregister(); // <- NEW

                if (_hook != null)
                {
                    _hook.KeyDown -= OnHookKeyDown;
                    _hook.KeyUp -= OnHookKeyUp;
                    _hook.Uninstall();
                    _hook.Dispose();
                }

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    _toolbarWindow?.Close();
                    _recentWindow?.Close();
                });

                SetRepository.Instance.FlushAll();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SelectionManagerActivator.Dispose] {ex.Message}");
            }
            _disposed = true;
        }
    }
}
using BATools.SelectionManager.Infrastructure;
using BATools.SelectionManager.ViewModels;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Point = System.Windows.Point;
namespace BATools.SelectionManager.Views
{
    public partial class QuickToolbarWindow : Window
    {
        // ── Win32 structs ─────────────────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        // ── Win32 messages ────────────────────────────────────────────────────
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_NCRBUTTONDOWN = 0x00A4;
        private const int WM_NCRBUTTONUP = 0x00A5;
        private const int WM_CONTEXTMENU = 0x007B;

        // WM_NCHITTEST return values
        private const int HTCLIENT = 1;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private const double ResizeBorder = 6.0;

        // ── Win32 imports ─────────────────────────────────────────────────────
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);


        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;


        // ── Fields ────────────────────────────────────────────────────────────
        private readonly CursorTracker _tracker;
        private readonly QuickToolbarViewModel _viewModel;
        private readonly RecentToolbarViewModel _recentViewModel;
        private RecentToolbarWindow? _recentWindow;
        private HwndSource? _hwndSource;

        // ── Constructor ───────────────────────────────────────────────────────
        public QuickToolbarWindow(
            QuickToolbarViewModel viewModel,
            RecentToolbarViewModel recentViewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _recentViewModel = recentViewModel;
            DataContext = viewModel;

            MinWidth = 200;
            MinHeight = 120;
            MaxWidth = 520;

            _tracker = new Infrastructure.CursorTracker();
            _tracker.PositionChanged += OnCursorMoved;

            viewModel.PropertyChanged += (_, args) =>
            {
                switch (args.PropertyName)
                {
                    case nameof(QuickToolbarViewModel.IsVisible):
                        if (viewModel.IsVisible) ShowAtCursor();
                        else HideToolbar();
                        break;

                    case nameof(QuickToolbarViewModel.IsFrozen):
                        if (viewModel.IsFrozen) _tracker.Pause();
                        else _tracker.Resume();
                        break;
                }
            };

            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    viewModel.DismissCommand.Execute(null);
                    e.Handled = true;
                }
            };

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);

            _hwndSource = HwndSource.FromHwnd(helper.Handle);
            _hwndSource?.AddHook(WndProc);

            // Lazy-create recents window on same thread as this window
            _recentWindow = new RecentToolbarWindow(_recentViewModel);
        }

        // ── WndProc ───────────────────────────────────────────────────────────
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam,
                               IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_NCHITTEST:
                    handled = true;
                    return (IntPtr)GetHitTestValue(lParam);

                case WM_RBUTTONDOWN:
                    handled = true;
                    return IntPtr.Zero;

                case WM_RBUTTONUP:
                case WM_NCRBUTTONDOWN:
                case WM_NCRBUTTONUP:
                case WM_CONTEXTMENU:
                    handled = true;
                    return IntPtr.Zero;
            }

            return IntPtr.Zero;
        }

        // ── Recents popup ─────────────────────────────────────────────────────
        private void OpenRecents()
        {
            try
            {
                _recentViewModel.RefreshAndShow();
                if (!_recentViewModel.IsVisible) return; // empty recents

                GetCursorPos(out POINT pt);
                _recentWindow?.ShowAt(new Point(pt.X, pt.Y));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OpenRecents] {ex.Message}");
            }
        }

        // ── Hit test ──────────────────────────────────────────────────────────
        private int GetHitTestValue(IntPtr lParam)
        {
            int screenX = (short)(lParam.ToInt32() & 0xFFFF);
            int screenY = (short)((lParam.ToInt32() >> 16) & 0xFFFF);

            var dpi = VisualTreeHelper.GetDpi(this);
            double left = Left * dpi.DpiScaleX;
            double top = Top * dpi.DpiScaleY;
            double right = left + ActualWidth * dpi.DpiScaleX;
            double bottom = top + ActualHeight * dpi.DpiScaleY;
            double bx = ResizeBorder * dpi.DpiScaleX;
            double by = ResizeBorder * dpi.DpiScaleY;

            bool onLeft = screenX < left + bx;
            bool onRight = screenX > right - bx;
            bool onTop = screenY < top + by;
            bool onBottom = screenY > bottom - by;

            if (onTop && onLeft) return HTTOPLEFT;
            if (onTop && onRight) return HTTOPRIGHT;
            if (onBottom && onLeft) return HTBOTTOMLEFT;
            if (onBottom && onRight) return HTBOTTOMRIGHT;
            if (onLeft) return HTLEFT;
            if (onRight) return HTRIGHT;
            if (onTop) return HTTOP;
            if (onBottom) return HTBOTTOM;

            return HTCLIENT;
        }

        // ── Positioning ───────────────────────────────────────────────────────
        private void ShowAtCursor()
        {
            GetCursorPos(out POINT pt);
            RECT work = GetMonitorWorkingArea(pt);

            double spawnX = pt.X + 16;
            double spawnY = pt.Y - 60;

            if (spawnX + Width > work.Right)
                spawnX = pt.X - Width - 16;

            spawnY = Math.Max(work.Top, Math.Min(spawnY, work.Bottom - 120));

            Left = spawnX;
            Top = spawnY;
            Show();

            _tracker.Start();
        }

        private void HideToolbar()
        {
            _tracker.Stop();
            _viewModel.IsFrozen = false;
            _recentWindow?.Hide();
            Hide();
        }

        private void OnCursorMoved(Point cursorPos)
        {
            Left = cursorPos.X + 16;
            Top = cursorPos.Y - (ActualHeight / 2);
        }

        public void ApplyWidth(int width)
        {
            Width = Math.Max(200, Math.Min(520, width));
        }
        private void GroupRenameBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox tb &&
                tb.Tag is ViewModels.FamilyFavGroupViewModel vm)
                vm.CommitRenameCommand.Execute(null);
        }

        private void GroupRenameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not System.Windows.Controls.TextBox tb ||
                tb.Tag is not ViewModels.FamilyFavGroupViewModel vm)
                return;

            if (e.Key == Key.Enter)
            {
                vm.CommitRenameCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                vm.IsRenaming = false;
                e.Handled = true;
            }
        }
        // ── Monitor helper ────────────────────────────────────────────────────
        private static RECT GetMonitorWorkingArea(POINT pt)
        {
            IntPtr hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            if (hMonitor == IntPtr.Zero)
                return new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };

            var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            return GetMonitorInfo(hMonitor, ref info)
                ? info.rcWork
                : new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        }
    }
}
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using BATools.SelectionManager.ViewModels;
using Point = System.Windows.Point;

namespace BATools.SelectionManager.Views
{
    public partial class RecentToolbarWindow : Window
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);


        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;


        private readonly RecentToolbarViewModel _viewModel;

        public RecentToolbarWindow(RecentToolbarViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;

            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(RecentToolbarViewModel.IsVisible))
                {
                    if (!viewModel.IsVisible) Hide();
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
            Deactivated += (_, _) => Hide();

        }

        /// <summary>
        /// Shows the window at the given screen position,
        /// clamped to the monitor working area.
        /// </summary>
        public void ShowAt(Point screenPos)
        {
            // Let WPF measure the window first
            Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Arrange(new Rect(DesiredSize));

            double w = ActualWidth > 0 ? ActualWidth : 200;
            double h = ActualHeight > 0 ? ActualHeight : 200;

            var pt = new POINT { X = (int)screenPos.X, Y = (int)screenPos.Y };
            IntPtr hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);

            double left = screenPos.X + 4;
            double top = screenPos.Y + 4;
            double right = 1920;
            double bottom = 1080;

            if (hMonitor != IntPtr.Zero)
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                if (GetMonitorInfo(hMonitor, ref info))
                {
                    right = info.rcWork.Right;
                    bottom = info.rcWork.Bottom;
                }
            }

            // Flip left if would overflow right edge
            if (left + w > right)
                left = screenPos.X - w - 4;

            // Flip up if would overflow bottom edge
            if (top + h > bottom)
                top = screenPos.Y - h - 4;

            Left = left;
            Top = top;

            Show();
        }
    }
}
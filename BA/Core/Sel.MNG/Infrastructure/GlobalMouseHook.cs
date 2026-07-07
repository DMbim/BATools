using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Point = System.Windows.Point;
namespace BATools.SelectionManager.Infrastructure
{
    public sealed class GlobalMouseHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_RBUTTONDOWN = 0x0204;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(
            IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public int X;
            public int Y;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelMouseProc(
            int nCode, IntPtr wParam, IntPtr lParam);

        private IntPtr _hookHandle = IntPtr.Zero;
        private readonly LowLevelMouseProc _hookCallback; // field prevents GC
        private bool _disposed;

        /// <summary>Fires with screen position of every right mouse button down.</summary>
        public event Action<Point>? RightButtonDown;

        public GlobalMouseHook()
        {
            _hookCallback = HookCallback;
        }

        public void Install()
        {
            if (_hookHandle != IntPtr.Zero) return;

            using var process = Process.GetCurrentProcess();
            using var module = process.MainModule
                ?? throw new InvalidOperationException("Cannot get main module.");

            _hookHandle = SetWindowsHookEx(
                WH_MOUSE_LL, _hookCallback,
                GetModuleHandle(module.ModuleName), 0);

            if (_hookHandle == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"SetWindowsHookEx (mouse) failed: {Marshal.GetLastWin32Error()}");
        }

        public void Uninstall()
        {
            if (_hookHandle == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        // WH_MOUSE_LL runs on the thread that called SetWindowsHookEx.
        // Invoke events directly — same thread as keyboard hook.
        // MUST NOT throw under any circumstances.
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && (int)wParam == WM_RBUTTONDOWN)
                {
                    var ms = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(
                        lParam, typeof(MSLLHOOKSTRUCT))!;
                    RightButtonDown?.Invoke(new Point(ms.X, ms.Y));
                }
            }
            catch { }

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed) return;
            Uninstall();
            _disposed = true;
        }

        ~GlobalMouseHook() => Uninstall();
    }
}
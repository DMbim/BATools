using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BATools.SelectionManager.Infrastructure
{
    public sealed class GlobalKeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYUP = 0x0105;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(
            IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode, scanCode, flags, time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelKeyboardProc(
            int nCode, IntPtr wParam, IntPtr lParam);

        private IntPtr _hookHandle = IntPtr.Zero;
        private readonly LowLevelKeyboardProc _hookCallback; // field prevents GC
        private bool _disposed;

        public event Action<uint>? KeyDown;
        public event Action<uint>? KeyUp;

        public GlobalKeyboardHook()
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
                WH_KEYBOARD_LL, _hookCallback,
                GetModuleHandle(module.ModuleName), 0);

            if (_hookHandle == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
        }

        public void Uninstall()
        {
            if (_hookHandle == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        // WH_KEYBOARD_LL runs on the thread that called SetWindowsHookEx.
        // That is Revit's main (WPF UI) thread — so we can invoke events
        // directly without BeginInvoke. The callback MUST NOT throw.
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    var kb = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(
                        lParam, typeof(KBDLLHOOKSTRUCT))!;
                    uint vk = kb.vkCode;

                    int msg = (int)wParam;   // explicit cast — avoids IntPtr==int ambiguity

                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                        KeyDown?.Invoke(vk);
                    else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                        KeyUp?.Invoke(vk);
                }
            }
            catch
            {
                // Exceptions must never escape a Win32 callback
            }

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed) return;
            Uninstall();
            _disposed = true;
        }
        ~GlobalKeyboardHook()
        {
            Uninstall();
        }
    }
}
using System;
using System.Runtime.InteropServices;

namespace OpenCodeLauncher
{
    internal static class Native
    {
        public const int HwndBroadcast = 0xFFFF;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        public static readonly uint ShowWindowMessage;
        public static readonly uint ExitMessage;

        static Native()
        {
            ShowWindowMessage = RegisterWindowMessage("OpenCodeLauncher.ShowWindow");
            ExitMessage = RegisterWindowMessage("OpenCodeLauncher.Exit");
        }

        public static void PostShowWindow()
        {
            PostMessage(new IntPtr(HwndBroadcast), ShowWindowMessage, IntPtr.Zero, IntPtr.Zero);
        }

        public static void PostExit()
        {
            PostMessage(new IntPtr(HwndBroadcast), ExitMessage, IntPtr.Zero, IntPtr.Zero);
        }

        public static void ApplyDarkTitleBar(IntPtr hwnd)
        {
            try
            {
                var value = 1;
                var hr = DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int));
                if (hr != 0)
                {
                    DwmSetWindowAttribute(hwnd, 19, ref value, sizeof(int));
                }
            }
            catch
            {
            }
        }

        public static bool TryEnablePerMonitorDpi()
        {
            try
            {
                return SetProcessDpiAwarenessContext(new IntPtr(-4));
            }
            catch
            {
                return false;
            }
        }

        public static void AddLibraryDirectory(string path)
        {
            try
            {
                SetDllDirectory(path);
            }
            catch
            {
            }
        }
    }
}

using System;
using System.Runtime.InteropServices;

namespace JRunner
{
    // this class just wraps some Win32 stuff that we're going to use
    internal class NativeMethods
    {
        public const int HWND_BROADCAST = 0xffff;
        public static readonly int WM_SHOWAPP = RegisterWindowMessage("WM_SHOWAPP");
        [DllImport("user32")]
        public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);
        [DllImport("user32")]
        public static extern int RegisterWindowMessage(string message);
    }

    internal class WineMethods
    {
        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        public static bool IsWine()
        {
            try
            {
                IntPtr ntdll = GetModuleHandle("ntdll.dll");
                if (ntdll == IntPtr.Zero) return false;

                return GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
            }
            catch { }

            return false;
        }
    }
}

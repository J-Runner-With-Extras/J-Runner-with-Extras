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
        [DllImport("ntdll")]
        public static extern IntPtr wine_get_version();

        public static bool IsWine()
        {
            try
            {
                IntPtr ptr = wine_get_version();
                return true;
            }
            catch { }

            return false;
        }
    }
}

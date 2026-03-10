using System;
using System.Runtime.InteropServices;

namespace StageManager.Native.PInvoke
{
    public static class Win32Helper
    {
        public static void QuitApplication(IntPtr hwnd)
        {
            Win32.SendNotifyMessage(hwnd, Win32.WM_SYSCOMMAND, Win32.SC_CLOSE, IntPtr.Zero);
        }

        public static bool IsCloaked(IntPtr hwnd)
        {
            bool isCloaked;
            Win32.DwmGetWindowAttribute(hwnd, (int)Win32.DwmWindowAttribute.DWMWA_CLOAKED, out isCloaked, Marshal.SizeOf(typeof(bool)));
            return isCloaked;
        }

        public static bool IsAppWindow(IntPtr hwnd)
        {
            return (Win32.IsWindowVisible(hwnd) || Win32.IsIconic(hwnd)) &&
                   !Win32.GetWindowExStyleLongPtr(hwnd).HasFlag(Win32.WS_EX.WS_EX_NOACTIVATE) &&
                   !Win32.GetWindowStyleLongPtr(hwnd).HasFlag(Win32.WS.WS_CHILD);
        }

        public static bool IsAltTabWindow(IntPtr hWnd)
        {
            var exStyle = Win32.GetWindowExStyleLongPtr(hWnd);
            if (exStyle.HasFlag(Win32.WS_EX.WS_EX_TOOLWINDOW) ||
                Win32.GetWindow(hWnd, Win32.GW.GW_OWNER) != IntPtr.Zero)
            {
                return false;
            }
            if (exStyle.HasFlag(Win32.WS_EX.WS_EX_APPWINDOW))
            {
                return true;
            }

            return true;
        }

        public static void ForceForegroundWindow(IntPtr hWnd)
        {
            FocusStealer.Steal(hWnd);
        }
    }
}

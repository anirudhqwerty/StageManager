using System;
using System.Runtime.InteropServices;

namespace StageManager.Native.Interop
{
    public static class NativeMethods
    {
        [DllImport("dwmapi.dll", SetLastError = true)]
        public static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);

        [DllImport("dwmapi.dll")]
        public static extern int DwmUnregisterThumbnail(IntPtr thumb);

        // BUG FIX: The original used System.Windows.Size (WPF type, fields are double).
        // The actual Win32 DwmQueryThumbnailSourceSize fills a SIZE struct (two 32-bit ints).
        // Marshaling a WPF Size here means the runtime reads 8 bytes of doubles where the
        // OS wrote 8 bytes of ints — the values come out as garbage on any non-trivial size.
        // Fix: use a dedicated Win32Size struct with int fields that maps correctly.
        [DllImport("dwmapi.dll", PreserveSig = false)]
        public static extern void DwmQueryThumbnailSourceSize(IntPtr hThumbnail, out Win32Size size);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmUpdateThumbnailProperties(IntPtr hThumbnail, ref DWM_THUMBNAIL_PROPERTIES props);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hWnd, DWMWINDOWATTRIBUTE dwAttribute, out bool pvAttribute, int cbAttribute);

        // Also expose MonitorFromWindow and GetMonitorInfoW here since VisualHelper.cs
        // uses them via ControlzEx's NativeMethods — keeping them co-located is cleaner
        // and removes the hidden dependency on ControlzEx internals.
        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        public const uint MONITOR_DEFAULTTONEAREST = 2;
    }

    /// <summary>
    /// Win32 SIZE struct: two 32-bit integers.
    /// Do NOT replace with System.Windows.Size — that uses doubles and will
    /// produce corrupt values when marshaled from native code.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Win32Size
    {
        public int cx;
        public int cy;
    }
}

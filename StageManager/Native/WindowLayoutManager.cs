using StageManager.Native.PInvoke;
using StageManager.Native.Window;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace StageManager.Native
{
    public static class WindowLayoutManager
    {
        private const int SIDEBAR_WIDTH = 160;
        private const int GAP = 8;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        public static void SplitScreen(IEnumerable<IWindow> windows)
        {
            var list = windows
                .Where(w => w.CanLayout && !w.IsMinimized)
                .ToList();

            if (list.Count == 0) return;

            var workArea = GetWorkAreaForWindow(list[0].Handle);
            int areaX = workArea.Left + SIDEBAR_WIDTH + GAP;
            int areaY = workArea.Top + GAP;
            int areaW = workArea.Right - areaX - GAP;
            int areaH = workArea.Bottom - workArea.Top - (GAP * 2);

            if (areaW <= 0 || areaH <= 0) return;

            var slots = ComputeSplitSlots(list.Count, areaX, areaY, areaW, areaH);

            foreach (var w in list)
            {
                if (w.IsMaximized)
                    Win32.ShowWindow(w.Handle, Win32.SW.SW_RESTORE);
            }

            var hdwp = Win32.BeginDeferWindowPos(list.Count);
            if (hdwp == IntPtr.Zero) return;

            for (int i = 0; i < list.Count && i < slots.Count; i++)
            {
                var w = list[i];
                var s = slots[i];
                var offset = w.Offset;

                hdwp = Win32.DeferWindowPos(
                    hdwp,
                    w.Handle,
                    IntPtr.Zero,
                    s.X + offset.X,
                    s.Y + offset.Y,
                    s.Width + offset.Width,
                    s.Height + offset.Height,
                    Win32.SWP.SWP_NOZORDER |
                    Win32.SWP.SWP_NOOWNERZORDER |
                    Win32.SWP.SWP_NOACTIVATE |
                    Win32.SWP.SWP_FRAMECHANGED);

                if (hdwp == IntPtr.Zero) return;
            }

            Win32.EndDeferWindowPos(hdwp);
        }

        private static List<WindowSlot> ComputeSplitSlots(int count, int areaX, int areaY, int areaW, int areaH)
        {
            var slots = new List<WindowSlot>();

            if (count == 1)
            {
                int w = (int)(areaW * 0.92);
                int h = (int)(areaH * 0.95);
                int x = areaX + (areaW - w) / 2;
                int y = areaY + (areaH - h) / 2;
                slots.Add(new WindowSlot(x, y, w, h));
                return slots;
            }

            if (count == 2)
            {
                int halfW = (areaW - GAP) / 2;
                slots.Add(new WindowSlot(areaX, areaY, halfW, areaH));
                slots.Add(new WindowSlot(areaX + halfW + GAP, areaY, halfW, areaH));
                return slots;
            }

            if (count == 3)
            {
                int leftW = (int)(areaW * 0.50);
                int rightW = areaW - leftW - GAP;
                int halfH = (areaH - GAP) / 2;

                slots.Add(new WindowSlot(areaX, areaY, leftW, areaH));
                slots.Add(new WindowSlot(areaX + leftW + GAP, areaY, rightW, halfH));
                slots.Add(new WindowSlot(areaX + leftW + GAP, areaY + halfH + GAP, rightW, halfH));
                return slots;
            }

            if (count == 4)
            {
                int halfW = (areaW - GAP) / 2;
                int halfH = (areaH - GAP) / 2;

                slots.Add(new WindowSlot(areaX, areaY, halfW, halfH));
                slots.Add(new WindowSlot(areaX + halfW + GAP, areaY, halfW, halfH));
                slots.Add(new WindowSlot(areaX, areaY + halfH + GAP, halfW, halfH));
                slots.Add(new WindowSlot(areaX + halfW + GAP, areaY + halfH + GAP, halfW, halfH));
                return slots;
            }

            int cols = (int)Math.Ceiling(Math.Sqrt(count));
            int rows = (int)Math.Ceiling((double)count / cols);
            int cellW = (areaW - (cols - 1) * GAP) / cols;
            int cellH = (areaH - (rows - 1) * GAP) / rows;

            for (int i = 0; i < count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                int windowsInRow = Math.Min(cols, count - row * cols);
                int rowW = windowsInRow * cellW + (windowsInRow - 1) * GAP;
                int rowOffsetX = (areaW - rowW) / 2;
                int colInRow = i - row * cols;

                int x = areaX + rowOffsetX + colInRow * (cellW + GAP);
                int y = areaY + row * (cellH + GAP);
                slots.Add(new WindowSlot(x, y, cellW, cellH));
            }

            return slots;
        }

        private static RECT GetWorkAreaForWindow(IntPtr hwnd)
        {
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var info = new MONITORINFO();
                info.cbSize = (uint)Marshal.SizeOf(info);
                if (GetMonitorInfo(monitor, ref info))
                {
                    return info.rcWork;
                }
            }

            return new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        }

        private readonly struct WindowSlot
        {
            public readonly int X, Y, Width, Height;

            public WindowSlot(int x, int y, int width, int height)
            {
                X = x; Y = y; Width = width; Height = height;
            }
        }
    }
}

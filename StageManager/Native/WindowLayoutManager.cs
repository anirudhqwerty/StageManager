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
        private const int STACK_OFFSET = 22;

        [DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);

        private const uint SPI_GETWORKAREA = 0x0030;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        public static void StackWindows(IEnumerable<IWindow> windows)
        {
            var list = windows
                .Where(w => w.CanLayout && !w.IsMinimized)
                .ToList();

            if (list.Count == 0) return;

            var workArea = GetWorkArea();
            int areaX = workArea.Left + SIDEBAR_WIDTH + GAP;
            int areaY = workArea.Top + GAP;
            int areaW = workArea.Right - areaX - GAP;
            int areaH = workArea.Bottom - workArea.Top - (GAP * 2);

            if (areaW <= 0 || areaH <= 0) return;

            int idealW = (int)(areaW * 0.82);
            int idealH = (int)(areaH * 0.88);

            int totalStackWidth = (list.Count - 1) * STACK_OFFSET;
            int totalStackHeight = (list.Count - 1) * STACK_OFFSET;

            int baseX = areaX + (areaW - idealW - totalStackWidth) / 2;
            int baseY = areaY + (areaH - idealH - totalStackHeight) / 2;

            if (baseX < areaX) baseX = areaX;
            if (baseY < areaY) baseY = areaY;

            for (int i = 0; i < list.Count; i++)
            {
                var w = list[i];
                int x = baseX + i * STACK_OFFSET;
                int y = baseY + i * STACK_OFFSET;

                if (w.IsMaximized)
                    Win32.ShowWindow(w.Handle, Win32.SW.SW_RESTORE);

                var offset = w.Offset;

                Win32.SetWindowPos(
                    w.Handle,
                    IntPtr.Zero,
                    x + offset.X,
                    y + offset.Y,
                    idealW + offset.Width,
                    idealH + offset.Height,
                    Win32.SetWindowPosFlags.DoNotActivate |
                    Win32.SetWindowPosFlags.IgnoreZOrder);
            }
        }

        public static void SplitScreen(IEnumerable<IWindow> windows)
        {
            var list = windows
                .Where(w => w.CanLayout && !w.IsMinimized)
                .ToList();

            if (list.Count == 0) return;

            var workArea = GetWorkArea();
            int areaX = workArea.Left + SIDEBAR_WIDTH + GAP;
            int areaY = workArea.Top + GAP;
            int areaW = workArea.Right - areaX - GAP;
            int areaH = workArea.Bottom - workArea.Top - (GAP * 2);

            if (areaW <= 0 || areaH <= 0) return;

            var slots = ComputeSplitSlots(list.Count, areaX, areaY, areaW, areaH);

            for (int i = 0; i < list.Count && i < slots.Count; i++)
            {
                var w = list[i];
                var s = slots[i];

                if (w.IsMaximized)
                    Win32.ShowWindow(w.Handle, Win32.SW.SW_RESTORE);

                var offset = w.Offset;

                Win32.SetWindowPos(
                    w.Handle,
                    IntPtr.Zero,
                    s.X + offset.X,
                    s.Y + offset.Y,
                    s.Width + offset.Width,
                    s.Height + offset.Height,
                    Win32.SetWindowPosFlags.DoNotActivate |
                    Win32.SetWindowPosFlags.IgnoreZOrder);
            }
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

        private static RECT GetWorkArea()
        {
            var rect = new RECT();
            SystemParametersInfo(SPI_GETWORKAREA, 0, ref rect, 0);
            return rect;
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

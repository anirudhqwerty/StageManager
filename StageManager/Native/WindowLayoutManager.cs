using StageManager.Native.Interop;
using StageManager.Native.PInvoke;
using StageManager.Native.Window;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;

namespace StageManager.Native
{
	public static class WindowLayoutManager
	{
		private const int SIDEBAR_WIDTH_LOGICAL = 160;
		private const int INNER_GAP = 8;

		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

		[DllImport("shcore.dll")]
		private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

		private const int MDT_EFFECTIVE_DPI = 0;

		[StructLayout(LayoutKind.Sequential)]
		private struct MONITORINFO
		{
			public uint cbSize;
			public MONRECT rcMonitor;
			public MONRECT rcWork;
			public uint dwFlags;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct MONRECT
		{
			public int Left, Top, Right, Bottom;
		}

		public static void SplitScreen(IEnumerable<IWindow> windows)
		{
			var list = windows
				.Where(w => w.CanLayout && !w.IsMinimized)
				.ToList();

			if (list.Count == 0) return;

			var monitor = NativeMethods.MonitorFromWindow(list[0].Handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
			var workArea = GetWorkAreaForMonitor(monitor);

			float dpiScale = GetDpiScale(monitor);
			int sidebarPhysical = (int)Math.Round(SIDEBAR_WIDTH_LOGICAL * dpiScale);

			int areaX = workArea.Left + sidebarPhysical;
			int areaY = workArea.Top;
			int areaW = workArea.Right - areaX;
			int areaH = workArea.Bottom - workArea.Top;

			if (areaW <= 0 || areaH <= 0) return;

			var slots = ComputeSplitSlots(list.Count, areaX, areaY, areaW, areaH);
			if (slots.Count == 0) return;

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

				bool offsetInvalid =
					Math.Abs(offset.X) > 20 ||
					Math.Abs(offset.Y) > 20 ||
					Math.Abs(offset.Width) > 40 ||
					Math.Abs(offset.Height) > 40;

				if (offsetInvalid)
					offset = new Rectangle(-7, 0, 14, 7);

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
					Win32.SWP.SWP_NOACTIVATE);

				if (hdwp == IntPtr.Zero) return;
			}

			Win32.EndDeferWindowPos(hdwp);
		}

		private static List<WindowSlot> ComputeSplitSlots(int count, int areaX, int areaY, int areaW, int areaH)
		{
			var slots = new List<WindowSlot>();

			if (count == 1)
			{
				slots.Add(new WindowSlot(areaX, areaY, areaW, areaH));
				return slots;
			}

			if (count == 2)
			{
				int leftW = (areaW - INNER_GAP) / 2;
				int rightW = areaW - leftW - INNER_GAP;
				slots.Add(new WindowSlot(areaX, areaY, leftW, areaH));
				slots.Add(new WindowSlot(areaX + leftW + INNER_GAP, areaY, rightW, areaH));
				return slots;
			}

			if (count == 3)
			{
				int leftW = (areaW - INNER_GAP) / 2;
				int rightW = areaW - leftW - INNER_GAP;
				int topH = (areaH - INNER_GAP) / 2;
				int botH = areaH - topH - INNER_GAP;

				slots.Add(new WindowSlot(areaX, areaY, leftW, areaH));
				slots.Add(new WindowSlot(areaX + leftW + INNER_GAP, areaY, rightW, topH));
				slots.Add(new WindowSlot(areaX + leftW + INNER_GAP, areaY + topH + INNER_GAP, rightW, botH));
				return slots;
			}

			if (count == 4)
			{
				int leftW = (areaW - INNER_GAP) / 2;
				int rightW = areaW - leftW - INNER_GAP;
				int topH = (areaH - INNER_GAP) / 2;
				int botH = areaH - topH - INNER_GAP;

				slots.Add(new WindowSlot(areaX, areaY, leftW, topH));
				slots.Add(new WindowSlot(areaX + leftW + INNER_GAP, areaY, rightW, topH));
				slots.Add(new WindowSlot(areaX, areaY + topH + INNER_GAP, leftW, botH));
				slots.Add(new WindowSlot(areaX + leftW + INNER_GAP, areaY + topH + INNER_GAP, rightW, botH));
				return slots;
			}

			int cols = (int)Math.Ceiling(Math.Sqrt(count));
			int rows = (int)Math.Ceiling((double)count / cols);

			int cellW = (areaW - (cols - 1) * INNER_GAP) / cols;
			int cellH = (areaH - (rows - 1) * INNER_GAP) / rows;

			for (int i = 0; i < count; i++)
			{
				int col = i % cols;
				int row = i / cols;

				int windowsInThisRow = Math.Min(cols, count - row * cols);
				bool isLastInRow = col == windowsInThisRow - 1;
				bool isLastRow = row == rows - 1;

				int x = areaX + col * (cellW + INNER_GAP);
				int y = areaY + row * (cellH + INNER_GAP);

				int w = isLastInRow ? (areaX + areaW - x) : cellW;
				int h = isLastRow ? (areaY + areaH - y) : cellH;

				slots.Add(new WindowSlot(x, y, w, h));
			}

			return slots;
		}

		private static float GetDpiScale(IntPtr hMonitor)
		{
			if (hMonitor != IntPtr.Zero)
			{
				int hr = GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _);
				if (hr == 0 && dpiX > 0)
					return dpiX / 96f;
			}

			return 1f;
		}

		private static MONRECT GetWorkAreaForMonitor(IntPtr hMonitor)
		{
			if (hMonitor != IntPtr.Zero)
			{
				var info = new MONITORINFO();
				info.cbSize = (uint)Marshal.SizeOf(info);
				if (GetMonitorInfo(hMonitor, ref info))
					return info.rcWork;
			}

			var wa = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea
					 ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
			return new MONRECT { Left = wa.Left, Top = wa.Top, Right = wa.Right, Bottom = wa.Bottom };
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


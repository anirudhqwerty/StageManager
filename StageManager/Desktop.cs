using Microsoft.Win32;
using StageManager.Native.PInvoke;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace StageManager
{
	internal class Desktop
	{
		[DllImport("user32.dll", SetLastError = true)]
		static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		static extern IntPtr SendMessage(IntPtr hWnd, UInt32 Msg, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll", SetLastError = true)]
		public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);

		[DllImport("user32.dll", SetLastError = false)]
		static extern IntPtr GetDesktopWindow();

		private const int WM_COMMAND = 0x111;
		private IntPtr _desktopViewHandle;
		private bool? _iconsVisibleCache;
		private RegistryKey? _advancedKey;

		public Desktop()
		{
			try
			{
				_advancedKey = Registry.CurrentUser.OpenSubKey(
					@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", writable: false);
			}
			catch { }
		}

		public void TrySetDesktopView(IntPtr handle)
		{
			var buffer = new StringBuilder(255);
			Win32.GetClassName(handle, buffer, buffer.Capacity + 1);
			if (buffer.ToString() == "WorkerW")
				_desktopViewHandle = handle;
		}

		public bool GetDesktopIconsVisible()
		{
			if (_iconsVisibleCache.HasValue)
				return _iconsVisibleCache.Value;

			_iconsVisibleCache = ReadDesktopIconsVisibleFromRegistry();
			return _iconsVisibleCache.Value;
		}

		private bool ReadDesktopIconsVisibleFromRegistry()
		{
			try
			{
				var key = _advancedKey
					?? Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", writable: false);

				if (key?.GetValue("HideIcons", 0) is int hideIconsValue)
					return hideIconsValue == 0;
			}
			catch { }

			return true;
		}

		private void ToggleDesktopIcons()
		{
			var toggleDesktopCommand = new IntPtr(0x7402);
			SendMessage(GetDesktopSHELLDLL_DefView(), WM_COMMAND, toggleDesktopCommand, IntPtr.Zero);
			_iconsVisibleCache = null;
		}

		public void ShowIcons()
		{
			if (!GetDesktopIconsVisible())
			{
				ToggleDesktopIcons();
				_iconsVisibleCache = true;
			}
		}

		public void HideIcons()
		{
			if (GetDesktopIconsVisible())
			{
				ToggleDesktopIcons();
				_iconsVisibleCache = false;
			}
		}

		public void InvalidateIconVisibilityCache()
		{
			_iconsVisibleCache = null;
		}

		static IntPtr GetDesktopSHELLDLL_DefView()
		{
			var hShellViewWin = IntPtr.Zero;
			var hWorkerW = IntPtr.Zero;

			var hProgman = FindWindow("Progman", "Program Manager");
			var hDesktopWnd = GetDesktopWindow();

			if (hProgman != IntPtr.Zero)
			{
				hShellViewWin = FindWindowEx(hProgman, IntPtr.Zero, "SHELLDLL_DefView", null);

				if (hShellViewWin == IntPtr.Zero)
				{
					do
					{
						hWorkerW = FindWindowEx(hDesktopWnd, hWorkerW, "WorkerW", null);
						hShellViewWin = FindWindowEx(hWorkerW, IntPtr.Zero, "SHELLDLL_DefView", null);
					} while (hShellViewWin == IntPtr.Zero && hWorkerW != IntPtr.Zero);
				}
			}
			return hShellViewWin;
		}

		public bool HasDesktopView => _desktopViewHandle != IntPtr.Zero;
		public IntPtr DesktopViewHandle => _desktopViewHandle;
	}
}

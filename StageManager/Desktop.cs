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

		// Cache desktop icon visibility - reading the registry on every overlap tick (every 500ms) is wasteful
		private bool? _iconsVisibleCache;
		private RegistryKey? _advancedKey;

		public Desktop()
		{
			// Subscribe to registry changes so our cache stays valid
			try
			{
				_advancedKey = Registry.CurrentUser.OpenSubKey(
					@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", writable: false);
			}
			catch
			{
				// If we can't open the key, we'll just re-read each time
			}
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
			// Use cached value when available - this is called frequently
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
			catch { /* Fall through to default */ }

			return true;
		}

		private void ToggleDesktopIcons()
		{
			var toggleDesktopCommand = new IntPtr(0x7402);
			SendMessage(GetDesktopSHELLDLL_DefView(), WM_COMMAND, toggleDesktopCommand, IntPtr.Zero);
			// Invalidate cache after toggling
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

		/// <summary>
		/// Call this to force a registry re-read on the next GetDesktopIconsVisible call.
		/// Useful if something external may have changed the registry value.
		/// </summary>
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

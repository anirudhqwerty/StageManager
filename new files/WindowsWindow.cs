using StageManager.Native.PInvoke;
using StageManager.Native.Window;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace StageManager.Native
{
	public class WindowsWindow : IWindow
	{
		private readonly IntPtr _handle;
		private bool _didManualHide;

		public event IWindowDelegate? WindowClosed;
		public event IWindowDelegate? WindowUpdated;
		public event IWindowDelegate? WindowFocused;

		private readonly int _processId;
		private readonly string _processName;
		private readonly string _processFileName;
		private readonly string _processExecutable;
		private IWindowLocation? _lastLocation;

		public WindowsWindow(IntPtr handle)
		{
			_handle = handle;

			try
			{
				var process = GetProcessByWindowHandle(_handle);
				_processId = process?.Id ?? -1;
				_processName = process?.ProcessName ?? string.Empty;
				_processExecutable = string.Empty;

				if (process != null)
				{
					try
					{
						var mainModule = process.MainModule;
						if (mainModule != null)
						{
							_processExecutable = mainModule.FileName ?? string.Empty;
							_processFileName = Path.GetFileName(_processExecutable);
						}
						else
						{
							_processFileName = string.Empty;
						}
					}
					catch (Exception)
					{
						// Access denied to MainModule (common for elevated processes)
						_processFileName = process.ProcessName;
					}
				}
				else
				{
					_processFileName = string.Empty;
				}
			}
			catch (Exception)
			{
				_processId = -1;
				_processName = string.Empty;
				_processFileName = string.Empty;
				_processExecutable = string.Empty;
			}

			_processFileName ??= string.Empty;
		}

		private Process? GetProcessByWindowHandle(IntPtr windowHandle)
		{
			Win32.GetWindowThreadProcessId(windowHandle, out var processId);
			if (processId == 0) return null;

			var result = (int)processId;

			try
			{
				var process = Process.GetProcessById(result);

				// Handle UWP apps hosted in ApplicationFrameHost
				if (process.ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
				{
					// Try to find the real hosted UWP process by looking at child windows
					var uwpProcess = TryGetUwpHostedProcess(windowHandle);
					if (uwpProcess != null)
						return uwpProcess;
				}

				return process;
			}
			catch (ArgumentException)
			{
				// Process already exited
				return null;
			}
		}

		/// <summary>
		/// UWP apps run inside ApplicationFrameHost. We walk child windows to find the
		/// actual UWP app process so we get the right name/icon.
		/// </summary>
		private Process? TryGetUwpHostedProcess(IntPtr frameHostWindow)
		{
			Process? uwpProcess = null;

			Win32.EnumChildWindows(frameHostWindow, (hwnd, _) =>
			{
				Win32.GetWindowThreadProcessId(hwnd, out var childPid);
				if (childPid != 0)
				{
					try
					{
						var candidate = Process.GetProcessById((int)childPid);
						if (!candidate.ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
						{
							uwpProcess = candidate;
							return false; // Stop enumeration
						}
					}
					catch { /* Process gone */ }
				}
				return true;
			}, IntPtr.Zero);

			return uwpProcess;
		}

		public bool DidManualHide => _didManualHide;

		public string Title
		{
			get
			{
				var buffer = new StringBuilder(512);
				Win32.GetWindowText(_handle, buffer, buffer.Capacity + 1);
				return buffer.ToString();
			}
		}

		public IntPtr Handle => _handle;

		public string Class
		{
			get
			{
				var buffer = new StringBuilder(255);
				Win32.GetClassName(_handle, buffer, buffer.Capacity + 1);
				return buffer.ToString();
			}
		}

		public IWindowLocation Location
		{
			get
			{
				Win32.Rect rect = new Win32.Rect();
				Win32.GetWindowRect(_handle, ref rect);

				WindowState state = IsMinimized ? WindowState.Minimized
								  : IsMaximized ? WindowState.Maximized
								  : WindowState.Normal;

				return new WindowLocation(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, state);
			}
		}

		public void StoreLastLocation() => _lastLocation = Location;

		public IWindowLocation? PopLastLocation()
		{
			var value = _lastLocation;
			_lastLocation = null;
			return value;
		}

		public Rectangle Offset
		{
			get
			{
				Win32.Rect rect1 = new();
				Win32.GetWindowRect(_handle, ref rect1);

				Win32.Rect rect2 = new();
				int size = Marshal.SizeOf(typeof(Win32.Rect));
				Win32.DwmGetWindowAttribute(_handle, (int)Win32.DwmWindowAttribute.DWMWA_EXTENDED_FRAME_BOUNDS, out rect2, size);

				return new Rectangle(
					rect1.Left - rect2.Left,
					rect1.Top - rect2.Top,
					(rect1.Right - rect1.Left) - (rect2.Right - rect2.Left),
					(rect1.Bottom - rect1.Top) - (rect2.Bottom - rect2.Top));
			}
		}

		public int ProcessId => _processId;
		public string ProcessFileName => _processFileName;
		public string ProcessName => _processName;

		public bool CanLayout
		{
			get
			{
				return _didManualHide ||
					(!Win32Helper.IsCloaked(_handle) &&
					   Win32Helper.IsAppWindow(_handle) &&
					   Win32Helper.IsAltTabWindow(_handle));
			}
		}

		private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
		{
			"TaskManagerWindow", "MSCTFIME UI", "SHELLDLL_DefView",
			"LockScreenBackstopFrame", "Progman", "Shell_TrayWnd", "WorkerW"
		};

		private static readonly HashSet<string> IgnoredProcesses = new(StringComparer.OrdinalIgnoreCase)
		{
			"SearchUI", "ShellExperienceHost", "PeopleExperienceHost", "LockApp",
			"StartMenuExperienceHost", "SearchApp", "SearchHost", "search", "ScreenClippingHost"
		};

		public bool IsCandidate()
		{
			if (!CanLayout) return false;
			if (IgnoredClasses.Contains(Class)) return false;
			if (IgnoredProcesses.Contains(ProcessName)) return false;
			return true;
		}

		public bool IsFocused => Win32.GetForegroundWindow() == _handle;
		public bool IsMinimized => Win32.IsIconic(_handle);
		public bool IsMaximized => Win32.IsZoomed(_handle);
		public bool IsMouseMoving { get; internal set; }

		public void Focus()
		{
			if (!IsFocused)
			{
				Win32Helper.ForceForegroundWindow(_handle);
				WindowFocused?.Invoke(this);
			}
		}

		public void Hide()
		{
			if (CanLayout)
				_didManualHide = true;
			Win32.ShowWindow(_handle, Win32.SW.SW_HIDE);
		}

		public void ShowNormal()
		{
			_didManualHide = false;
			Win32.ShowWindow(_handle, Win32.SW.SW_SHOWNOACTIVATE);
		}

		public void ShowMaximized()
		{
			_didManualHide = false;
			Win32.ShowWindow(_handle, Win32.SW.SW_SHOWMAXIMIZED);
		}

		public void ShowMinimized()
		{
			_didManualHide = false;
			Win32.ShowWindow(_handle, Win32.SW.SW_SHOWMINIMIZED);
		}

		public void ShowInCurrentState()
		{
			if (IsMinimized) ShowMinimized();
			else if (IsMaximized) ShowMaximized();
			else ShowNormal();

			WindowUpdated?.Invoke(this);
		}

		public void BringToTop()
		{
			Win32.BringWindowToTop(_handle);
			WindowUpdated?.Invoke(this);
		}

		public void Close()
		{
			Win32Helper.QuitApplication(_handle);
			WindowClosed?.Invoke(this);
		}

		public void NotifyUpdated() => WindowUpdated?.Invoke(this);

		public override string ToString() => $"[{Handle}][{Title}][{Class}][{ProcessName}]";

		public Icon? ExtractIcon()
		{
			if (string.IsNullOrWhiteSpace(_processExecutable))
				return null;

			try
			{
				return Icon.ExtractAssociatedIcon(_processExecutable);
			}
			catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
			{
				// File gone, locked, or we don't have permission - silently return null
				return null;
			}
		}
	}
}

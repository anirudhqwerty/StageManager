using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using StageManager.Native.PInvoke;
using StageManager.Native.Window;

namespace StageManager.Native
{
	public delegate void WindowDelegate(IWindow window);
	public delegate void WindowCreateDelegate(IWindow window, bool firstCreate);
	public delegate void WindowUpdateDelegate(IWindow window, WindowUpdateType type);

	public class WindowsManager : IWindowsManager
	{
		private bool _active;
		private IDictionary<IntPtr, WindowsWindow> _windows;
		private WinEventDelegate _hookDelegate;

		private WindowsWindow? _mouseMoveWindow;
		private readonly object _mouseMoveLock = new object();
		private Win32.HookProc _mouseHook;

		// BUG FIX: Store hook handles so we can properly unhook on Stop()
		// The original Stop() only set _active = false but left all WinEvent hooks registered.
		// This meant the hook callbacks kept firing on the OS thread even after "stop",
		// and the mouse hook thread (Application.Run) was never terminated.
		private readonly List<IntPtr> _winEventHooks = new();
		private IntPtr _mouseHookHandle;
		private Thread? _hookThread;

		private Dictionary<WindowsWindow, bool> _floating;
		private IntPtr _currentProcessWindowHandle;
		private int _currentProcessId;

		public event WindowCreateDelegate? WindowCreated;
		public event WindowDelegate? WindowDestroyed;
		public event WindowUpdateDelegate? WindowUpdated;
		public event EventHandler<IntPtr>? UntrackedFocus;
		public event WindowFocusDelegate? WindowFocused;
		public event EventHandler? WindowMoved;
		public event WindowDelegate? ExternalWindowUpdate;
		public event WindowDelegate? ExternalWindowClosed;

		public IEnumerable<IWindow> Windows => _windows.Values;

		public WindowsManager()
		{
			_windows = new Dictionary<IntPtr, WindowsWindow>();
			_floating = new Dictionary<WindowsWindow, bool>();
			_hookDelegate = new WinEventDelegate(WindowHook);
		}

		public Task Start()
		{
			_active = true;

			var currentProcess = Process.GetCurrentProcess();
			_currentProcessId = currentProcess.Id;
			_currentProcessWindowHandle = currentProcess.MainWindowHandle;

			RegisterWinEventHook(Win32.EVENT_CONSTANTS.EVENT_OBJECT_DESTROY, Win32.EVENT_CONSTANTS.EVENT_OBJECT_SHOW);
			RegisterWinEventHook(Win32.EVENT_CONSTANTS.EVENT_OBJECT_CLOAKED, Win32.EVENT_CONSTANTS.EVENT_OBJECT_UNCLOAKED);
			RegisterWinEventHook(Win32.EVENT_CONSTANTS.EVENT_SYSTEM_MINIMIZESTART, Win32.EVENT_CONSTANTS.EVENT_SYSTEM_MINIMIZEEND);
			RegisterWinEventHook(Win32.EVENT_CONSTANTS.EVENT_SYSTEM_MOVESIZESTART, Win32.EVENT_CONSTANTS.EVENT_SYSTEM_MOVESIZEEND);
			RegisterWinEventHook(Win32.EVENT_CONSTANTS.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_CONSTANTS.EVENT_SYSTEM_FOREGROUND);
			RegisterWinEventHook(Win32.EVENT_CONSTANTS.EVENT_OBJECT_LOCATIONCHANGE, Win32.EVENT_CONSTANTS.EVENT_OBJECT_LOCATIONCHANGE);

			_mouseHook = MouseHook;

			Win32.EnumWindows((handle, param) =>
			{
				if (Win32Helper.IsAppWindow(handle))
					RegisterWindow(handle, false);
				return true;
			}, IntPtr.Zero);

			_hookThread = new Thread(() =>
			{
				_mouseHookHandle = Win32.SetWindowsHookEx(
					Win32.WH_MOUSE_LL,
					_mouseHook,
					currentProcess.MainModule!.BaseAddress,
					0);

				Application.Run(); // Pumps messages for the low-level mouse hook
			});

			_hookThread.Name = "WindowsManager.HookThread";
			_hookThread.IsBackground = true; // Don't keep the process alive just for this thread
			_hookThread.Start();

			return Task.CompletedTask;
		}

		public void Stop()
		{
			_active = false;

			// Unhook all WinEvent hooks (was completely missing before)
			foreach (var hook in _winEventHooks)
			{
				try { Win32.UnhookWinEvent(hook); } catch { /* Best effort */ }
			}
			_winEventHooks.Clear();

			// Terminate the hook message pump thread
			// Application.ExitThread() must be called from within that thread's context,
			// so we post it. The thread is IsBackground so it won't block process exit anyway.
			if (_hookThread is not null && _hookThread.IsAlive)
			{
				try { Application.ExitThread(); } catch { /* Best effort */ }
			}
		}

		private void RegisterWinEventHook(Win32.EVENT_CONSTANTS min, Win32.EVENT_CONSTANTS max)
		{
			var hook = Win32.SetWinEventHook(min, max, IntPtr.Zero, _hookDelegate, 0, 0, 0);
			if (hook != IntPtr.Zero)
				_winEventHooks.Add(hook);
		}

		public IWindowsDeferPosHandle DeferWindowsPos(int count)
		{
			var info = Win32.BeginDeferWindowPos(count);
			return new WindowsDeferPosHandle(info);
		}

		public void ToggleFocusedWindowTiling()
		{
			if (!_active) return;

			var window = _windows.Values.FirstOrDefault(w => w.IsFocused);
			if (window != null)
			{
				if (_floating.ContainsKey(window))
				{
					_floating.Remove(window);
					HandleWindowAdd(window, false);
				}
				else
				{
					_floating[window] = true;
					HandleWindowRemove(window);
					window.BringToTop();
				}
				window.Focus();
			}
		}

		private IntPtr MouseHook(int nCode, UIntPtr wParam, IntPtr lParam)
		{
			if (nCode == 0 && (uint)wParam == Win32.WM_LBUTTONUP)
				HandleWindowMoveEnd();

			return Win32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
		}

		private void WindowHook(IntPtr hWinEventHook, Win32.EVENT_CONSTANTS eventType, IntPtr hwnd,
			Win32.OBJID idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
		{
			if (!_active) return;
			if (!EventWindowIsValid(idChild, idObject, hwnd)) return;

			switch (eventType)
			{
				case Win32.EVENT_CONSTANTS.EVENT_OBJECT_SHOW:
					RegisterWindow(hwnd);
					break;
				case Win32.EVENT_CONSTANTS.EVENT_OBJECT_DESTROY:
					UnregisterWindow(hwnd);
					break;
				case Win32.EVENT_CONSTANTS.EVENT_OBJECT_CLOAKED:
					UpdateWindow(hwnd, WindowUpdateType.Hide);
					break;
				case Win32.EVENT_CONSTANTS.EVENT_OBJECT_UNCLOAKED:
					UpdateWindow(hwnd, WindowUpdateType.Show);
					break;
				case Win32.EVENT_CONSTANTS.EVENT_SYSTEM_MINIMIZESTART:
					UpdateWindow(hwnd, WindowUpdateType.MinimizeStart);
					break;
				case Win32.EVENT_CONSTANTS.EVENT_SYSTEM_MINIMIZEEND:
					UpdateWindow(hwnd, WindowUpdateType.MinimizeEnd);
					break;
				case Win32.EVENT_CONSTANTS.EVENT_SYSTEM_FOREGROUND:
					UpdateWindow(hwnd, WindowUpdateType.Foreground);
					break;
				case Win32.EVENT_CONSTANTS.EVENT_SYSTEM_MOVESIZESTART:
					StartWindowMove(hwnd);
					break;
				case Win32.EVENT_CONSTANTS.EVENT_SYSTEM_MOVESIZEEND:
					EndWindowMove(hwnd);
					break;
				case Win32.EVENT_CONSTANTS.EVENT_OBJECT_LOCATIONCHANGE:
					WindowMove(hwnd);
					break;
			}
		}

		private bool EventWindowIsValid(int idChild, Win32.OBJID idObject, IntPtr hwnd)
			=> idChild == Win32.CHILDID_SELF && idObject == Win32.OBJID.OBJID_WINDOW && hwnd != IntPtr.Zero;

		private void RegisterWindow(IntPtr handle, bool emitEvent = true)
		{
			if (!_active) return;
			if (handle == _currentProcessWindowHandle) return;

			if (!_windows.ContainsKey(handle))
			{
				var window = new WindowsWindow(handle);

				if (window.ProcessId < 0 || window.ProcessId == _currentProcessId)
					return;

				if (window.IsCandidate())
				{
					window.WindowFocused += sender => HandleWindowFocused(sender);
					window.WindowUpdated += sender => HandleWindowUpdated(sender);
					window.WindowClosed += sender => HandleWindowClosed(sender);

					_windows[handle] = window;

					if (emitEvent)
						HandleWindowAdd(window, true);
				}
			}
		}

		private void UnregisterWindow(IntPtr handle)
		{
			if (!_active) return;

			if (_windows.ContainsKey(handle))
			{
				var window = _windows[handle];
				_windows.Remove(handle);
				HandleWindowRemove(window);
			}
		}

		private void UpdateWindow(IntPtr handle, WindowUpdateType type)
		{
			if (!_active) return;

			if (type == WindowUpdateType.Show && _windows.ContainsKey(handle))
			{
				WindowUpdated?.Invoke(_windows[handle], type);
			}
			else if (type == WindowUpdateType.Show)
			{
				RegisterWindow(handle);
			}
			else if (type == WindowUpdateType.Hide && _windows.ContainsKey(handle))
			{
				var window = _windows[handle];
				if (!window.DidManualHide)
					UnregisterWindow(handle);
				else
					WindowUpdated?.Invoke(window, type);
			}
			else if (_windows.ContainsKey(handle))
			{
				WindowUpdated?.Invoke(_windows[handle], type);
			}
			else
			{
				UntrackedFocus?.Invoke(this, handle);
			}
		}

		private void StartWindowMove(IntPtr handle)
		{
			if (!_active || !_windows.ContainsKey(handle)) return;

			var window = _windows[handle];
			window.StoreLastLocation();
			HandleWindowMoveStart(window);
			WindowUpdated?.Invoke(window, WindowUpdateType.MoveStart);
		}

		private void EndWindowMove(IntPtr handle)
		{
			if (!_active || !_windows.ContainsKey(handle)) return;

			var window = _windows[handle];
			HandleWindowMoveEnd();
			WindowUpdated?.Invoke(window, WindowUpdateType.MoveEnd);
		}

		private void WindowMove(IntPtr handle)
		{
			if (!_active) return;

			if (_mouseMoveWindow != null && _windows.ContainsKey(handle))
			{
				var window = _windows[handle];
				if (_mouseMoveWindow == window)
					WindowUpdated?.Invoke(window, WindowUpdateType.Move);
			}
		}

		private void HandleWindowFocused(IWindow window) { if (_active) WindowFocused?.Invoke(window); }
		private void HandleWindowUpdated(IWindow window) { if (_active) ExternalWindowUpdate?.Invoke(window); }
		private void HandleWindowClosed(IWindow window) { if (_active) ExternalWindowClosed?.Invoke(window); }

		private void HandleWindowMoveStart(WindowsWindow window)
		{
			if (!_active) return;

			if (_mouseMoveWindow != null)
				_mouseMoveWindow.IsMouseMoving = false;

			_mouseMoveWindow = window;
			window.IsMouseMoving = true;
		}

		private void HandleWindowMoveEnd()
		{
			if (!_active) return;

			lock (_mouseMoveLock)
			{
				if (_mouseMoveWindow != null)
				{
					var window = _mouseMoveWindow;
					_mouseMoveWindow = null;
					window.IsMouseMoving = false;
					WindowMoved?.Invoke(window, EventArgs.Empty);
				}
			}
		}

		private void HandleWindowAdd(IWindow window, bool firstCreate) { if (_active) WindowCreated?.Invoke(window, firstCreate); }
		private void HandleWindowRemove(IWindow window) { if (_active) WindowDestroyed?.Invoke(window); }
	}
}

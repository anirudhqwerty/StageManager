// Win32.Extensions.cs
// New P/Invoke declarations needed by the fixed code.
// Add this file to the project alongside the existing Win32 partial class files.

using System;
using System.Runtime.InteropServices;

namespace StageManager.Native.PInvoke
{
	public static partial class Win32
	{
		/// <summary>
		/// Removes an event hook function created by a previous call to SetWinEventHook.
		/// This was MISSING from the codebase — without it, WindowsManager.Stop() couldn't
		/// actually unregister any of the hooks it had set up.
		/// </summary>
		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

		/// <summary>
		/// Enumerates child windows — needed to resolve UWP apps hosted in ApplicationFrameHost.
		/// </summary>
		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool EnumChildWindows(IntPtr hwndParent, EnumDelegate lpEnumFunc, IntPtr lParam);
	}
}

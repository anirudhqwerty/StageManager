using StageManager.Native.PInvoke;
using StageManager.Native.Window;

namespace StageManager.Strategies
{
	/// <summary>
	/// Hides windows by minimizing them and shows them by restoring.
	/// 
	/// KEY FIX: The original code used SW_MINIMIZE (6) to hide, which causes Windows to activate
	/// the next window in Z-order, firing EVENT_SYSTEM_FOREGROUND. This triggered another
	/// SwitchTo call mid-transition, creating a feedback loop. We now use SW_SHOWMINNOACTIVE (7)
	/// which minimizes WITHOUT activating any other window.
	///
	/// For Show: SW_SHOWNOACTIVATE doesn't restore a minimized window. We use SW_RESTORE for 
	/// minimized windows so they actually come back visible.
	/// </summary>
	internal class NormalizeAndMinimizeWindowStrategy : IWindowStrategy
	{
		public void Show(IWindow window)
		{
			// SW_SHOWNOACTIVATE (4) does NOT restore a minimized window - it keeps it minimized.
			// We must use SW_RESTORE for minimized windows so they actually appear.
			// SW_RESTORE also doesn't steal focus from the currently active window.
			if (window.IsMinimized)
				Win32.ShowWindow(window.Handle, Win32.SW.SW_RESTORE);
			else
				Win32.ShowWindow(window.Handle, Win32.SW.SW_SHOWNOACTIVATE);
		}

		public void Hide(IWindow window)
		{
			// SW_SHOWMINNOACTIVE (7): minimizes the window WITHOUT activating any other window.
			// The original SW_MINIMIZE (6) would activate the next window in Z-order,
			// which fired EVENT_SYSTEM_FOREGROUND and caused unwanted scene switching mid-transition.
			Win32.ShowWindow(window.Handle, Win32.SW.SW_SHOWMINNOACTIVE);
		}
	}
}

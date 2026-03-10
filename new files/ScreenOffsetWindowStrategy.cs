using StageManager.Native.PInvoke;
using StageManager.Native.Window;
using System.Collections.Concurrent;
using System.Windows.Forms;

namespace StageManager.Strategies
{
	/// <summary>
	/// Hides windows by moving them far off-screen and restores them to their original
	/// position when shown. This keeps windows "alive" (not minimized/hidden) which
	/// can help with apps that misbehave when minimized (e.g. screen-capture tools,
	/// floating toolbars).
	///
	/// Trade-offs vs NormalizeAndMinimize:
	///   + Windows remain in their normal rendering state (useful for captures/overlays)
	///   + No minimize animation flash
	///   - Windows are technically still "visible" to other apps on their virtual position
	///   - Uses more GPU/memory than truly hidden windows
	/// </summary>
	internal class ScreenOffsetWindowStrategy : IWindowStrategy
	{
		// Large enough offset to push any window fully off even 8K screens
		private const int OFF_SCREEN_OFFSET = 32000;

		// Map handle → original position so we can restore exactly
		private readonly ConcurrentDictionary<nint, (int x, int y)> _originalPositions = new();

		public void Show(IWindow window)
		{
			if (_originalPositions.TryRemove(window.Handle, out var original))
			{
				Win32.SetWindowPos(
					window.Handle,
					nint.Zero,
					original.x,
					original.y,
					0, 0,
					Win32.SetWindowPosFlags.IgnoreResize |
					Win32.SetWindowPosFlags.IgnoreZOrder |
					Win32.SetWindowPosFlags.DoNotActivate);
			}
			// If we don't have a stored position, just make it visible where it is
		}

		public void Hide(IWindow window)
		{
			var loc = window.Location;
			if (loc.State == WindowState.Minimized || loc.State == WindowState.Maximized)
			{
				// Don't try to offset minimized/maximized windows - minimize instead
				Win32.ShowWindow(window.Handle, Win32.SW.SW_SHOWMINNOACTIVE);
				return;
			}

			// Store current position before moving off-screen
			_originalPositions[window.Handle] = (loc.X, loc.Y);

			// Move to far off-screen right (positive X avoids issues with multi-monitor setups on the left)
			var screenWidth = SystemInformation.VirtualScreen.Width;
			Win32.SetWindowPos(
				window.Handle,
				nint.Zero,
				screenWidth + OFF_SCREEN_OFFSET,
				loc.Y,
				0, 0,
				Win32.SetWindowPosFlags.IgnoreResize |
				Win32.SetWindowPosFlags.IgnoreZOrder |
				Win32.SetWindowPosFlags.DoNotActivate);
		}
	}
}

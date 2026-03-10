using StageManager.Native.PInvoke;
using StageManager.Native.Window;
using System.Collections.Concurrent;
using System.Windows.Forms;

namespace StageManager.Strategies
{
	internal class ScreenOffsetWindowStrategy : IWindowStrategy
	{
		private const int OFF_SCREEN_OFFSET = 32000;
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
		}

		public void Hide(IWindow window)
		{
		}
	}
}

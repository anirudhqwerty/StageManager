using StageManager.Native.PInvoke;
using StageManager.Native.Window;

namespace StageManager.Strategies
{
	internal class NormalizeAndMinimizeWindowStrategy : IWindowStrategy
	{
		public void Show(IWindow window)
		{
			window.BringToTop();
		}

		public void Hide(IWindow window)
		{
		}
	}
}

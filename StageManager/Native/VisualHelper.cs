using ControlzEx.Standard;
using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace StageManager.Native
{
	public static class VisualHelper
	{
		public static Size GetMonitorWorkSize(this Visual visual)
		{
			if (visual != null)
			{
				var hwndSource = PresentationSource.FromVisual(visual) as HwndSource;
				if (hwndSource != null && !hwndSource.IsDisposed && hwndSource.RootVisual != null && hwndSource.Handle != IntPtr.Zero)
				{
					IntPtr intPtr = NativeMethods.MonitorFromWindow(hwndSource.Handle, MonitorOptions.MONITOR_DEFAULTTONEAREST);
					if (intPtr != IntPtr.Zero)
					{
						var monitorInfoW = NativeMethods.GetMonitorInfoW(intPtr);
						return new Size(monitorInfoW.rcWork.Width, monitorInfoW.rcWork.Height);
					}
				}
			}

			return default;
		}

		public static DependencyObject? GetParentObject(this DependencyObject? child)
		{
			if (child is null)
				return null;

			if (child is ContentElement contentElement)
			{
				DependencyObject parent = ContentOperations.GetParent(contentElement);
				if (parent is not null)
					return parent;

				return contentElement is FrameworkContentElement fce ? fce.Parent : null;
			}

			var childParent = VisualTreeHelper.GetParent(child);
			if (childParent is not null)
				return childParent;

			if (child is FrameworkElement frameworkElement)
			{
				DependencyObject parent = frameworkElement.Parent;
				if (parent is not null)
					return parent;
			}

			return null;
		}
	}
}

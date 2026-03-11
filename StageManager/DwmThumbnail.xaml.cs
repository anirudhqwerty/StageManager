using StageManager.Native.Interop;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StageManager
{
	public partial class DwmThumbnail : UserControl
	{
		public DwmThumbnail()
		{
			InitializeComponent();
			CacheMode = new BitmapCache();
			LayoutUpdated += DwmThumbnail_LayoutUpdated;
			Unloaded += DwmThumbnail_Unloaded;
		}

		private IntPtr _dwmThumbnail;
		private Window? _window;
		private Point? _dpiScaleFactor;

		public static readonly DependencyProperty PreviewHandleProperty = DependencyProperty.Register(nameof(PreviewHandle),
			   typeof(IntPtr),
			   typeof(DwmThumbnail),
			   new PropertyMetadata(IntPtr.Zero));

		public IntPtr PreviewHandle
		{
			get { return (IntPtr)GetValue(PreviewHandleProperty); }
			set { SetValue(PreviewHandleProperty, value); }
		}

		private Point GetDpiScaleFactor()
		{
			if (_dpiScaleFactor is null)
			{
				var source = PresentationSource.FromVisual(this);
				_dpiScaleFactor = source?.CompositionTarget != null
					? new Point(source.CompositionTarget.TransformToDevice.M11, source.CompositionTarget.TransformToDevice.M22)
					: new Point(1.0d, 1.0d);
			}

			return _dpiScaleFactor.Value;
		}

		protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
		{
			_dpiScaleFactor = null;
			base.OnDpiChanged(oldDpi, newDpi);
		}

		protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
		{
			base.OnPropertyChanged(e);

			if (nameof(PreviewHandle).Equals(e.Property.Name))
			{
				var oldHandle = (IntPtr)e.OldValue;
				var newHandle = (IntPtr)e.NewValue;

				if (newHandle != IntPtr.Zero)
				{
					if (oldHandle != IntPtr.Zero)
						UnregisterThumbnail();

					StartCapture();
				}
				else if (oldHandle != IntPtr.Zero)
				{
					UnregisterThumbnail();
				}

				UpdateThumbnailProperties();
			}

			if (nameof(IsVisible).Equals(e.Property.Name) && !(bool)e.NewValue && _dwmThumbnail != IntPtr.Zero)
			{
				UnregisterThumbnail();
			}
		}

		private void DwmThumbnail_LayoutUpdated(object? sender, EventArgs e)
		{
			UpdateThumbnailProperties();
		}

		private void DwmThumbnail_Unloaded(object? sender, RoutedEventArgs e)
		{
			UnregisterThumbnail();
		}

		private void UnregisterThumbnail()
		{
			if (_dwmThumbnail != IntPtr.Zero)
			{
				NativeMethods.DwmUnregisterThumbnail(_dwmThumbnail);
				_dwmThumbnail = IntPtr.Zero;
			}
		}

		public static Rect BoundsRelativeTo(FrameworkElement element, Visual relativeTo)
		{
			return element.TransformToVisual(relativeTo)
						  .TransformBounds(System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(element));
		}

		private void StartCapture()
		{
			var window = FindWindow();
			if (window is null) return;

			var windowHandle = new System.Windows.Interop.WindowInteropHelper(window).Handle;

			var hr = NativeMethods.DwmRegisterThumbnail(windowHandle, PreviewHandle, out _dwmThumbnail);
			if (hr != 0)
				return;
		}

		private Window? FindWindow() => _window ??= Window.GetWindow(this);

		private void UpdateThumbnailProperties()
		{
			if (_dwmThumbnail == IntPtr.Zero)
				return;

			var window = FindWindow();
			if (window is null) return;

			var dpi = GetDpiScaleFactor();

			var topLeft = TransformToVisual(window).Transform(new Point(0, 0));
			var bottomRight = TransformToVisual(window).Transform(new Point(ActualWidth, ActualHeight));

			var thumbnailRect = new RECT
			{
				top = (int)Math.Round(topLeft.Y * dpi.Y),
				left = (int)Math.Round(topLeft.X * dpi.X),
				bottom = (int)Math.Round(bottomRight.Y * dpi.Y),
				right = (int)Math.Round(bottomRight.X * dpi.X)
			};

			var props = new DWM_THUMBNAIL_PROPERTIES
			{
				fVisible = true,
				dwFlags = (int)(DWM_TNP.DWM_TNP_VISIBLE | DWM_TNP.DWM_TNP_OPACITY | DWM_TNP.DWM_TNP_RECTDESTINATION | DWM_TNP.DWM_TNP_SOURCECLIENTAREAONLY),
				opacity = 255,
				rcDestination = thumbnailRect,
				fSourceClientAreaOnly = true
			};
			NativeMethods.DwmUpdateThumbnailProperties(_dwmThumbnail, ref props);
		}
	}
}

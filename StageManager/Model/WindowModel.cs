using StageManager.Native;
using StageManager.Native.Window;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StageManager.Model
{
	[System.Diagnostics.DebuggerDisplay("{Title}")]
	public class WindowModel : INotifyPropertyChanged
	{
		[DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool DeleteObject([In] IntPtr hObject);

		private IWindow _window = null!;
		private ImageSource? _iconSource;

		public event PropertyChangedEventHandler? PropertyChanged;

		public WindowModel(IWindow window)
		{
			Window = window ?? throw new ArgumentNullException(nameof(window));
		}

		private void RaisePropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(memberName));
		}

		public string Title => _window.Title.Length > 20 ? _window.Title.Substring(0, 17) + " ..." : _window.Title;

		public static ImageSource? IconToImageSource(System.Drawing.Icon? icon)
		{
			if (icon is null)
				return null;

			var imageSource = Imaging.CreateBitmapSourceFromHIcon(
				icon.Handle,
				Int32Rect.Empty,
				BitmapSizeOptions.FromEmptyOptions());

			return imageSource;
		}

		public ImageSource? Icon => _iconSource ??= IconToImageSource((Window as WindowsWindow)?.ExtractIcon());

		public IWindow Window
		{
			get => _window;
			set
			{
				_window = value;
				RaisePropertyChanged();
				RaisePropertyChanged(nameof(Title));
				RaisePropertyChanged(nameof(Handle));
			}
		}

		public IntPtr Handle => _window?.Handle ?? IntPtr.Zero;
	}
}

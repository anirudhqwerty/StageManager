using AsyncAwaitBestPractices;
using Microsoft.Xaml.Behaviors.Core;
using SharpHook;
using StageManager.Model;
using StageManager.Native;
using StageManager.Native.PInvoke;
using StageManager.Native.Interop;
using StageManager.Native.Window;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StageManager
{
	public partial class MainWindow : Window
	{
		private const int MAX_SCENES = 6;
		private const string APP_NAME = "StageManager";
		private IntPtr _thisHandle;
		private TaskPoolGlobalHook? _hook;
		private volatile bool _hookPaused;
		private WindowMode _mode;
		private double _lastWidth;
		private Point _mouse = new Point(0, 0);
		private SceneModel? _removedCurrentScene;
		private SceneModel? _mouseDownScene;
		private WorkflowPanel? _workflowPanel;

		// Cached values for the hook thread
		private int _cachedPhysicalScreenWidth;
		private int _cachedPhysicalScreenHeight;
		private double _cachedPanelWidth = 360;
		private double _cachedPanelHeight = 660;
		private double _cachedPanelTop = 50;

		public bool EnableWindowDropToScene = false;
		public bool EnableWindowPullToScene = true;

		public MainWindow()
		{
			InitializeComponent();
			DataContext = this;
			SwitchSceneCommand = new ActionCommand(async model =>
			{
				var sceneModel = (SceneModel)model;
				var isCtrlHeld = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

				if (isCtrlHeld)
					await SceneManager!.ToggleSplit(sceneModel.Scene!);
				else
					await SceneManager!.SwitchTo(sceneModel.Scene);
			});
		}

		protected override void OnInitialized(EventArgs e)
		{
			base.OnInitialized(e);
			_thisHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
			_lastWidth = Width;
			Mode = WindowMode.OffScreen;

			// Cache physical screen dimensions once at startup
			var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
			_cachedPhysicalScreenWidth = primaryScreen.Bounds.Width;
			_cachedPhysicalScreenHeight = primaryScreen.Bounds.Height;

			ApplyMicaBackdrop();
			StartHook();
		}

		private void ApplyMicaBackdrop()
		{
			try
			{
				int darkMode = 1;
				NativeMethods.DwmSetWindowAttribute(_thisHandle,
					NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

				int backdropType = 3;
				NativeMethods.DwmSetWindowAttribute(_thisHandle,
					NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
			}
			catch { }
		}

		protected override void OnClosed(EventArgs e)
		{
			StopHook();
			trayIcon.Dispose();
			SceneManager?.Stop();
			base.OnClosed(e);
			Environment.Exit(0);
		}

		protected override async void OnContentRendered(EventArgs e)
		{
			base.OnContentRendered(e);
			_thisHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;

			// Instantiate the workflow panel
			_workflowPanel = new WorkflowPanel();
			_workflowPanel.Show();
			_cachedPanelWidth = _workflowPanel.Width;
			_cachedPanelHeight = _workflowPanel.Height;
			_cachedPanelTop = _workflowPanel.Top;

			var windowsManager = new WindowsManager();
			SceneManager = new SceneManager(windowsManager);
			await SceneManager.Start().ConfigureAwait(true);

			SceneManager.SceneChanged += SceneManager_SceneChanged;
			SceneManager.CurrentSceneSelectionChanged += SceneManager_CurrentSceneSelectionChanged;

			AddInitialScenes();

			var foreground = Win32.GetForegroundWindow();
			var foregroundScene = SceneManager.FindSceneForWindow(foreground);
			if (foregroundScene is object)
				await SceneManager.SwitchTo(foregroundScene).ConfigureAwait(true);
		}

		private void AddInitialScenes()
		{
			var initialScenes = SceneManager!.GetScenes().ToArray();
			for (int i = 0; i < initialScenes.Length; i++)
			{
				var model = SceneModel.FromScene(initialScenes[i]);
				model.IsVisible = i <= MAX_SCENES;
				Scenes.Add(model);
			}
		}

		private void SceneManager_CurrentSceneSelectionChanged(object? sender, CurrentSceneSelectionChangedEventArgs args)
		{
			var currentModel = args.Current is null ? null : Scenes.FirstOrDefault(m => m.Id == args.Current.Id);

			if (currentModel is object)
			{
				var currentIndex = Scenes.IndexOf(currentModel);
				Scenes.RemoveAt(currentIndex);

				if (_removedCurrentScene is object)
					Scenes.Insert(currentIndex, _removedCurrentScene);
			}
			else
			{
				if (_removedCurrentScene is object)
					Scenes.Add(_removedCurrentScene);
			}

			_removedCurrentScene = currentModel;
			SyncVisibilityByUpdatedTimeStamp();
		}

		protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
		{
			base.OnRenderSizeChanged(sizeInfo);
			var area = this.GetMonitorWorkSize();
			this.Left = 0;
			this.Top = 0;
			this.Height = area.Height;
		}

		private void SceneManager_SceneChanged(object? sender, SceneChangedEventArgs e)
		{
			this.Dispatcher.BeginInvoke(() =>
			{
				switch (e.Change)
				{
					case ChangeType.Created:
						Scenes.Add(SceneModel.FromScene(e.Scene));
						SyncVisibilityByUpdatedTimeStamp();
						break;
					case ChangeType.Updated:
						if (AllScenes.FirstOrDefault(s => s.Id == e.Scene.Id) is SceneModel toUpdate)
							toUpdate.UpdateFromScene(e.Scene);
						break;
					case ChangeType.Removed:
						if (AllScenes.FirstOrDefault(s => s.Id == e.Scene.Id) is SceneModel toRemove)
						{
							if (toRemove.Equals(_removedCurrentScene))
								_removedCurrentScene = null;
							else
								Scenes.Remove(toRemove);
						}
						SyncVisibilityByUpdatedTimeStamp();
						break;
				}
			});
		}

		private void OnMousePressed(object? sender, MouseHookEventArgs e)
		{
			if (_hookPaused) return;

			var foregroundWindow = Win32.GetForegroundWindow();
			if (foregroundWindow != _thisHandle)
				return;

			if (EnableWindowPullToScene)
			{
				var screenPoint = new Point(e.Data.X, e.Data.Y);
				this.Dispatcher.BeginInvoke(() =>
				{
					_mouseDownScene = FindSceneByPoint(screenPoint);
				});
			}
		}

		private void OnMouseReleased(object? sender, MouseHookEventArgs e)
		{
			if (_hookPaused) return;

			if (EnableWindowDropToScene)
			{
				var foregroundWindow = Win32.GetForegroundWindow();

				if (foregroundWindow == _thisHandle)
					return;

				var screenPoint = new Point(e.Data.X, e.Data.Y);
				this.Dispatcher.BeginInvoke(() =>
				{
					var sceneModel = FindSceneByPoint(screenPoint);
					if (sceneModel is object)
						SceneManager?.MoveWindow(foregroundWindow, sceneModel.Scene!).SafeFireAndForget();
				});
			}

			if (EnableWindowPullToScene)
			{
				if (e.Data.X > _lastWidth && _mouseDownScene is object)
				{
					this.Dispatcher.BeginInvoke(() =>
					{
						SceneManager?.PopWindowFrom(_mouseDownScene.Scene!).SafeFireAndForget();
					});
				}
			}
		}

		private SceneModel? FindSceneByPoint(Point p)
		{
			var thisWindow = new WindowsWindow(_thisHandle);
			var pointOnWindow = new Point(p.X - thisWindow.Location.X, p.Y - thisWindow.Location.Y);

			var dpi = VisualTreeHelper.GetDpi(this);
			pointOnWindow.X /= dpi.DpiScaleX;
			pointOnWindow.Y /= dpi.DpiScaleY;

			SceneModel? model = null;
			var element = VisualTreeHelper.HitTest(this, pointOnWindow)?.VisualHit;

			while (element is not null)
			{
				if (element is FrameworkElement { DataContext: SceneModel m })
				{
					model = m;
					break;
				}
				element = element.GetParentObject();
			}

			return model;
		}

		private void SyncVisibilityByUpdatedTimeStamp()
		{
			var scenes = Scenes.OrderByDescending(s => s.Updated).ToArray();
			for (int i = 0; i < scenes.Length; i++)
				scenes[i].IsVisible = i < MAX_SCENES;
		}

		public ObservableCollection<SceneModel> Scenes { get; } = new ObservableCollection<SceneModel>();

		public IEnumerable<SceneModel> AllScenes => Scenes.Union(new[] { _removedCurrentScene! }).Where(s => s is not null);

		public ICommand SwitchSceneCommand { get; }

		public SceneManager? SceneManager { get; private set; }

		public IntPtr Handle => _thisHandle;

		public WindowMode Mode
		{
			get => _mode;
			set
			{
				if (value == _mode)
					return;

				_mode = value;
				this.Topmost = value == WindowMode.Flyover;
				ApplyWindowMode();
			}
		}

		private void ApplyWindowMode()
		{
			var newLeft = Mode == StageManager.WindowMode.OffScreen ? (-1 * Width) : 0.0;
			if (Left == newLeft)
				return;

			var isIncoming = newLeft > Left;
			var easingMode = isIncoming ? EasingMode.EaseOut : EasingMode.EaseIn;

			var animation = new DoubleAnimationUsingKeyFrames();
			animation.Duration = new Duration(TimeSpan.FromMilliseconds(220));
			var easingFunction = new CircleEase { EasingMode = easingMode };
			animation.KeyFrames.Add(new EasingDoubleKeyFrame(Left, KeyTime.FromPercent(0)));
			animation.KeyFrames.Add(new EasingDoubleKeyFrame(newLeft, KeyTime.FromPercent(1.0), easingFunction));

			BeginAnimation(LeftProperty, animation);
		}

		private void StartHook()
		{
			if (_hook is not null) return;

			_hook = new TaskPoolGlobalHook();
			_hook.MousePressed += OnMousePressed;
			_hook.MouseReleased += OnMouseReleased;
			_hook.MouseMoved += _hook_MouseMoved;
			Task.Run(_hook.Run);
		}

		private void StopHook()
		{
			if (_hook is null) return;

			_hook.MousePressed -= OnMousePressed;
			_hook.MouseReleased -= OnMouseReleased;
			_hook.MouseMoved -= _hook_MouseMoved;

			try
			{
				_hook.Dispose();
			}
			catch (HookException) { }

			_hook = null;
		}

		private void _hook_MouseMoved(object? sender, MouseHookEventArgs e)
		{
			if (_hookPaused) return;

			_mouse.X = e.Data.X;
			_mouse.Y = e.Data.Y;

			// --- LEFT CENTER (StageManager) ---
			var sh = _cachedPhysicalScreenHeight;
			bool isLeftCenter = e.Data.X <= 1 && e.Data.Y > sh / 3 && e.Data.Y < 2 * sh / 3;

			if (Mode == WindowMode.OffScreen && isLeftCenter)
			{
				Dispatcher.BeginInvoke(() => Mode = WindowMode.Flyover);
			}
			else if (Mode == WindowMode.Flyover && e.Data.X > _lastWidth + 40)
			{
				Dispatcher.BeginInvoke(() => Mode = WindowMode.OffScreen);
			}

			// --- RIGHT EDGE (Workflow Panel) ---
			if (_workflowPanel is null) return;

			var sw = _cachedPhysicalScreenWidth;
			bool isRightEdge = e.Data.X >= sw - 1;

			// Open panel
			if (isRightEdge && _workflowPanel.Mode == WindowMode.OffScreen)
			{
				Dispatcher.BeginInvoke(() => _workflowPanel.Mode = WindowMode.Flyover);
			}

			// Dismiss when mouse leaves the panel bounding box
			if (_workflowPanel.Mode == WindowMode.Flyover)
			{
				var panelLeft = sw - _cachedPanelWidth;
				if (e.Data.X < panelLeft - 50 ||
					e.Data.Y < _cachedPanelTop - 30 ||
					e.Data.Y > _cachedPanelTop + _cachedPanelHeight + 30)
				{
					Dispatcher.BeginInvoke(() => _workflowPanel.Mode = WindowMode.OffScreen);
				}
			}
		}

		private void NavigateToProjectPage()
		{
			Process.Start(new ProcessStartInfo("https://github.com/awaescher/StageManager")
			{
				UseShellExecute = true
			});
		}

		public static bool StartsWithWindows
		{
			get => AutoStart.IsStartup(APP_NAME);
			set => AutoStart.SetStartup(APP_NAME, value);
		}

		private void MenuItem_ProjectPage_Click(object sender, RoutedEventArgs e) => NavigateToProjectPage();

		private void MenuItem_Quit_Click(object sender, RoutedEventArgs e) => Close();

		private void ContextMenu_Closed(object sender, RoutedEventArgs e) => _hookPaused = false;

		private void ContextMenu_Opened(object sender, RoutedEventArgs e) => _hookPaused = true;
	}

	public enum WindowMode
	{
		OnScreen,
		OffScreen,
		Flyover
	}
}

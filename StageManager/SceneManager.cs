using AsyncAwaitBestPractices;
using StageManager.Native;
using StageManager.Native.PInvoke;
using StageManager.Native.Window;
using StageManager.Strategies;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StageManager
{
	public class SceneManager
	{
		private readonly Desktop _desktop;
		private List<Scene> _scenes = new();
		private Scene? _current;
		private volatile bool _suspend = false;
		private Guid? _reentrancyLockSceneId;
		private CancellationTokenSource? _reentrancyCts;

		public event EventHandler<SceneChangedEventArgs>? SceneChanged;
		public event EventHandler<CurrentSceneSelectionChangedEventArgs>? CurrentSceneSelectionChanged;

		private IWindowStrategy WindowStrategy { get; } = new NormalizeAndMinimizeWindowStrategy();

		public WindowsManager WindowsManager { get; }

		public SceneManager(WindowsManager windowsManager)
		{
			WindowsManager = windowsManager ?? throw new ArgumentNullException(nameof(windowsManager));
			_desktop = new Desktop();
		}

		public async Task Start()
		{
			if (Thread.CurrentThread.ManagedThreadId != 1)
				throw new NotSupportedException("Start must be called on the main thread.");

			WindowsManager.WindowCreated += WindowsManager_WindowCreated;
			WindowsManager.WindowUpdated += WindowsManager_WindowUpdated;
			WindowsManager.WindowDestroyed += WindowsManager_WindowDestroyed;
			WindowsManager.UntrackedFocus += WindowsManager_UntrackedFocus;

			await WindowsManager.Start();
		}

		public void Stop()
		{
			WindowsManager.Stop();
		}

		private void WindowsManager_WindowUpdated(IWindow window, WindowUpdateType type)
		{
			if (_suspend) return;

			if (type == WindowUpdateType.Foreground)
				SwitchToSceneByWindow(window).SafeFireAndForget();
		}

		private void WindowsManager_UntrackedFocus(object? sender, IntPtr e)
		{
			if (_suspend) return;

			if (!_desktop.HasDesktopView)
				_desktop.TrySetDesktopView(e);

			if (_desktop.HasDesktopView && _desktop.DesktopViewHandle == e)
				SwitchTo(null).SafeFireAndForget();
		}

		private void WindowsManager_WindowDestroyed(IWindow window)
		{
			var scene = FindSceneForWindow(window);
			if (scene is null) return;

			scene.Remove(window);

			if (scene.Windows.Any())
			{
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Updated));
			}
			else
			{
				_scenes.Remove(scene);
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Removed));
			}
		}

		public Scene? FindSceneForWindow(IWindow window) => FindSceneForWindow(window.Handle);

		public Scene? FindSceneForWindow(IntPtr handle)
			=> _scenes.FirstOrDefault(s => s.Windows.Any(w => w.Handle == handle));

		private void WindowsManager_WindowCreated(IWindow window, bool firstCreate)
		{
			SwitchToSceneByNewWindow(window).SafeFireAndForget();
		}

		private async Task SwitchToSceneByWindow(IWindow window)
		{
			var scene = FindSceneForWindow(window);
			if (scene is null)
			{
				scene = new Scene(GetWindowGroupKey(window), window);
				_scenes.Add(scene);
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Created));
			}

			await SwitchTo(scene);
		}

		private async Task SwitchToSceneByNewWindow(IWindow window)
		{
			var scene = new Scene(GetWindowGroupKey(window), window);
			_scenes.Add(scene);
			SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Created));

			await SwitchTo(scene).ConfigureAwait(true);
		}

		private bool IsReentrancy(Scene? scene)
		{
			if (scene is null) return false;
			if (Guid.Equals(scene.Id, _reentrancyLockSceneId)) return true;

			if (_current is not null)
			{
				_reentrancyCts?.Cancel();
				_reentrancyCts = new CancellationTokenSource();

				_reentrancyLockSceneId = _current.Id;
				var token = _reentrancyCts.Token;

				Task.Run(async () =>
				{
					try
					{
						await Task.Delay(1000, token).ConfigureAwait(false);
						_reentrancyLockSceneId = null;
					}
					catch (OperationCanceledException) { }
				}).SafeFireAndForget();
			}

			return false;
		}

		public async Task SwitchTo(Scene? scene)
		{
			if (object.Equals(scene, _current)) return;
			if (IsReentrancy(scene)) return;

			try
			{
				_suspend = true;

				var prior = _current;
				_current = scene;

				foreach (var s in _scenes)
					s.IsSelected = s.Equals(scene);

				if (scene is not null)
				{
					var windowToFocus = scene.Windows.LastOrDefault();
					if (windowToFocus is not null)
					{
						await Task.Delay(30).ConfigureAwait(true);
						windowToFocus.BringToTop();
						windowToFocus.Focus();
					}
				}

				CurrentSceneSelectionChanged?.Invoke(this, new CurrentSceneSelectionChangedEventArgs(prior, _current));
			}
			finally
			{
				_suspend = false;
			}
		}

		public Task MoveWindow(Scene sourceScene, IWindow window, Scene targetScene)
		{
			try
			{
				_suspend = true;

				if (sourceScene is null || sourceScene.Equals(targetScene))
					return Task.CompletedTask;

				sourceScene.Remove(window);
				targetScene.Add(window);

				SceneChanged?.Invoke(this, new SceneChangedEventArgs(sourceScene, window, ChangeType.Updated));
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(targetScene, window, ChangeType.Updated));

				if (!sourceScene.Windows.Any())
				{
					_scenes.Remove(sourceScene);
					SceneChanged?.Invoke(this, new SceneChangedEventArgs(sourceScene, window, ChangeType.Removed));
				}

				if (targetScene.Equals(_current))
				{
					window.BringToTop();
					window.Focus();
				}

				return Task.CompletedTask;
			}
			finally
			{
				_suspend = false;
			}
		}

		public async Task MoveWindow(IntPtr handle, Scene targetScene)
		{
			var source = FindSceneForWindow(handle);
			if (source is null || source.Equals(targetScene)) return;

			var window = source.Windows.First(w => w.Handle == handle);
			await MoveWindow(source, window, targetScene);
		}

		public async Task PopWindowFrom(Scene sourceScene)
		{
			if (sourceScene is null || _current is null || sourceScene.Equals(_current)) return;

			var window = sourceScene.Windows.LastOrDefault();
			if (window is not null)
				await MoveWindow(sourceScene, window, _current).ConfigureAwait(false);
		}

		private IEnumerable<IWindow> GetSceneableWindows()
			=> WindowsManager?.Windows?.Where(w => !string.IsNullOrEmpty(w.ProcessFileName) && !string.IsNullOrEmpty(w.Title))
			   ?? Enumerable.Empty<IWindow>();

		public IEnumerable<Scene> GetScenes()
		{
			if (_scenes.Count == 0)
			{
				var windows = GetSceneableWindows()
					.Select(w => new Scene(GetWindowGroupKey(w), w))
					.ToList();

				_scenes.AddRange(windows);
			}

			return _scenes;
		}

		public IEnumerable<IWindow> GetCurrentWindows()
			=> _current?.Windows ?? GetSceneableWindows();

		private string GetWindowGroupKey(IWindow window) => window.Handle.ToString();
	}
}

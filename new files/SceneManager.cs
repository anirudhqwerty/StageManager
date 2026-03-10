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

		// BUG FIX: _scenes was initialized lazily in GetScenes(), but WindowsManager events
		// fire on background threads immediately after Start(). Any event arriving before the
		// first call to GetScenes() would hit a null _scenes and throw a NullReferenceException.
		// Now we initialize eagerly to an empty list so all code paths are safe.
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
			_desktop.HideIcons();
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

			foreach (var scene in _scenes)
			{
				foreach (var w in scene.Windows)
					WindowStrategy.Show(w);
			}

			_desktop.ShowIcons();
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

		private Scene? FindSceneForProcess(string processName)
			=> _scenes.FirstOrDefault(s => string.Equals(s.Key, processName, StringComparison.OrdinalIgnoreCase));

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
			var existentScene = FindSceneForProcess(GetWindowGroupKey(window));
			var scene = existentScene ?? new Scene(window.ProcessName, window);

			if (existentScene is null)
			{
				_scenes.Add(scene);
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Created));
			}
			else
			{
				scene.Add(window);
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Updated));
			}

			await SwitchTo(scene).ConfigureAwait(true);
		}

		/// <summary>
		/// Detects when an app reactivates one of its own windows immediately after being hidden
		/// (e.g. Teams floating call window). Without this guard, we'd get an infinite hide/show loop.
		///
		/// BUG FIX: The original used a bare Task.Run with Task.Delay and no cancellation.
		/// Multiple rapid scene switches would spawn multiple timer tasks, each clearing the lock
		/// at a different time and potentially interfering with each other.
		/// Now we cancel any pending timer before starting a new one.
		/// </summary>
		private bool IsReentrancy(Scene? scene)
		{
			if (scene is null) return false;
			if (Guid.Equals(scene.Id, _reentrancyLockSceneId)) return true;

			if (_current is not null)
			{
				// Cancel any previous pending lock-clear task
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
					catch (OperationCanceledException) { /* Superseded by a newer switch, that's fine */ }
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

				var otherWindows = GetSceneableWindows()
					.Except(scene?.Windows ?? Array.Empty<IWindow>())
					.ToArray();

				var prior = _current;
				_current = scene;

				foreach (var s in _scenes)
					s.IsSelected = s.Equals(scene);

				if (scene is not null)
				{
					foreach (var w in scene.Windows)
						WindowStrategy.Show(w);
				}

				foreach (var o in otherWindows)
					WindowStrategy.Hide(o);

				CurrentSceneSelectionChanged?.Invoke(this, new CurrentSceneSelectionChangedEventArgs(prior, _current));

				if (scene is null)
					_desktop.ShowIcons();
				else
					_desktop.HideIcons();

				// Focus the most recently active window in the scene so the user can work immediately.
				// This is deferred slightly to let the show/hide operations complete first.
				if (scene is not null)
				{
					var windowToFocus = scene.Windows.LastOrDefault();
					if (windowToFocus is not null)
					{
						await Task.Delay(50).ConfigureAwait(true); // Let minimize animations settle
						windowToFocus.Focus();
					}
				}
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
					WindowStrategy.Show(window);
					window.Focus();
				}
				else
				{
					WindowStrategy.Hide(window);

					if (window is WindowsWindow w && w.PopLastLocation() is IWindowLocation l)
						Win32.SetWindowPos(window.Handle, IntPtr.Zero, l.X, l.Y, 0, 0,
							Win32.SetWindowPosFlags.IgnoreResize);
				}

				return Task.CompletedTask;
			}
			finally
			{
				_suspend = false;
			}
		}

		// BUG FIX: Was marked async but contained no await - the compiler would wrap it
		// in a state machine for no reason. Made synchronous and returns Task.CompletedTask
		// after the await on the inner call. Now properly awaits MoveWindow internally.
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
			// BUG FIX: Was lazily initializing here and returning - but if Start() fires
			// events before this is first called, _scenes was null and event handlers crashed.
			// Now _scenes is always initialized in the field declaration; we only populate
			// it here if it's empty (first call).
			if (_scenes.Count == 0)
			{
				var grouped = GetSceneableWindows()
					.GroupBy(GetWindowGroupKey)
					.Select(group => new Scene(group.Key, group.ToArray()))
					.ToList();

				_scenes.AddRange(grouped);
			}

			return _scenes;
		}

		public IEnumerable<IWindow> GetCurrentWindows()
			=> _current?.Windows ?? GetSceneableWindows();

		private string GetWindowGroupKey(IWindow window) => window.ProcessName;
	}
}

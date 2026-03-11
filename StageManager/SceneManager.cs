using AsyncAwaitBestPractices;
using StageManager.Native;
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
		private readonly List<Scene> _scenes = new();
		private readonly List<IWindow> _splitWindows = new();
		private Scene? _current;

		private readonly SemaphoreSlim _transitionLock = new SemaphoreSlim(1, 1);
		private readonly object _stateLock = new object();
		private SynchronizationContext? _syncContext;

		public event EventHandler<SceneChangedEventArgs>? SceneChanged;
		public event EventHandler<CurrentSceneSelectionChangedEventArgs>? CurrentSceneSelectionChanged;

		private IWindowStrategy WindowStrategy { get; } = new NormalizeAndMinimizeWindowStrategy();

		public WindowsManager WindowsManager { get; }

		public bool IsSplitActive
		{
			get { lock (_stateLock) return _splitWindows.Count > 1; }
		}

		public SceneManager(WindowsManager windowsManager)
		{
			WindowsManager = windowsManager ?? throw new ArgumentNullException(nameof(windowsManager));
			_desktop = new Desktop();
		}

		public async Task Start()
		{
			if (Thread.CurrentThread.ManagedThreadId != 1)
				throw new NotSupportedException("Start must be called on the main thread.");

			_syncContext = SynchronizationContext.Current;

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
			if (_transitionLock.CurrentCount == 0) return;

			if (type == WindowUpdateType.Foreground)
				AutoSwitchToSceneByWindow(window);
		}

		private void AutoSwitchToSceneByWindow(IWindow window)
		{
			if (_transitionLock.CurrentCount == 0) return;

			Scene? scene;
			Scene? prior;
			SceneChangedEventArgs? createdArgs = null;
			CurrentSceneSelectionChangedEventArgs? selectionArgs = null;

			lock (_stateLock)
			{
				scene = FindSceneForWindow_NoLock(window.Handle);
				if (scene is null)
				{
					scene = new Scene(GetWindowGroupKey(window), window);
					_scenes.Add(scene);
					createdArgs = new SceneChangedEventArgs(scene, window, ChangeType.Created);
				}

				if (object.Equals(scene, _current))
					return;

				prior = _current;
				_current = scene;
				_splitWindows.Clear();

				foreach (var s in _scenes)
					s.IsSelected = s.Equals(scene);

				selectionArgs = new CurrentSceneSelectionChangedEventArgs(prior, _current);
			}

			RaiseOnCapturedContext(() =>
			{
				if (createdArgs is not null)
					SceneChanged?.Invoke(this, createdArgs);

				if (selectionArgs is not null)
					CurrentSceneSelectionChanged?.Invoke(this, selectionArgs);
			});
		}

		private void WindowsManager_UntrackedFocus(object? sender, IntPtr e)
		{
			if (_transitionLock.CurrentCount == 0) return;
		}

		private void WindowsManager_WindowDestroyed(IWindow window)
		{
			Scene? scene;
			List<SceneChangedEventArgs> sceneChangedArgs = new List<SceneChangedEventArgs>(capacity: 2);

			lock (_stateLock)
			{
				scene = FindSceneForWindow_NoLock(window.Handle);
				if (scene is null) return;

				_splitWindows.RemoveAll(w => w.Handle == window.Handle);

				scene.Remove(window);

				if (scene.Windows.Any())
				{
					sceneChangedArgs.Add(new SceneChangedEventArgs(scene, window, ChangeType.Updated));
				}
				else
				{
					_scenes.Remove(scene);
					sceneChangedArgs.Add(new SceneChangedEventArgs(scene, window, ChangeType.Removed));
				}
			}

			RaiseOnCapturedContext(() =>
			{
				foreach (var args in sceneChangedArgs)
					SceneChanged?.Invoke(this, args);
			});
		}

		public Scene? FindSceneForWindow(IWindow window) => FindSceneForWindow(window.Handle);

		public Scene? FindSceneForWindow(IntPtr handle)
		{
			lock (_stateLock)
			{
				return FindSceneForWindow_NoLock(handle);
			}
		}

		private Scene? FindSceneForWindow_NoLock(IntPtr handle)
			=> _scenes.FirstOrDefault(s => s.Windows.Any(w => w.Handle == handle));

		private void WindowsManager_WindowCreated(IWindow window, bool firstCreate)
		{
			Scene scene;
			lock (_stateLock)
			{
				scene = new Scene(GetWindowGroupKey(window), window);
				_scenes.Add(scene);
			}

			RaiseOnCapturedContext(() =>
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Created)));

			SwitchTo(scene).SafeFireAndForget();
		}

		public async Task SwitchTo(Scene? scene)
		{
			if (object.Equals(scene, _current)) return;

			await _transitionLock.WaitAsync();
			try
			{
				Scene? prior;
				IWindow? windowToFocus = null;
				CurrentSceneSelectionChangedEventArgs? selectionArgs = null;

				lock (_stateLock)
				{
					_splitWindows.Clear();

					prior = _current;
					_current = scene;

					foreach (var s in _scenes)
						s.IsSelected = s.Equals(scene);

					if (scene is not null)
						windowToFocus = scene.Windows.LastOrDefault();

					selectionArgs = new CurrentSceneSelectionChangedEventArgs(prior, _current);
				}

				if (windowToFocus is not null)
				{
					windowToFocus.BringToTop();
					windowToFocus.Focus();
				}

				RaiseOnCapturedContext(() =>
				{
					if (selectionArgs is not null)
						CurrentSceneSelectionChanged?.Invoke(this, selectionArgs);
				});
			}
			finally
			{
				await Task.Delay(100);
				_transitionLock.Release();
			}
		}

		public async Task ToggleSplit(Scene scene)
		{
			if (scene is null) return;

			await _transitionLock.WaitAsync();
			var lockHeld = true;
			try
			{
				List<IWindow> splitSnapshot;
				Scene? remainingSceneToSwitch = null;

				lock (_stateLock)
				{
					if (_splitWindows.Count == 0 && _current is not null)
						_splitWindows.AddRange(_current.Windows);

					var sceneWindow = scene.Windows.FirstOrDefault();
					if (sceneWindow is not null)
					{
						var existing = _splitWindows.FindIndex(sw => sw.Handle == sceneWindow.Handle);
						if (existing >= 0)
						{
							_splitWindows.RemoveAt(existing);
						}
						else
						{
							foreach (var w in scene.Windows)
							{
								if (!_splitWindows.Any(sw => sw.Handle == w.Handle))
									_splitWindows.Add(w);
							}
						}
					}

					splitSnapshot = _splitWindows.ToList();

					if (splitSnapshot.Count <= 1)
					{
						var remaining = splitSnapshot.FirstOrDefault();
						_splitWindows.Clear();

						if (remaining is not null)
						{
							remainingSceneToSwitch = FindSceneForWindow_NoLock(remaining.Handle);
						}
					}
				}

				if (remainingSceneToSwitch is not null)
				{
					_transitionLock.Release();
					lockHeld = false;
					await SwitchTo(remainingSceneToSwitch);
					return;
				}

				foreach (var w in splitSnapshot)
					w.BringToTop();

				var last = splitSnapshot.LastOrDefault();
				last?.Focus();

				WindowLayoutManager.SplitScreen(splitSnapshot);
			}
			finally
			{
				if (lockHeld)
				{
					await Task.Delay(100);
					_transitionLock.Release();
				}
			}
		}

		public async Task MoveWindow(Scene sourceScene, IWindow window, Scene targetScene)
		{
			await _transitionLock.WaitAsync();
			try
			{
				if (sourceScene is null || sourceScene.Equals(targetScene))
					return;

				List<SceneChangedEventArgs> sceneChangedArgs = new List<SceneChangedEventArgs>(capacity: 3);

				lock (_stateLock)
				{
					sourceScene.Remove(window);
					targetScene.Add(window);

					sceneChangedArgs.Add(new SceneChangedEventArgs(sourceScene, window, ChangeType.Updated));
					sceneChangedArgs.Add(new SceneChangedEventArgs(targetScene, window, ChangeType.Updated));

					if (!sourceScene.Windows.Any())
					{
						_scenes.Remove(sourceScene);
						sceneChangedArgs.Add(new SceneChangedEventArgs(sourceScene, window, ChangeType.Removed));
					}
				}

				RaiseOnCapturedContext(() =>
				{
					foreach (var args in sceneChangedArgs)
						SceneChanged?.Invoke(this, args);
				});

				if (targetScene.Equals(_current))
				{
					window.BringToTop();
					window.Focus();
				}
			}
			finally
			{
				_transitionLock.Release();
			}
		}

		public async Task MoveWindow(IntPtr handle, Scene targetScene)
		{
			var source = FindSceneForWindow(handle);
			if (source is null || source.Equals(targetScene)) return;

			var window = source.Windows.FirstOrDefault(w => w.Handle == handle);
			if (window != null)
				await MoveWindow(source, window, targetScene);
		}

		public async Task PopWindowFrom(Scene sourceScene)
		{
			if (sourceScene is null || _current is null || sourceScene.Equals(_current)) return;

			var window = sourceScene.Windows.LastOrDefault();
			if (window is not null)
				await MoveWindow(sourceScene, window, _current);
		}

		private IEnumerable<IWindow> GetSceneableWindows()
			=> WindowsManager?.Windows?.Where(w => !string.IsNullOrEmpty(w.ProcessFileName) && !string.IsNullOrEmpty(w.Title))
			   ?? Enumerable.Empty<IWindow>();

		public IEnumerable<Scene> GetScenes()
		{
			lock (_stateLock)
			{
				if (_scenes.Count == 0)
				{
					var windows = GetSceneableWindows()
						.Select(w => new Scene(GetWindowGroupKey(w), w))
						.ToList();

					_scenes.AddRange(windows);
				}

				return _scenes.ToList();
			}
		}

		public IEnumerable<IWindow> GetCurrentWindows()
		{
			lock (_stateLock)
			{
				if (_current?.Windows is not null)
					return _current.Windows.ToList();
			}

			return GetSceneableWindows().ToList();
		}

		private string GetWindowGroupKey(IWindow window) => window.Handle.ToString();

		private void RaiseOnCapturedContext(Action action)
		{
			var ctx = _syncContext;
			if (ctx is null || SynchronizationContext.Current == ctx)
			{
				action();
				return;
			}

			ctx.Post(_ => action(), null);
		}
	}
}

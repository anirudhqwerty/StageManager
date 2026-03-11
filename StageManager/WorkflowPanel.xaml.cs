using StageManager.Model;
using StageManager.Native.Interop;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace StageManager
{
    public partial class WorkflowPanel : Window
    {
        public ObservableCollection<TodoItem> Todos { get; set; } = new ObservableCollection<TodoItem>();

        private WindowMode _mode = WindowMode.OffScreen;
        private readonly string _todosPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "todos.txt");
        private readonly string _notesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "notes.txt");

        // Clock
        private DispatcherTimer _clockTimer;

        // Focus timer
        private DispatcherTimer _focusTimer;
        private TimeSpan _focusDuration = TimeSpan.FromMinutes(25);
        private TimeSpan _focusRemaining;
        private bool _focusRunning;

        public WorkflowPanel()
        {
            InitializeComponent();
            TodoListBox.ItemsSource = Todos;
            Todos.CollectionChanged += (_, __) => UpdateTaskCount();

            LoadTodos();
            LoadNotes();

            // Initialize clock
            UpdateClock();
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _clockTimer.Tick += (_, __) => UpdateClock();
            _clockTimer.Start();

            // Initialize focus timer
            _focusRemaining = _focusDuration;
            _focusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _focusTimer.Tick += FocusTimer_Tick;
            UpdateTimerDisplay();
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            ApplyMicaBackdrop();
        }

        // ── Mode & Animation ──────────────────────────────────────

        public WindowMode Mode
        {
            get => _mode;
            set
            {
                if (value == _mode) return;
                _mode = value;
                this.Topmost = value == WindowMode.Flyover;
                ApplyWindowMode();
                if (_mode == WindowMode.OffScreen) Save();
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            this.Top = 50;
            ApplyWindowMode();
        }

        private void ApplyWindowMode()
        {
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            this.Top = 50;
            var newLeft = Mode == WindowMode.OffScreen ? screenWidth : (screenWidth - Width);
            if (Left == newLeft) return;

            var easingMode = Mode == WindowMode.Flyover ? EasingMode.EaseOut : EasingMode.EaseIn;
            var animation = new DoubleAnimationUsingKeyFrames();
            animation.Duration = new Duration(TimeSpan.FromMilliseconds(250));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(Left, KeyTime.FromPercent(0)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(newLeft, KeyTime.FromPercent(1.0),
                new CircleEase { EasingMode = easingMode }));
            BeginAnimation(LeftProperty, animation);
        }

        private void ApplyMicaBackdrop()
        {
            try
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
                int darkMode = 1;
                NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
                int backdropType = 3;
                NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
            }
            catch { }
        }

        // ── Clock ─────────────────────────────────────────────────

        private void UpdateClock()
        {
            var now = DateTime.Now;
            DateText.Text = now.ToString("dddd, MMMM d");
            TimeText.Text = now.ToString("h:mm tt");
        }

        // ── Notes ─────────────────────────────────────────────────

        private void LoadNotes()
        {
            if (File.Exists(_notesPath))
                NotesTextBox.Text = File.ReadAllText(_notesPath);
        }

        // ── To-Do ─────────────────────────────────────────────────

        private void AddTask_Click(object sender, RoutedEventArgs e) => AddNewTask();

        private void NewTaskTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddNewTask();
        }

        private void AddNewTask()
        {
            if (!string.IsNullOrWhiteSpace(NewTaskTextBox.Text))
            {
                Todos.Add(new TodoItem { Task = NewTaskTextBox.Text });
                NewTaskTextBox.Clear();
            }
        }

        private void DeleteTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.DataContext is TodoItem item)
                Todos.Remove(item);
        }

        private void UpdateTaskCount()
        {
            var total = Todos.Count;
            var done = Todos.Count(t => t.IsCompleted);
            TaskCountText.Text = done > 0 ? $"{done}/{total} done" : $"{total} task{(total == 1 ? "" : "s")}";
        }

        // ── Focus Timer ───────────────────────────────────────────

        private void StartPause_Click(object sender, RoutedEventArgs e)
        {
            if (_focusRunning)
            {
                _focusTimer.Stop();
                _focusRunning = false;
                StartPauseBtn.Content = "Resume";
            }
            else
            {
                if (_focusRemaining.TotalSeconds <= 0)
                {
                    _focusRemaining = _focusDuration;
                }
                _focusTimer.Start();
                _focusRunning = true;
                StartPauseBtn.Content = "Pause";
            }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            _focusTimer.Stop();
            _focusRunning = false;
            _focusRemaining = _focusDuration;
            StartPauseBtn.Content = "Start";
            UpdateTimerDisplay();
        }

        private void FocusTimer_Tick(object? sender, EventArgs e)
        {
            _focusRemaining = _focusRemaining.Subtract(TimeSpan.FromSeconds(1));
            if (_focusRemaining.TotalSeconds <= 0)
            {
                _focusRemaining = TimeSpan.Zero;
                _focusTimer.Stop();
                _focusRunning = false;
                StartPauseBtn.Content = "Start";
                System.Media.SystemSounds.Asterisk.Play();

                // Auto-show the panel when timer completes
                Dispatcher.BeginInvoke(() =>
                {
                    if (Mode == WindowMode.OffScreen)
                        Mode = WindowMode.Flyover;
                });
            }
            UpdateTimerDisplay();
        }

        private void UpdateTimerDisplay()
        {
            TimerText.Text = _focusRemaining.ToString(@"mm\:ss");

            // Update progress bar
            if (TimerProgress.Parent is FrameworkElement parent && parent.ActualWidth > 0)
            {
                var progress = _focusRemaining.TotalSeconds / _focusDuration.TotalSeconds;
                TimerProgress.Width = parent.ActualWidth * progress;
            }
        }

        // ── Persistence ───────────────────────────────────────────

        private void LoadTodos()
        {
            if (File.Exists(_todosPath))
            {
                foreach (var line in File.ReadAllLines(_todosPath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("[x]"))
                        Todos.Add(new TodoItem { IsCompleted = true, Task = line.Substring(3) });
                    else
                        Todos.Add(new TodoItem { IsCompleted = false, Task = line });
                }
            }
        }

        private void Save()
        {
            // Save notes
            try { File.WriteAllText(_notesPath, NotesTextBox.Text); } catch { }

            // Save todos
            try
            {
                var lines = Todos.Select(t => (t.IsCompleted ? "[x]" : "") + t.Task);
                File.WriteAllLines(_todosPath, lines);
            }
            catch { }
        }
    }
}

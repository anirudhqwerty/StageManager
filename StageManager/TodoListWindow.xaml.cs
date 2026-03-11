using StageManager.Model;
using StageManager.Native.Interop;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace StageManager
{
    public partial class TodoListWindow : Window
    {
        public ObservableCollection<TodoItem> Todos { get; set; } = new ObservableCollection<TodoItem>();
        private WindowMode _mode = WindowMode.OffScreen;
        private string TodosFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "todos.txt");

        public TodoListWindow()
        {
            InitializeComponent();
            TodoListBox.ItemsSource = Todos;
            LoadTodos();
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            ApplyMicaBackdrop();
        }

        public WindowMode Mode
        {
            get => _mode;
            set
            {
                if (value == _mode) return;
                _mode = value;
                this.Topmost = value == WindowMode.Flyover;
                ApplyWindowMode();

                // Save when sliding away
                if (_mode == WindowMode.OffScreen) SaveTodos();
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            // Center vertically on screen
            this.Top = (SystemParameters.WorkArea.Height - this.Height) / 2;
            ApplyWindowMode();
        }

        private void ApplyWindowMode()
        {
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            // Center vertically
            this.Top = (SystemParameters.WorkArea.Height - this.Height) / 2;

            var newLeft = Mode == WindowMode.OffScreen ? screenWidth : (screenWidth - Width);

            if (Left == newLeft) return;

            var easingMode = Mode == WindowMode.Flyover ? EasingMode.EaseOut : EasingMode.EaseIn;
            var animation = new DoubleAnimationUsingKeyFrames();
            animation.Duration = new Duration(TimeSpan.FromMilliseconds(250));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(Left, KeyTime.FromPercent(0)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(newLeft, KeyTime.FromPercent(1.0), new CircleEase { EasingMode = easingMode }));

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

        private void AddButton_Click(object sender, RoutedEventArgs e) => AddNewTask();

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

        private void LoadTodos()
        {
            if (File.Exists(TodosFilePath))
            {
                foreach (var line in File.ReadAllLines(TodosFilePath))
                {
                    if (line.StartsWith("[x]")) Todos.Add(new TodoItem { IsCompleted = true, Task = line.Substring(3) });
                    else Todos.Add(new TodoItem { IsCompleted = false, Task = line });
                }
            }
        }

        private void SaveTodos()
        {
            var lines = Todos.Select(t => (t.IsCompleted ? "[x]" : "") + t.Task);
            File.WriteAllLines(TodosFilePath, lines);
        }
    }
}

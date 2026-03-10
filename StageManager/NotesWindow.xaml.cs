using StageManager.Native.Interop;
using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Animation;

namespace StageManager
{
    public partial class NotesWindow : Window
    {
        private WindowMode _mode = WindowMode.OffScreen;
        private string NotesFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "notes.txt");

        public NotesWindow()
        {
            InitializeComponent();
            if (File.Exists(NotesFilePath))
                NotesTextBox.Text = File.ReadAllText(NotesFilePath);
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

                // Save notes when sliding away
                if (_mode == WindowMode.OffScreen)
                {
                    File.WriteAllText(NotesFilePath, NotesTextBox.Text);
                }
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            this.Top = 0; // Fixes the window to the top right
            ApplyWindowMode();
        }

        private void ApplyWindowMode()
        {
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var newLeft = Mode == WindowMode.OffScreen ? screenWidth : (screenWidth - Width);

            if (Left == newLeft) return;

            var easingMode = Mode == WindowMode.Flyover ? EasingMode.EaseOut : EasingMode.EaseIn;
            var animation = new DoubleAnimationUsingKeyFrames();
            animation.Duration = new Duration(TimeSpan.FromMilliseconds(220));
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
    }
}

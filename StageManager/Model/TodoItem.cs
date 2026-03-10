using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StageManager.Model
{
    public class TodoItem : INotifyPropertyChanged
    {
        private string _task;
        private bool _isCompleted;

        public string Task
        {
            get => _task;
            set { _task = value; OnPropertyChanged(); }
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set { _isCompleted = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

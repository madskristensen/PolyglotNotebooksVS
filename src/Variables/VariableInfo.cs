using System.ComponentModel;

namespace PolyglotNotebooks.Variables
{
    /// <summary>
    /// Represents a single kernel variable displayed in the Variable Explorer.
    /// </summary>
    public sealed class VariableInfo : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _typeName = string.Empty;
        private string _value = string.Empty;
        private string _kernelName = string.Empty;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        public string TypeName
        {
            get => _typeName;
            set { _typeName = value; OnPropertyChanged(nameof(TypeName)); }
        }

        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(nameof(Value)); }
        }

        public string KernelName
        {
            get => _kernelName;
            set { _kernelName = value; OnPropertyChanged(nameof(KernelName)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

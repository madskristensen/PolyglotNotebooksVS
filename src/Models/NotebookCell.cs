using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PolyglotNotebooks.Models
{
    public class NotebookCell : INotifyPropertyChanged
    {
        private string _kernelName;
        private string _contents;
        private int? _executionOrder;
        private CellExecutionStatus _executionStatus;
        private TimeSpan? _lastExecutionDuration;
        private bool _isDirty;

        public NotebookCell(CellKind kind, string kernelName, string contents = "")
        {
            Id = Guid.NewGuid().ToString();
            Kind = kind;
            _kernelName = kernelName;
            _contents = contents;
            Outputs = new ObservableCollection<CellOutput>();
            Metadata = new Dictionary<string, object>();
        }

        internal NotebookCell(string id, CellKind kind, string kernelName, string contents)
        {
            Id = id ?? Guid.NewGuid().ToString();
            Kind = kind;
            _kernelName = kernelName;
            _contents = contents;
            Outputs = new ObservableCollection<CellOutput>();
            Metadata = new Dictionary<string, object>();
        }

        public string Id { get; }

        public CellKind Kind { get; }

        public string KernelName
        {
            get => _kernelName;
            set
            {
                if (_kernelName != value)
                {
                    _kernelName = value;
                    MarkDirty();
                    OnPropertyChanged();
                }
            }
        }

        public string Contents
        {
            get => _contents;
            set
            {
                if (_contents != value)
                {
                    _contents = value;
                    MarkDirty();
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<CellOutput> Outputs { get; }

        public int? ExecutionOrder
        {
            get => _executionOrder;
            set
            {
                if (_executionOrder != value)
                {
                    _executionOrder = value;
                    OnPropertyChanged();
                }
            }
        }

        public CellExecutionStatus ExecutionStatus
        {
            get => _executionStatus;
            set
            {
                if (_executionStatus != value)
                {
                    _executionStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        public TimeSpan? LastExecutionDuration
        {
            get => _lastExecutionDuration;
            set
            {
                if (_lastExecutionDuration != value)
                {
                    _lastExecutionDuration = value;
                    OnPropertyChanged();
                }
            }
        }

        public Dictionary<string, object> Metadata { get; }

        public bool IsDirty
        {
            get => _isDirty;
            private set
            {
                if (_isDirty != value)
                {
                    _isDirty = value;
                    OnPropertyChanged();
                }
            }
        }

        public void MarkClean() => IsDirty = false;

        private void MarkDirty() => IsDirty = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace PolyglotNotebooks.Models
{
    public class NotebookDocument : INotifyPropertyChanged
    {
        private string _filePath;
        private string _defaultKernelName;
        private bool _isDirty;

        private NotebookDocument(string filePath, NotebookFormat format, string defaultKernelName)
        {
            _filePath = filePath;
            Format = format;
            _defaultKernelName = defaultKernelName;
            Cells = new ObservableCollection<NotebookCell>();
            Cells.CollectionChanged += OnCellsCollectionChanged;
            Metadata = new Dictionary<string, object>();
        }

        public static NotebookDocument Create(string filePath, NotebookFormat format, string defaultKernelName = "csharp")
            => new NotebookDocument(filePath, format, defaultKernelName);

        public ObservableCollection<NotebookCell> Cells { get; }

        /// <summary>
        /// Document-level metadata. For .ipynb files this preserves kernelspec, language_info,
        /// nbformat, and any other top-level Jupyter metadata across open/edit/save cycles.
        /// </summary>
        public Dictionary<string, object> Metadata { get; }

        public string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FileName));
                }
            }
        }

        public string FileName => Path.GetFileName(_filePath);

        public NotebookFormat Format { get; }

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

        public string DefaultKernelName
        {
            get => _defaultKernelName;
            set
            {
                if (_defaultKernelName != value)
                {
                    _defaultKernelName = value;
                    SetDirty();
                    OnPropertyChanged();
                }
            }
        }

        public NotebookCell AddCell(CellKind kind, string kernelName, int? index = null)
        {
            var cell = new NotebookCell(kind, kernelName);
            cell.PropertyChanged += OnCellPropertyChanged;

            if (index.HasValue && index.Value >= 0 && index.Value <= Cells.Count)
                Cells.Insert(index.Value, cell);
            else
                Cells.Add(cell);

            SetDirty();
            return cell;
        }

        public void RemoveCell(NotebookCell cell)
        {
            if (Cells.Remove(cell))
            {
                cell.PropertyChanged -= OnCellPropertyChanged;
                SetDirty();
            }
        }

        public void MoveCell(NotebookCell cell, int newIndex)
        {
            int currentIndex = Cells.IndexOf(cell);
            if (currentIndex < 0)
                return;

            newIndex = Math.Max(0, Math.Min(newIndex, Cells.Count - 1));
            if (currentIndex != newIndex)
            {
                Cells.Move(currentIndex, newIndex);
                SetDirty();
            }
        }

        public static NotebookDocument Load(string filePath)
            => NotebookParser.Load(filePath);

        public void Save()
        {
            NotebookParser.Save(this);
            MarkClean();
        }

        public void SaveAs(string filePath)
        {
            FilePath = filePath;
            NotebookParser.Save(this);
            MarkClean();
        }

        public void MarkClean()
        {
            IsDirty = false;
            foreach (var cell in Cells)
                cell.MarkClean();
        }

        internal void AddCellInternal(NotebookCell cell)
        {
            cell.PropertyChanged += OnCellPropertyChanged;
            Cells.Add(cell);
        }

        private void SetDirty() => IsDirty = true;

        private void OnCellsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (NotebookCell cell in e.NewItems)
                    cell.PropertyChanged += OnCellPropertyChanged;

            if (e.OldItems != null)
                foreach (NotebookCell cell in e.OldItems)
                    cell.PropertyChanged -= OnCellPropertyChanged;
        }

        private void OnCellPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NotebookCell.IsDirty) && sender is NotebookCell cell && cell.IsDirty)
                SetDirty();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

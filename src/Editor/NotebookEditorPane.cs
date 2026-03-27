using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using PolyglotNotebooks.Diagnostics;
using PolyglotNotebooks.Execution;
using PolyglotNotebooks.IntelliSense;
using PolyglotNotebooks.Kernel;
using PolyglotNotebooks.Models;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PolyglotNotebooks.Editor
{
    /// <summary>
    /// Custom editor pane that hosts the notebook WPF UI and manages document persistence.
    /// Serves as both the view (IVsWindowPane via WindowPane) and the document data (IVsPersistDocData).
    /// </summary>
    public sealed class NotebookEditorPane : WindowPane, IVsPersistDocData
    {
        // GUID of NotebookEditorFactory — returned by GetGuidEditorType.
        private static readonly Guid EditorFactoryGuid = new Guid("52746fdf-4a26-4633-a712-74470fe70bd4");

        private readonly PolyglotNotebooksPackage _package;
        private readonly NotebookDocumentManager _documentManager;
        private string _filePath;
        private NotebookDocument? _document;
        private NotebookControl? _control;
        private KernelProcessManager? _kernelProcessManager;
        private ExecutionCoordinator? _coordinator;
        private IntelliSenseManager? _intelliSenseManager;
        private bool _closed;

        public NotebookEditorPane(
            PolyglotNotebooksPackage package,
            NotebookDocumentManager documentManager,
            string filePath)
            : base(null)
        {
            _package = package;
            _documentManager = documentManager;
            _filePath = filePath;
        }

        protected override void Initialize()
        {
            base.Initialize();
            _control = new NotebookControl(_document);
            Content = _control;
        }

        // ── IVsPersistDocData ─────────────────────────────────────────────────────

        public int GetGuidEditorType(out Guid pClassID)
        {
            pClassID = EditorFactoryGuid;
            return VSConstants.S_OK;
        }

        public int IsDocDataDirty(out int pfDirty)
        {
            pfDirty = (_document != null && _document.IsDirty) ? 1 : 0;
            return VSConstants.S_OK;
        }

        public int SetUntitledDocPath(string pszDocDataPath)
        {
            _filePath = pszDocDataPath;
            return VSConstants.S_OK;
        }

        public int LoadDocData(string pszMkDocument)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _filePath = pszMkDocument;

            // Check dotnet-interactive installation and notify the user if missing.
            // If the user installs via the dialog, re-check and proceed normally.
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                var detector = new Kernel.KernelInstallationDetector();
                bool isInstalled = await detector.IsInstalledAsync().ConfigureAwait(false);
                if (!isInstalled)
                {
                    ExtensionLogger.LogWarning(nameof(NotebookEditorPane),
                        $"dotnet-interactive not installed; opening '{pszMkDocument}' in degraded mode.");
                    bool installed = await Kernel.KernelNotInstalledDialog.ShowAsync(detector).ConfigureAwait(false);
                    if (installed)
                    {
                        // Re-verify after install; cache was invalidated by the dialog.
                        isInstalled = await detector.IsInstalledAsync().ConfigureAwait(false);
                    }
                }

                if (isInstalled)
                {
                    ExtensionLogger.LogInfo(nameof(NotebookEditorPane),
                        $"dotnet-interactive detected. Loading '{pszMkDocument}'.");
                }
            });

            try
            {
                _document = ThreadHelper.JoinableTaskFactory.Run(
                    () => _documentManager.OpenAsync(pszMkDocument));
            }
            catch (Exception ex)
            {
                ExtensionLogger.LogException(nameof(NotebookEditorPane),
                    $"Failed to load notebook '{pszMkDocument}'", ex);
                ActivityLog.LogError(nameof(NotebookEditorPane),
                    $"Failed to load notebook '{pszMkDocument}': {ex.Message}");
                return VSConstants.E_FAIL;
            }

            _document.PropertyChanged += OnDocumentPropertyChanged;

            if (_control != null)
                _control.Document = _document;

            // Wire up the Run button → execution engine pipeline.
            var workingDirectory = Path.GetDirectoryName(_filePath) ?? string.Empty;
            _kernelProcessManager = new KernelProcessManager(workingDirectory);
            _coordinator = new ExecutionCoordinator(_kernelProcessManager);
            if (_control != null)
            {
                _control.CellRunRequested        += _coordinator.HandleCellRunRequested;
                _control.RunAllRequested         += OnRunAllRequested;
                _control.RestartAndRunAllRequested += OnRestartAndRunAllRequested;
                _control.InterruptRequested      += OnInterruptRequested;
                _control.RestartKernelRequested  += OnRestartKernelRequested;
                _control.ClearAllOutputsRequested += OnClearAllOutputsRequested;
                _control.RunCellAboveRequested   += OnRunCellAboveRequested;
                _control.RunCellBelowRequested   += OnRunCellBelowRequested;
                _control.RunSelectionRequested   += OnRunSelectionRequested;
            }
            _kernelProcessManager.StatusChanged += OnKernelStatusChanged;

            _coordinator.KernelClientAvailable += OnKernelClientAvailable;

            return VSConstants.S_OK;
        }

        private void OnKernelClientAvailable(Protocol.KernelClient client)
        {
#pragma warning disable VSTHRD110, VSSDK007
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    _intelliSenseManager ??= new IntelliSenseManager();
                    _intelliSenseManager.SetKernelClient(client);
                    if (_control != null)
                        _control.IntelliSenseManager = _intelliSenseManager;

                    Variables.VariableService.Current?.SetKernelClient(client);
                }
                catch (Exception ex)
                {
                    ExtensionLogger.LogException(nameof(NotebookEditorPane),
                        "Error initializing IntelliSense", ex);
                }
            });
#pragma warning restore VSTHRD110, VSSDK007
        }

        public int SaveDocData(VSSAVEFLAGS grfSave, out string pbstrMkDocumentNew, out int pfSaveCanceled)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            pbstrMkDocumentNew = _filePath;
            pfSaveCanceled = 0;

            if (_document == null)
                return VSConstants.E_UNEXPECTED;

            try
            {
                switch (grfSave)
                {
                    case VSSAVEFLAGS.VSSAVE_Save:
                    case VSSAVEFLAGS.VSSAVE_SilentSave:
                        _document.Save();
                        break;

                    case VSSAVEFLAGS.VSSAVE_SaveAs:
                    {
                        string? newPath = ShowSaveDialog();
                        if (newPath == null)
                        {
                            pfSaveCanceled = 1;
                            return VSConstants.S_OK;
                        }
                        _document.SaveAs(newPath);
                        _filePath = newPath;
                        pbstrMkDocumentNew = newPath;
                        break;
                    }

                    case VSSAVEFLAGS.VSSAVE_SaveCopyAs:
                    {
                        string? newPath = ShowSaveDialog();
                        if (newPath == null)
                        {
                            pfSaveCanceled = 1;
                            return VSConstants.S_OK;
                        }
                        // Save a copy without changing the document's tracked path or dirty state.
                        string content = _document.Format == NotebookFormat.Ipynb
                            ? NotebookParser.SerializeIpynb(_document)
                            : NotebookParser.SerializeDib(_document);
                        File.WriteAllText(newPath, content);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                ActivityLog.LogError(nameof(NotebookEditorPane),
                    $"Failed to save notebook: {ex.Message}");
                return VSConstants.E_FAIL;
            }

            return VSConstants.S_OK;
        }

        public int Close()
        {
            if (!_closed)
            {
                _closed = true;
                UnsubscribeDocument();

                if (_control != null && _coordinator != null)
                    _control.CellRunRequested -= _coordinator.HandleCellRunRequested;

                if (_control != null)
                {
                    _control.RunAllRequested             -= OnRunAllRequested;
                    _control.RestartAndRunAllRequested   -= OnRestartAndRunAllRequested;
                    _control.InterruptRequested          -= OnInterruptRequested;
                    _control.RestartKernelRequested      -= OnRestartKernelRequested;
                    _control.ClearAllOutputsRequested    -= OnClearAllOutputsRequested;
                    _control.RunCellAboveRequested       -= OnRunCellAboveRequested;
                    _control.RunCellBelowRequested       -= OnRunCellBelowRequested;
                    _control.RunSelectionRequested       -= OnRunSelectionRequested;
                }

                if (_kernelProcessManager != null)
                    _kernelProcessManager.StatusChanged -= OnKernelStatusChanged;

                if (_coordinator != null)
                    _coordinator.KernelClientAvailable -= OnKernelClientAvailable;

                _intelliSenseManager?.Dispose();
                _intelliSenseManager = null;

                _coordinator?.Dispose();
                _coordinator = null;

                _kernelProcessManager?.Dispose();
                _kernelProcessManager = null;

                ThreadHelper.JoinableTaskFactory.Run(
                    () => _documentManager.CloseAsync(_filePath));
            }
            return VSConstants.S_OK;
        }

        public int IsDocDataReloadable(out int pfReloadable)
        {
            pfReloadable = 1;
            return VSConstants.S_OK;
        }

        public int ReloadDocData(uint grfFlags)
        {
            if (_document == null)
                return VSConstants.E_UNEXPECTED;

            try
            {
                UnsubscribeDocument();
                ThreadHelper.JoinableTaskFactory.Run(
                    () => _documentManager.CloseAsync(_filePath));

                _document = ThreadHelper.JoinableTaskFactory.Run(
                    () => _documentManager.OpenAsync(_filePath));

                _document.PropertyChanged += OnDocumentPropertyChanged;

                if (_control != null)
                    _control.Document = _document;
            }
            catch (Exception ex)
            {
                ActivityLog.LogError(nameof(NotebookEditorPane),
                    $"Failed to reload notebook: {ex.Message}");
                return VSConstants.E_FAIL;
            }

            return VSConstants.S_OK;
        }

        public int OnRegisterDocData(uint docCookie, IVsHierarchy pHierNew, uint itemidNew)
            => VSConstants.S_OK;

        public int RenameDocData(uint grfAttribs, IVsHierarchy pHierNew, uint itemidNew, string pszMkDocumentNew)
        {
            if (_document == null)
                return VSConstants.S_OK;

            // Unregister from old path, update document path, re-register under new path.
            ThreadHelper.JoinableTaskFactory.Run(() => _documentManager.CloseAsync(_filePath));
            _document.FilePath = pszMkDocumentNew;
            _filePath = pszMkDocumentNew;
            _documentManager.RegisterDocument(pszMkDocumentNew, _document);

            return VSConstants.S_OK;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private void UnsubscribeDocument()
        {
            if (_document != null)
                _document.PropertyChanged -= OnDocumentPropertyChanged;
        }

        private void OnDocumentPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // VS polls IsDocDataDirty; no extra action required here.
            // This hook is available for future UI updates (e.g. title bar asterisk).
        }

        private string? ShowSaveDialog()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = Path.GetFileName(_filePath),
                Filter = "Polyglot Notebook (*.dib)|*.dib|Jupyter Notebook (*.ipynb)|*.ipynb|All Files (*.*)|*.*",
                InitialDirectory = Path.GetDirectoryName(_filePath)
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_closed)
            {
                UnsubscribeDocument();

                if (_control != null && _coordinator != null)
                    _control.CellRunRequested -= _coordinator.HandleCellRunRequested;

                if (_control != null)
                {
                    _control.RunAllRequested             -= OnRunAllRequested;
                    _control.RestartAndRunAllRequested   -= OnRestartAndRunAllRequested;
                    _control.InterruptRequested          -= OnInterruptRequested;
                    _control.RestartKernelRequested      -= OnRestartKernelRequested;
                    _control.ClearAllOutputsRequested    -= OnClearAllOutputsRequested;
                    _control.RunCellAboveRequested       -= OnRunCellAboveRequested;
                    _control.RunCellBelowRequested       -= OnRunCellBelowRequested;
                    _control.RunSelectionRequested       -= OnRunSelectionRequested;
                }

                if (_kernelProcessManager != null)
                    _kernelProcessManager.StatusChanged -= OnKernelStatusChanged;

                if (_coordinator != null)
                    _coordinator.KernelClientAvailable -= OnKernelClientAvailable;

                _intelliSenseManager?.Dispose();
                _coordinator?.Dispose();
                _kernelProcessManager?.Dispose();
            }

            base.Dispose(disposing);
        }

        // ── Toolbar event handlers ────────────────────────────────────────────────

        private void OnRunAllRequested(object sender, EventArgs e)
        {
            if (_coordinator == null || _document == null) return;
            _coordinator.HandleRunAllRequested(_document);
        }

        private void OnRestartAndRunAllRequested(object sender, EventArgs e)
        {
            if (_coordinator == null || _document == null) return;
            _coordinator.HandleRestartAndRunAllRequested(_document);
        }

        private void OnInterruptRequested(object sender, EventArgs e)
            => _coordinator?.CancelCurrentExecution();

        private void OnRunCellAboveRequested(object sender, CellRunEventArgs e)
        {
            if (_coordinator == null || _document == null || e?.Cell == null) return;
            _coordinator.HandleRunCellsAboveRequested(_document, e.Cell);
        }

        private void OnRunCellBelowRequested(object sender, CellRunEventArgs e)
        {
            if (_coordinator == null || _document == null || e?.Cell == null) return;
            _coordinator.HandleRunCellsBelowRequested(_document, e.Cell);
        }

        private void OnRunSelectionRequested(object sender, CellRunSelectionEventArgs e)
        {
            if (_coordinator == null || e?.Cell == null) return;
            _coordinator.HandleRunSelectionRequested(e.Cell, e.SelectedText);
        }

        private void OnRestartKernelRequested(object sender, EventArgs e)
        {
            if (_kernelProcessManager == null) return;

#pragma warning disable VSTHRD110, VSSDK007
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try { await _kernelProcessManager.RestartAsync().ConfigureAwait(false); }
                catch (Exception ex)
                {
                    ExtensionLogger.LogException(nameof(NotebookEditorPane),
                        "Kernel restart failed.", ex);
                }
            });
#pragma warning restore VSTHRD110, VSSDK007
        }

        private void OnClearAllOutputsRequested(object sender, EventArgs e)
        {
            if (_document == null) return;
            foreach (var cell in _document.Cells)
                cell.Outputs.Clear();
        }

        private void OnKernelStatusChanged(object sender, KernelStatusChangedEventArgs e)
        {
            var status = e.NewStatus;
#pragma warning disable VSTHRD110, VSSDK007
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _control?.UpdateKernelStatus(status);
            });
#pragma warning restore VSTHRD110, VSSDK007
        }
    }
}

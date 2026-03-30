using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Web.WebView2.Core;

using PolyglotNotebooks.Diagnostics;
using PolyglotNotebooks.Execution;
using PolyglotNotebooks.IntelliSense;
using PolyglotNotebooks.Kernel;
using PolyglotNotebooks.Models;

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms.Integration;

namespace PolyglotNotebooks.Editor
{
    /// <summary>
    /// Custom editor pane that hosts the notebook WPF UI and manages document persistence.
    /// Serves as both the view (IVsWindowPane via WindowPane) and the document data (IVsPersistDocData).
    /// </summary>
    [ComVisible(true)]
    public sealed class NotebookEditorPane : WindowPane, IVsPersistDocData2, IPersistFileFormat, IOleCommandTarget, IVsDocOutlineProvider
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
        private ElementHost? _outlineHost;
        private DocumentOutlineControl? _outlineControl;

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
            _control.IsKeyboardFocusWithinChanged += OnNotebookFocusChanged;
            Content = _control;
        }

        /// <summary>
        /// When keyboard focus enters this notebook pane, re-bind the Variable Explorer
        /// to this notebook's kernel so it shows the correct variables.
        /// </summary>
        private void OnNotebookFocusChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue && _coordinator?.KernelClient != null)
            {
                Variables.VariableService.Current.SetKernelClient(_coordinator.KernelClient);
#pragma warning disable VSTHRD110
                _ = Variables.VariableService.Current.RefreshVariablesAsync();
#pragma warning restore VSTHRD110
            }
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

            // Installation check for dotnet-interactive is deferred to the first
            // cell execution (see ExecutionCoordinator.EnsureKernelStartedAsync).
            // This keeps LoadDocData fast — no child process spawn on the UI thread.

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
            _coordinator = new ExecutionCoordinator(_kernelProcessManager, _filePath);
            if (_control != null)
            {
                _control.CellRunRequested += _coordinator.HandleCellRunRequested;
                _control.RunAllRequested += OnRunAllRequested;
                _control.RestartAndRunAllRequested += OnRestartAndRunAllRequested;
                _control.InterruptRequested += OnInterruptRequested;
                _control.RestartKernelRequested += OnRestartKernelRequested;
                _control.ClearAllOutputsRequested += OnClearAllOutputsRequested;
                _control.ExportRequested += OnExportRequested;
                _control.RunCellAboveRequested += OnRunCellAboveRequested;
                _control.RunCellBelowRequested += OnRunCellBelowRequested;
                _control.RunSelectionRequested += OnRunSelectionRequested;
                _control.CellStopRequested += OnInterruptRequested;
            }
            _kernelProcessManager.StatusChanged += OnKernelStatusChanged;

            _coordinator.KernelClientAvailable += OnKernelClientAvailable;
            _coordinator.ExecutionCompleted += OnExecutionCompleted;

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

                    Variables.VariableService.Current.SetKernelClient(client);
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

            // Delegate to IVsUIShell.SaveDocDataToFile which manages the Save As
            // dialog, QueryEditQuerySave checks, and calls back through our
            // IPersistFileFormat.Save implementation.
            IVsUIShell uiShell = (IVsUIShell)GetService(typeof(SVsUIShell));
            if (uiShell == null)
                return VSConstants.E_FAIL;

            return uiShell.SaveDocDataToFile(grfSave, this, _filePath,
                out pbstrMkDocumentNew, out pfSaveCanceled);
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
                    _control.IsKeyboardFocusWithinChanged -= OnNotebookFocusChanged;
                    _control.RunAllRequested -= OnRunAllRequested;
                    _control.RestartAndRunAllRequested -= OnRestartAndRunAllRequested;
                    _control.InterruptRequested -= OnInterruptRequested;
                    _control.RestartKernelRequested -= OnRestartKernelRequested;
                    _control.ClearAllOutputsRequested -= OnClearAllOutputsRequested;
                    _control.ExportRequested -= OnExportRequested;
                    _control.RunCellAboveRequested -= OnRunCellAboveRequested;
                    _control.RunCellBelowRequested -= OnRunCellBelowRequested;
                    _control.RunSelectionRequested -= OnRunSelectionRequested;
                }

                if (_kernelProcessManager != null)
                    _kernelProcessManager.StatusChanged -= OnKernelStatusChanged;

                if (_coordinator != null)
                    _coordinator.KernelClientAvailable -= OnKernelClientAvailable;

                if (_coordinator != null)
                    _coordinator.ExecutionCompleted -= OnExecutionCompleted;

                _intelliSenseManager?.Dispose();
                _intelliSenseManager = null;

                _outlineControl?.Cleanup();
                _outlineControl = null;
                if (_outlineHost != null)
                {
                    _outlineHost.Child = null;
                    _outlineHost.Dispose();
                    _outlineHost = null;
                }

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

        // ── IVsPersistDocData2 additional members ────────────────────────────

        public int SetDocDataDirty(int fDirty)
        {
            if (_document != null)
                _document.IsDirty = fDirty != 0;

            return VSConstants.S_OK;
        }

        public int IsDocDataReadOnly(out int pfReadOnly)
        {
            pfReadOnly = 0;
            return VSConstants.S_OK;
        }

        public int SetDocDataReadOnly(int fReadOnly)
        {
            return VSConstants.S_OK;
        }

        // ── IPersistFileFormat ───────────────────────────────────────────────

        int Microsoft.VisualStudio.OLE.Interop.IPersist.GetClassID(out Guid pClassID)
        {
            pClassID = EditorFactoryGuid;
            return VSConstants.S_OK;
        }

        int IPersistFileFormat.GetClassID(out Guid pClassID)
        {
            pClassID = EditorFactoryGuid;
            return VSConstants.S_OK;
        }

        public int GetFormatList(out string ppszFormatList)
        {
            ppszFormatList = "Polyglot Notebook (*.dib)\n*.dib\nJupyter Notebook (*.ipynb)\n*.ipynb\n";
            return VSConstants.S_OK;
        }

        int IPersistFileFormat.GetCurFile(out string ppszFilename, out uint pnFormatIndex)
        {
            ppszFilename = _filePath;
            // 0 = .dib, 1 = .ipynb
            pnFormatIndex = (_document != null && _document.Format == NotebookFormat.Ipynb) ? 1u : 0u;
            return VSConstants.S_OK;
        }

        int IPersistFileFormat.InitNew(uint nFormatIndex)
        {
            return VSConstants.S_OK;
        }

        int IPersistFileFormat.IsDirty(out int pfIsDirty)
        {
            pfIsDirty = (_document != null && _document.IsDirty) ? 1 : 0;
            return VSConstants.S_OK;
        }

        int IPersistFileFormat.Load(string pszFilename, uint grfMode, int fReadOnly)
        {
            return LoadDocData(pszFilename);
        }

        int IPersistFileFormat.Save(string pszFilename, int fRemember, uint nFormatIndex)
        {
            if (_document == null)
                return VSConstants.E_UNEXPECTED;

            try
            {
                if (string.IsNullOrEmpty(pszFilename))
                {
                    // Save to current file.
                    _document.Save();
                }
                else if (fRemember != 0)
                {
                    // Save As: save to new path and adopt it.
                    _document.SaveAs(pszFilename);
                    _filePath = pszFilename;
                }
                else
                {
                    // Save Copy As: write to the specified path without changing state.
                    string content = _document.Format == NotebookFormat.Ipynb
                        ? NotebookParser.SerializeIpynb(_document)
                        : NotebookParser.SerializeDib(_document);
                    File.WriteAllText(pszFilename, content);
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

        int IPersistFileFormat.SaveCompleted(string pszFilename)
        {
            return VSConstants.S_OK;
        }

        // ── IOleCommandTarget

        int IOleCommandTarget.Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Don't forward Save/SaveAs to the embedded text view — it would save
            // the cell buffer to its fake .cs/.js path instead of the notebook file.
            // Returning NOTSUPPORTED lets VS fall through to IVsPersistDocData.SaveDocData().
            if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97)
            {
                switch ((VSConstants.VSStd97CmdID)nCmdID)
                {
                    case VSConstants.VSStd97CmdID.Save:
                    case VSConstants.VSStd97CmdID.SaveAs:
                    case VSConstants.VSStd97CmdID.SaveProjectItem:
                        return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
                }
            }

            if (_control != null)
            {
                // Use HasFocusedTextView (checks IWpfTextView.HasAggregateFocus)
                // instead of IsKeyboardFocusWithin (WPF-only, misses IVsCodeWindow's HWND)
                var cmdTarget = _control.GetFocusedCommandTarget();
                if (cmdTarget != null)
                    return cmdTarget.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
            }
            return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
        }

        int IOleCommandTarget.QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Don't forward Save/SaveAs status to the embedded text view — VS handles
            // these through IVsPersistDocData and the Running Document Table.
            if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97 && prgCmds != null)
            {
                for (int i = 0; i < prgCmds.Length; i++)
                {
                    switch ((VSConstants.VSStd97CmdID)prgCmds[i].cmdID)
                    {
                        case VSConstants.VSStd97CmdID.Save:
                        case VSConstants.VSStd97CmdID.SaveAs:
                        case VSConstants.VSStd97CmdID.SaveProjectItem:
                            return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
                    }
                }
            }

            if (_control != null)
            {
                var cmdTarget = _control.GetFocusedCommandTarget();
                if (cmdTarget != null)
                    return cmdTarget.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
            }
            return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
        }

        // ── Keyboard routing ──────────────────────────────────────────────────────

        /// <summary>
        /// Translates keyboard messages using VS's accelerator service so that editor
        /// key bindings (IntelliSense, navigation, etc.) are properly dispatched.
        /// The command flows through IVsTextView's IOleCommandTarget filter chain,
        /// which includes the CommandHandlerServiceAdapter that bridges to MEF
        /// ICommandHandler&lt;T&gt; (e.g. Copilot's ICommandHandler&lt;TabKeyCommandArgs&gt;).
        /// </summary>
        protected override bool PreProcessMessage(ref System.Windows.Forms.Message m)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // WM_KEYDOWN (0x100) through WM_UNICHAR (0x109)
            if (m.Msg >= 0x0100 && m.Msg <= 0x0109)
            {
                if (_control != null && _control.HasFocusedTextView())
                {
                    IVsFilterKeys2 filterKeys = (IVsFilterKeys2)GetService(typeof(SVsFilterKeys));
                    if (filterKeys != null)
                    {
                        MSG oleMSG = new MSG()
                        {
                            hwnd = m.HWnd,
                            message = (uint)m.Msg,
                            wParam = m.WParam,
                            lParam = m.LParam
                        };

                        Guid cmdGuid;
                        uint cmdId;
                        int fTranslated;
                        int fStartsMultiKeyChord;

                        int res = filterKeys.TranslateAcceleratorEx(
                            new MSG[] { oleMSG },
                            (uint)__VSTRANSACCELEXFLAGS.VSTAEXF_UseTextEditorKBScope,
                            0,
                            new Guid[0],
                            out cmdGuid,
                            out cmdId,
                            out fTranslated,
                            out fStartsMultiKeyChord);

                        if (fStartsMultiKeyChord == 0)
                        {
                            res = filterKeys.TranslateAcceleratorEx(
                                new MSG[] { oleMSG },
                                (uint)(__VSTRANSACCELEXFLAGS.VSTAEXF_NoFireCommand | __VSTRANSACCELEXFLAGS.VSTAEXF_UseTextEditorKBScope),
                                0,
                                new Guid[0],
                                out cmdGuid,
                                out cmdId,
                                out fTranslated,
                                out fStartsMultiKeyChord);
                            return (res == VSConstants.S_OK);
                        }
                        return (res == VSConstants.S_OK) || (fStartsMultiKeyChord != 0);
                    }
                }
            }

            return base.PreProcessMessage(ref m);
        }

        // ── IVsDocOutlineProvider ─────────────────────────────────────────────────

        public int GetOutline(out IntPtr phwnd, out IOleCommandTarget ppCmdTarget)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ppCmdTarget = null!;

            if (_document == null || _control == null)
            {
                phwnd = IntPtr.Zero;
                return VSConstants.E_UNEXPECTED;
            }

            _outlineControl = new DocumentOutlineControl(_document, _control);
            _outlineHost = new ElementHost
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                Child = _outlineControl
            };

            phwnd = _outlineHost.Handle;
            return VSConstants.S_OK;
        }

        public int ReleaseOutline(IntPtr hwnd, IOleCommandTarget pCmdTarget)
        {
            if (_outlineControl != null)
            {
                _outlineControl.Cleanup();
                _outlineControl = null;
            }

            if (_outlineHost != null)
            {
                _outlineHost.Child = null;
                _outlineHost.Dispose();
                _outlineHost = null;
            }

            return VSConstants.S_OK;
        }

        public int GetOutlineCaption(VSOUTLINECAPTION nCaptionType, out string pbstrCaption)
        {
            pbstrCaption = "Document Outline";
            return VSConstants.S_OK;
        }

        public int OnOutlineStateChange(uint dwMask, uint dwState)
        {
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
                    _control.IsKeyboardFocusWithinChanged -= OnNotebookFocusChanged;
                    _control.RunAllRequested -= OnRunAllRequested;
                    _control.RestartAndRunAllRequested -= OnRestartAndRunAllRequested;
                    _control.InterruptRequested -= OnInterruptRequested;
                    _control.RestartKernelRequested -= OnRestartKernelRequested;
                    _control.ClearAllOutputsRequested -= OnClearAllOutputsRequested;
                    _control.ExportRequested -= OnExportRequested;
                    _control.RunCellAboveRequested -= OnRunCellAboveRequested;
                    _control.RunCellBelowRequested -= OnRunCellBelowRequested;
                    _control.RunSelectionRequested -= OnRunSelectionRequested;
                }

                if (_kernelProcessManager != null)
                    _kernelProcessManager.StatusChanged -= OnKernelStatusChanged;

                if (_coordinator != null)
                    _coordinator.KernelClientAvailable -= OnKernelClientAvailable;

                if (_coordinator != null)
                    _coordinator.ExecutionCompleted -= OnExecutionCompleted;

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
            _control?.SetExecuting(true);
            _coordinator.HandleRunAllRequested(_document);
        }

        private void OnRestartAndRunAllRequested(object sender, EventArgs e)
        {
            if (_coordinator == null || _document == null) return;
            _control?.SetExecuting(true);
            _coordinator.HandleRestartAndRunAllRequested(_document);
        }

        private void OnInterruptRequested(object sender, EventArgs e)
        {
            // Immediately reset any Running/Queued cells on the UI thread so the timer
            // stops and the Stop button hides without waiting for the async cancellation
            // chain to propagate back through the engine and coordinator.
            if (_document != null)
            {
                foreach (var cell in _document.Cells)
                {
                    if (cell.ExecutionStatus == Models.CellExecutionStatus.Running ||
                        cell.ExecutionStatus == Models.CellExecutionStatus.Queued)
                    {
                        cell.ExecutionStatus = Models.CellExecutionStatus.Idle;
                    }
                }
            }

            _coordinator?.CancelCurrentExecution();
        }

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
            if (_coordinator == null) return;
            _coordinator.HandleRestartKernelRequested();
        }

        private void OnClearAllOutputsRequested(object sender, EventArgs e)
        {
            if (_document == null) return;
            foreach (var cell in _document.Cells)
                cell.Outputs.Clear();
        }

        private void OnExportRequested(object sender, ExportFormat format)
        {
            if (_document == null) return;

            try
            {
                string filter = NotebookExporter.GetFileFilter(format);
                string ext = NotebookExporter.GetFileExtension(format);
                string defaultName = Path.GetFileNameWithoutExtension(_document.FilePath) + ext;

                var dialog = new System.Windows.Forms.SaveFileDialog
                {
                    Filter = filter,
                    FileName = defaultName,
                    Title = "Export Notebook"
                };

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;

                string savedPath = dialog.FileName;

                if (format == ExportFormat.Pdf)
                {
                    string htmlForPdf = NotebookExporter.ExportToHtml(_document);
#pragma warning disable VSTHRD110
                    _ = ExportToPdfAsync(htmlForPdf, savedPath);
#pragma warning restore VSTHRD110
                    return;
                }

                string content = NotebookExporter.Export(_document, format);
                File.WriteAllText(savedPath, content, System.Text.Encoding.UTF8);

                OpenExportedFile(format, savedPath);
            }
            catch (Exception ex)
            {
                ExtensionLogger.LogException(nameof(NotebookEditorPane), "Export failed", ex);
            }
        }

        private static void OpenExportedFile(ExportFormat format, string filePath)
        {
            try
            {
                if (format == ExportFormat.Html)
                {
                    Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                }
                else
                {
#pragma warning disable VSTHRD110
                    _ = VS.Documents.OpenAsync(filePath);
#pragma warning restore VSTHRD110
                }
            }
            catch (Exception ex)
            {
                ExtensionLogger.LogException(nameof(NotebookEditorPane), "Failed to open exported file", ex);
            }
        }

        /// <summary>
        /// Renders <paramref name="htmlContent"/> in a temporary offscreen WebView2
        /// and prints it to PDF at <paramref name="pdfPath"/>.
        /// Uses a hidden WinForms window to provide the HWND that WebView2 requires.
        /// </summary>
        private async Task ExportToPdfAsync(string htmlContent, string pdfPath)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            System.Windows.Forms.Form hiddenForm = null;
            CoreWebView2Controller controller = null;
            try
            {
                hiddenForm = new System.Windows.Forms.Form
                {
                    Width = 1024,
                    Height = 768,
                    ShowInTaskbar = false,
                    WindowState = System.Windows.Forms.FormWindowState.Minimized,
                    FormBorderStyle = System.Windows.Forms.FormBorderStyle.None,
                    Opacity = 0
                };
                hiddenForm.Show();

                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PolyglotNotebooksVS", "WebView2PdfCache");

                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                controller = await env.CreateCoreWebView2ControllerAsync(hiddenForm.Handle);

                CoreWebView2 coreWebView = controller.CoreWebView2;

                var navTcs = new TaskCompletionSource<bool>();
                coreWebView.NavigationCompleted += (s, e) => navTcs.TrySetResult(e.IsSuccess);
                coreWebView.NavigateToString(htmlContent);

                bool success = await navTcs.Task;
                if (!success)
                {
                    ExtensionLogger.LogWarning(nameof(NotebookEditorPane), "PDF export: navigation failed");
                    return;
                }

                await coreWebView.PrintToPdfAsync(pdfPath);

                Process.Start(new ProcessStartInfo(pdfPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ExtensionLogger.LogException(nameof(NotebookEditorPane), "PDF export failed", ex);
            }
            finally
            {
                controller?.Close();
                hiddenForm?.Close();
                hiddenForm?.Dispose();
            }
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

        private void OnExecutionCompleted(object sender, EventArgs e)
        {
#pragma warning disable VSTHRD110, VSSDK007
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _control?.SetExecuting(false);
            });
#pragma warning restore VSTHRD110, VSSDK007
        }
    }
}

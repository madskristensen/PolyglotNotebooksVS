using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using PolyglotNotebooks.Models;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PolyglotNotebooks.Editor
{
    [Guid("52746fdf-4a26-4633-a712-74470fe70bd4")]
    public sealed class NotebookEditorFactory : IVsEditorFactory
    {
        private readonly PolyglotNotebooksPackage _package;

        internal NotebookDocumentManager DocumentManager { get; } = new NotebookDocumentManager();

        public NotebookEditorFactory(PolyglotNotebooksPackage package)
        {
            _package = package;
        }

        public int SetSite(Microsoft.VisualStudio.OLE.Interop.IServiceProvider psp) => VSConstants.S_OK;

        public int MapLogicalView(ref Guid rguidLogicalView, out string pbstrPhysicalView)
        {
            pbstrPhysicalView = null!;

            if (rguidLogicalView == VSConstants.LOGVIEWID_Primary ||
                rguidLogicalView == VSConstants.LOGVIEWID_Designer)
            {
                return VSConstants.S_OK;
            }

            // Code and TextView views: return E_NOTIMPL so VS falls back to
            // the built-in text editor for F7 (View Code).
            return VSConstants.E_NOTIMPL;
        }

        public int Close() => VSConstants.S_OK;

        public int CreateEditorInstance(
            uint grfCreateDoc,
            string pszMkDocument,
            string pszPhysicalView,
            IVsHierarchy pvHier,
            uint itemid,
            IntPtr punkDocDataExisting,
            out IntPtr ppunkDocView,
            out IntPtr ppunkDocData,
            out string pbstrEditorCaption,
            out Guid pguidCmdUI,
            out int pgrfCDW)
        {
            ppunkDocView = IntPtr.Zero;
            ppunkDocData = IntPtr.Zero;
            pbstrEditorCaption = string.Empty;
            pguidCmdUI = Guid.Empty;
            pgrfCDW = 0;

            // If existing doc data is provided, only accept it if it's our own type.
            if (punkDocDataExisting != IntPtr.Zero)
            {
                object existingDocData = Marshal.GetObjectForIUnknown(punkDocDataExisting);
                if (!(existingDocData is NotebookEditorPane))
                    return VSConstants.VS_E_INCOMPATIBLEDOCDATA;
            }

            var pane = new NotebookEditorPane(_package, DocumentManager, pszMkDocument);

            ppunkDocView = Marshal.GetIUnknownForObject(pane);
            ppunkDocData = Marshal.GetIUnknownForObject(pane);
            pbstrEditorCaption = Path.GetFileName(pszMkDocument);

            return VSConstants.S_OK;
        }
    }
}

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyglotNotebooks.Models;

#pragma warning disable MSTEST0037
#pragma warning disable VSTHRD002

namespace PolyglotNotebooks.Test
{
    /// <summary>
    /// Tests for the editor-factory document-lifecycle layer.
    ///
    /// <see cref="NotebookEditorFactory"/> depends on VS SDK types (IVsEditorFactory,
    /// VSConstants) that are not available in the unit-test runner.  The factory's
    /// primary non-VS responsibility is managing open <see cref="NotebookDocument"/>
    /// instances through <see cref="NotebookDocumentManager"/>, which IS fully
    /// testable here.  A separate integration test suite covers CreateEditorInstance
    /// and MapLogicalView.
    /// </summary>
    [TestClass]
    public class EditorFactoryDocumentManagerTests
    {
        // ── Construction ──────────────────────────────────────────────────────

        [TestMethod]
        public void DocumentManager_WhenCreated_OpenDocumentsIsEmpty()
        {
            var manager = new NotebookDocumentManager();

            Assert.AreEqual(0, manager.OpenDocuments.Count);
        }

        // ── RegisterDocument ──────────────────────────────────────────────────

        [TestMethod]
        public void RegisterDocument_WhenCalled_DocumentAppearsInOpenDocuments()
        {
            var manager = new NotebookDocumentManager();
            var doc = NotebookDocument.Create(@"C:\notebooks\test.dib", NotebookFormat.Dib);

            manager.RegisterDocument(@"C:\notebooks\test.dib", doc);

            Assert.IsTrue(manager.IsOpen(@"C:\notebooks\test.dib"));
        }

        [TestMethod]
        public void RegisterDocument_WhenCalled_FiresDocumentOpenedEvent()
        {
            var manager = new NotebookDocumentManager();
            var doc = NotebookDocument.Create(@"C:\notebooks\test.dib", NotebookFormat.Dib);
            NotebookDocument? received = null;
            manager.DocumentOpened += (s, e) => received = e.Document;

            manager.RegisterDocument(@"C:\notebooks\test.dib", doc);

            Assert.AreSame(doc, received, "DocumentOpened event must carry the registered document");
        }

        [TestMethod]
        public void RegisterDocument_WhenCalledTwice_SecondRegistrationOverwrites()
        {
            var manager = new NotebookDocumentManager();
            var doc1 = NotebookDocument.Create(@"C:\notebooks\note.dib", NotebookFormat.Dib);
            var doc2 = NotebookDocument.Create(@"C:\notebooks\note.dib", NotebookFormat.Dib);

            manager.RegisterDocument(@"C:\notebooks\note.dib", doc1);
            manager.RegisterDocument(@"C:\notebooks\note.dib", doc2);

            // Most-recently registered document wins.
            Assert.AreSame(doc2, manager.GetDocument(@"C:\notebooks\note.dib"));
        }

        // ── IsOpen / GetDocument ──────────────────────────────────────────────

        [TestMethod]
        public void IsOpen_WhenPathNotRegistered_ReturnsFalse()
        {
            var manager = new NotebookDocumentManager();

            Assert.IsFalse(manager.IsOpen(@"C:\notebooks\nonexistent.dib"));
        }

        [TestMethod]
        public void GetDocument_WhenPathNotRegistered_ReturnsNull()
        {
            var manager = new NotebookDocumentManager();

            Assert.IsNull(manager.GetDocument(@"C:\notebooks\nonexistent.dib"));
        }

        [TestMethod]
        public void GetDocument_AfterRegister_ReturnsSameInstance()
        {
            var manager = new NotebookDocumentManager();
            var doc = NotebookDocument.Create(@"C:\notebooks\book.dib", NotebookFormat.Dib);
            manager.RegisterDocument(@"C:\notebooks\book.dib", doc);

            var retrieved = manager.GetDocument(@"C:\notebooks\book.dib");

            Assert.AreSame(doc, retrieved);
        }

        [TestMethod]
        public void IsOpen_IsCaseInsensitiveOnPaths()
        {
            var manager = new NotebookDocumentManager();
            var doc = NotebookDocument.Create(@"C:\notebooks\Book.dib", NotebookFormat.Dib);
            manager.RegisterDocument(@"C:\notebooks\Book.dib", doc);

            // Lookup with different casing must still succeed on Windows.
            Assert.IsTrue(manager.IsOpen(@"C:\notebooks\book.dib"));
        }

        // ── CloseAsync ───────────────────────────────────────────────────────

        [TestMethod]
        public void CloseAsync_AfterRegister_RemovesDocument()
        {
            var manager = new NotebookDocumentManager();
            var doc = NotebookDocument.Create(@"C:\notebooks\close-me.dib", NotebookFormat.Dib);
            manager.RegisterDocument(@"C:\notebooks\close-me.dib", doc);

            manager.CloseAsync(@"C:\notebooks\close-me.dib").GetAwaiter().GetResult();

            Assert.IsFalse(manager.IsOpen(@"C:\notebooks\close-me.dib"));
        }

        [TestMethod]
        public void CloseAsync_AfterRegister_FiresDocumentClosedEvent()
        {
            var manager = new NotebookDocumentManager();
            var doc = NotebookDocument.Create(@"C:\notebooks\close-event.dib", NotebookFormat.Dib);
            manager.RegisterDocument(@"C:\notebooks\close-event.dib", doc);
            string? closedPath = null;
            manager.DocumentClosed += (s, e) => closedPath = e.FilePath;

            manager.CloseAsync(@"C:\notebooks\close-event.dib").GetAwaiter().GetResult();

            Assert.IsNotNull(closedPath, "DocumentClosed event must fire");
        }

        [TestMethod]
        public void CloseAsync_WhenPathNotRegistered_DoesNotThrow()
        {
            var manager = new NotebookDocumentManager();

            bool threw = false;
            try
            {
                manager.CloseAsync(@"C:\notebooks\never-opened.dib").GetAwaiter().GetResult();
            }
            catch { threw = true; }
            Assert.IsFalse(threw, "CloseAsync on unknown path should be a no-op");
        }

        // ── Dirty-state event propagation ─────────────────────────────────────

        [TestMethod]
        public void DocumentDirtyChanged_WhenCellContentChanges_IsFired()
        {
            var manager = new NotebookDocumentManager();
            var doc = NotebookDocument.Create(@"C:\notebooks\dirty.dib", NotebookFormat.Dib);
            var cell = doc.AddCell(CellKind.Code, "csharp");
            // AddCell marks the document dirty.  Reset it so the subsequent content
            // change can trigger a fresh dirty→event cycle.
            doc.MarkClean();
            manager.RegisterDocument(@"C:\notebooks\dirty.dib", doc);

            bool eventFired = false;
            manager.DocumentDirtyChanged += (s, e) => eventFired = true;

            // Modifying cell content marks the document dirty and fires the event.
            cell.Contents = "Console.WriteLine(\"hello\");";

            Assert.IsTrue(eventFired, "DocumentDirtyChanged must fire when document becomes dirty");
        }

        // ── Rename scenario ───────────────────────────────────────────────────

        [TestMethod]
        public void RegisterDocument_AfterCloseOldPath_AllowsRenameScenario()
        {
            var manager = new NotebookDocumentManager();
            var doc = NotebookDocument.Create(@"C:\notebooks\old-name.dib", NotebookFormat.Dib);
            manager.RegisterDocument(@"C:\notebooks\old-name.dib", doc);

            // Simulate rename: close old, re-register under new path.
            manager.CloseAsync(@"C:\notebooks\old-name.dib").GetAwaiter().GetResult();
            doc.FilePath = @"C:\notebooks\new-name.dib";
            manager.RegisterDocument(@"C:\notebooks\new-name.dib", doc);

            Assert.IsFalse(manager.IsOpen(@"C:\notebooks\old-name.dib"),
                "Old path must no longer be tracked");
            Assert.IsTrue(manager.IsOpen(@"C:\notebooks\new-name.dib"),
                "New path must be tracked after rename");
            Assert.AreSame(doc, manager.GetDocument(@"C:\notebooks\new-name.dib"));
        }
    }
}

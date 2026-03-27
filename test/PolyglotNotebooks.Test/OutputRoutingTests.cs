using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyglotNotebooks.Editor;
using PolyglotNotebooks.Models;

#pragma warning disable MSTEST0037

namespace PolyglotNotebooks.Test
{
    /// <summary>
    /// Tests for output routing and rendering logic.
    ///
    /// Two layers are covered:
    ///  1. Model layer — <see cref="CellOutput"/> / <see cref="FormattedOutput"/> construction
    ///     and MIME-type tracking.  These are plain C# objects and run on any thread.
    ///  2. WPF layer — <see cref="OutputControl"/> visibility behavior, tested on an STA
    ///     background thread to satisfy WPF's apartment requirement.
    /// </summary>
    [TestClass]
    public class OutputRoutingTests
    {
        // ── FormattedOutput model ─────────────────────────────────────────────

        [TestMethod]
        public void FormattedOutput_WhenCreated_StoresMimeType()
        {
            var fo = new FormattedOutput("text/plain", "hello");
            Assert.AreEqual("text/plain", fo.MimeType);
        }

        [TestMethod]
        public void FormattedOutput_WhenCreated_StoresValue()
        {
            var fo = new FormattedOutput("text/plain", "hello world");
            Assert.AreEqual("hello world", fo.Value);
        }

        [TestMethod]
        public void FormattedOutput_DefaultSuppressDisplay_IsFalse()
        {
            var fo = new FormattedOutput("text/html", "<b>bold</b>");
            Assert.IsFalse(fo.SuppressDisplay);
        }

        [TestMethod]
        public void FormattedOutput_WhenSuppressDisplayTrue_IsTrue()
        {
            var fo = new FormattedOutput("text/plain", "42", suppressDisplay: true);
            Assert.IsTrue(fo.SuppressDisplay);
        }

        [TestMethod]
        public void FormattedOutput_TextHtml_MimeTypeStoredVerbatim()
        {
            var fo = new FormattedOutput("text/html", "<p>hi</p>");
            Assert.AreEqual("text/html", fo.MimeType);
        }

        [TestMethod]
        public void FormattedOutput_ApplicationJson_MimeTypeStoredVerbatim()
        {
            var fo = new FormattedOutput("application/json", "{\"key\":1}");
            Assert.AreEqual("application/json", fo.MimeType);
        }

        [TestMethod]
        public void FormattedOutput_ImagePng_MimeTypeStoredVerbatim()
        {
            var fo = new FormattedOutput("image/png", "base64data");
            Assert.AreEqual("image/png", fo.MimeType);
        }

        // ── CellOutput model ──────────────────────────────────────────────────

        [TestMethod]
        public void CellOutput_WhenCreatedWithKind_StoresKind()
        {
            var output = new CellOutput(CellOutputKind.ReturnValue, new List<FormattedOutput>());
            Assert.AreEqual(CellOutputKind.ReturnValue, output.Kind);
        }

        [TestMethod]
        public void CellOutput_WhenCreatedWithStdout_StoresKind()
        {
            var output = new CellOutput(CellOutputKind.StandardOutput,
                new List<FormattedOutput> { new FormattedOutput("text/plain", "out") });
            Assert.AreEqual(CellOutputKind.StandardOutput, output.Kind);
        }

        [TestMethod]
        public void CellOutput_WhenCreatedWithStderr_StoresKind()
        {
            var output = new CellOutput(CellOutputKind.StandardError,
                new List<FormattedOutput> { new FormattedOutput("text/plain", "err") });
            Assert.AreEqual(CellOutputKind.StandardError, output.Kind);
        }

        [TestMethod]
        public void CellOutput_WhenCreatedWithError_StoresKind()
        {
            var output = new CellOutput(CellOutputKind.Error,
                new List<FormattedOutput> { new FormattedOutput("text/plain", "Unhandled exception") });
            Assert.AreEqual(CellOutputKind.Error, output.Kind);
        }

        [TestMethod]
        public void CellOutput_WhenCreatedWithDisplay_StoresKind()
        {
            var output = new CellOutput(CellOutputKind.Display,
                new List<FormattedOutput> { new FormattedOutput("text/html", "<em>hi</em>") });
            Assert.AreEqual(CellOutputKind.Display, output.Kind);
        }

        [TestMethod]
        public void CellOutput_WhenNullFormattedValues_FormattedValuesIsEmpty()
        {
            var output = new CellOutput(CellOutputKind.ReturnValue, null!);
            Assert.IsNotNull(output.FormattedValues);
            Assert.AreEqual(0, output.FormattedValues.Count);
        }

        [TestMethod]
        public void CellOutput_WhenValueIdProvided_StoresValueId()
        {
            var output = new CellOutput(CellOutputKind.ReturnValue,
                new List<FormattedOutput>(), valueId: "val-42");
            Assert.AreEqual("val-42", output.ValueId);
        }

        [TestMethod]
        public void CellOutput_WhenNoValueId_ValueIdIsNull()
        {
            var output = new CellOutput(CellOutputKind.ReturnValue, new List<FormattedOutput>());
            Assert.IsNull(output.ValueId);
        }

        [TestMethod]
        public void CellOutput_WhenMultipleFormattedValues_AllStored()
        {
            var values = new List<FormattedOutput>
            {
                new FormattedOutput("text/plain", "42"),
                new FormattedOutput("text/html", "<b>42</b>"),
                new FormattedOutput("application/json", "42")
            };
            var output = new CellOutput(CellOutputKind.ReturnValue, values);
            Assert.AreEqual(3, output.FormattedValues.Count);
        }

        // ── Output accumulation in NotebookCell ───────────────────────────────

        [TestMethod]
        public void NotebookCell_Outputs_InitiallyEmpty()
        {
            var cell = new NotebookCell(CellKind.Code, "csharp");
            Assert.AreEqual(0, cell.Outputs.Count);
        }

        [TestMethod]
        public void NotebookCell_Outputs_AccumulatesMultipleOutputs()
        {
            var cell = new NotebookCell(CellKind.Code, "csharp");

            cell.Outputs.Add(new CellOutput(CellOutputKind.StandardOutput,
                new List<FormattedOutput> { new FormattedOutput("text/plain", "line 1") }));
            cell.Outputs.Add(new CellOutput(CellOutputKind.StandardOutput,
                new List<FormattedOutput> { new FormattedOutput("text/plain", "line 2") }));
            cell.Outputs.Add(new CellOutput(CellOutputKind.ReturnValue,
                new List<FormattedOutput> { new FormattedOutput("text/plain", "42") }));

            Assert.AreEqual(3, cell.Outputs.Count);
        }

        [TestMethod]
        public void NotebookCell_Outputs_CanBeClearedBetweenRuns()
        {
            var cell = new NotebookCell(CellKind.Code, "csharp");
            cell.Outputs.Add(new CellOutput(CellOutputKind.StandardOutput,
                new List<FormattedOutput> { new FormattedOutput("text/plain", "stale") }));

            cell.Outputs.Clear();

            Assert.AreEqual(0, cell.Outputs.Count);
        }

        [TestMethod]
        public void NotebookCell_Outputs_ErrorOutputHasErrorKind()
        {
            var cell = new NotebookCell(CellKind.Code, "csharp");
            var errorOutput = new CellOutput(CellOutputKind.Error,
                new List<FormattedOutput> { new FormattedOutput("text/plain", "System.Exception") });

            cell.Outputs.Add(errorOutput);

            Assert.AreEqual(CellOutputKind.Error, cell.Outputs[0].Kind);
            Assert.AreEqual("System.Exception", cell.Outputs[0].FormattedValues[0].Value);
        }

        // ── OutputControl WPF visibility ──────────────────────────────────────
        //
        // OutputControl.Rebuild() directly references VsBrushes (VS SDK type).  The
        // CLR JIT-compiles the entire method body when first called, so any test that
        // sets OutputControl.Cell triggers the JIT of Rebuild(), which fails with
        // FileNotFoundException for Microsoft.VisualStudio.Shell.15.0.
        //
        // Only the zero-Cell construction path (no Rebuild() call) is testable here.
        // The full Visibility-toggle contract is validated by VS-hosted integration tests.

        [TestMethod]
        public void OutputControl_WhenCellIsNull_VisibilityIsCollapsed()
        {
            Exception? capturedException = null;

            var thread = new Thread(() =>
            {
                try
                {
                    var control = new OutputControl();
                    // Cell is null by default — the control must start collapsed.
                    Assert.AreEqual(Visibility.Collapsed, control.Visibility);
                }
                catch (Exception ex)
                {
                    capturedException = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (capturedException != null)
                throw capturedException;
        }
    }
}

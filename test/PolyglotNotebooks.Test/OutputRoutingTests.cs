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

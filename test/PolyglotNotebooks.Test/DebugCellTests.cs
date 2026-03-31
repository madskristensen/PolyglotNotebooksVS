using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyglotNotebooks.Execution;
using System.Reflection;

namespace PolyglotNotebooks.Test
{
    /// <summary>
    /// Tests for cell-level debugging support.
    /// Since the main execution paths require VS SDK runtime (ThreadHelper, IVsDebugger2),
    /// these tests focus on the static helper methods that determine debuggability.
    /// </summary>
    [TestClass]
    public class DebugCellTests
    {
        // ── IsDebuggableKernel helper ─────────────────────────────────────────

        [TestMethod]
        public void IsDebuggableKernel_WhenCSharp_ReturnsTrue()
        {
            Assert.IsTrue(InvokeIsDebuggableKernel("csharp"));
        }

        [TestMethod]
        public void IsDebuggableKernel_WhenCSharpUpperCase_ReturnsTrue()
        {
            Assert.IsTrue(InvokeIsDebuggableKernel("CSharp"));
            Assert.IsTrue(InvokeIsDebuggableKernel("CSHARP"));
        }

        [TestMethod]
        public void IsDebuggableKernel_WhenFSharp_ReturnsTrue()
        {
            Assert.IsTrue(InvokeIsDebuggableKernel("fsharp"));
        }

        [TestMethod]
        public void IsDebuggableKernel_WhenFSharpUpperCase_ReturnsTrue()
        {
            Assert.IsTrue(InvokeIsDebuggableKernel("FSharp"));
            Assert.IsTrue(InvokeIsDebuggableKernel("FSHARP"));
        }

        [TestMethod]
        public void IsDebuggableKernel_WhenPowerShell_ReturnsTrue()
        {
            Assert.IsTrue(InvokeIsDebuggableKernel("pwsh"));
            Assert.IsTrue(InvokeIsDebuggableKernel("powershell"));
        }

        [TestMethod]
        public void IsDebuggableKernel_WhenPowerShellUpperCase_ReturnsTrue()
        {
            Assert.IsTrue(InvokeIsDebuggableKernel("PWSH"));
            Assert.IsTrue(InvokeIsDebuggableKernel("PowerShell"));
        }

        [TestMethod]
        public void IsDebuggableKernel_WhenJavaScript_ReturnsFalse()
        {
            Assert.IsFalse(InvokeIsDebuggableKernel("javascript"));
            Assert.IsFalse(InvokeIsDebuggableKernel("js"));
        }

        [TestMethod]
        public void IsDebuggableKernel_WhenSQL_ReturnsFalse()
        {
            Assert.IsFalse(InvokeIsDebuggableKernel("sql"));
            Assert.IsFalse(InvokeIsDebuggableKernel("mssql"));
        }

        [TestMethod]
        public void IsDebuggableKernel_WhenHTML_ReturnsFalse()
        {
            Assert.IsFalse(InvokeIsDebuggableKernel("html"));
        }

        [TestMethod]
        public void IsDebuggableKernel_WhenMarkdown_ReturnsFalse()
        {
            Assert.IsFalse(InvokeIsDebuggableKernel("markdown"));
        }

        [TestMethod]
        public void IsDebuggableKernel_WhenNull_ReturnsFalse()
        {
            Assert.IsFalse(InvokeIsDebuggableKernel(null));
        }

        [TestMethod]
        public void IsDebuggableKernel_WhenEmpty_ReturnsFalse()
        {
            Assert.IsFalse(InvokeIsDebuggableKernel(""));
        }

        [TestMethod]
        public void IsDebuggableKernel_WhenUnknownKernel_ReturnsFalse()
        {
            Assert.IsFalse(InvokeIsDebuggableKernel("unknown"));
            Assert.IsFalse(InvokeIsDebuggableKernel("ruby"));
            Assert.IsFalse(InvokeIsDebuggableKernel("python"));
        }

        // ── Helper to invoke private method via reflection ────────────────────

        /// <summary>
        /// Invokes the private IsDebuggableKernel method on CellExecutionEngine via reflection.
        /// </summary>
        private static bool InvokeIsDebuggableKernel(string? kernelName)
        {
            var engineType = typeof(CellExecutionEngine);
            var method = engineType.GetMethod("IsDebuggableKernel",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method, "IsDebuggableKernel method not found on CellExecutionEngine");

            var result = method.Invoke(null, new object?[] { kernelName });
            return (bool)result!;
        }
    }
}

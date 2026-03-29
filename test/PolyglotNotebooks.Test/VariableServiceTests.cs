using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyglotNotebooks.Variables;

#pragma warning disable MSTEST0037

namespace PolyglotNotebooks.Test
{
    /// <summary>
    /// Tests for <see cref="VariableService"/>.
    ///
    /// NOTE ON MOCK LIMITATIONS:
    /// VariableService depends on <c>KernelClient</c>, which is a sealed concrete class
    /// with no interface.  Moq cannot mock sealed types, so we cannot substitute
    /// RequestValueInfosAsync or RequestValueAsync calls.  Protocol-level tests would
    /// require an IKernelClient interface or an integration test harness.
    ///
    /// VariableService also uses ThreadHelper.JoinableTaskFactory internally, which
    /// is not available outside the VS process.  Lifecycle tests use NoInlining wrappers
    /// to avoid JIT-loading VS SDK types in non-VS SDK code paths.
    ///
    /// These tests cover:
    ///   • Truncate — the pure string-truncation helper (promoted to internal static)
    ///   • Lifecycle — Initialize, Current, Dispose, SetKnownKernels (null-client paths)
    ///   • Edge cases and null safety
    /// </summary>
    [TestClass]
    public class VariableServiceTests
    {
        // ====================================================================
        // Truncate — pure helper (promoted from private static to internal static)
        // ====================================================================

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string CallTruncate(string value, int max) =>
            VariableService.Truncate(value, max);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string CallTruncateDefault(string value) =>
            VariableService.Truncate(value);

        [TestMethod]
        public void Truncate_ShortValue_ReturnsUnchanged()
        {
            var result = CallTruncate("hello", 100);
            Assert.AreEqual("hello", result);
        }

        [TestMethod]
        public void Truncate_ExactlyAtMax_ReturnsUnchanged()
        {
            var value = new string('x', 100);
            var result = CallTruncate(value, 100);
            Assert.AreEqual(value, result);
        }

        [TestMethod]
        public void Truncate_OneBeyondMax_ReturnsTruncatedWithEllipsis()
        {
            var value = new string('a', 101);
            var result = CallTruncate(value, 100);
            Assert.AreEqual(100 + 1, result.Length); // 100 chars + ellipsis char
            Assert.IsTrue(result.EndsWith("…"));
            Assert.AreEqual(new string('a', 100) + "…", result);
        }

        [TestMethod]
        public void Truncate_VeryLongValue_TruncatesToMaxPlusEllipsis()
        {
            var value = new string('z', 500);
            var result = CallTruncate(value, 100);
            Assert.AreEqual(101, result.Length);
            Assert.IsTrue(result.StartsWith(new string('z', 100)));
            Assert.IsTrue(result.EndsWith("…"));
        }

        [TestMethod]
        public void Truncate_EmptyString_ReturnsEmpty()
        {
            var result = CallTruncate("", 100);
            Assert.AreEqual("", result);
        }

        [TestMethod]
        public void Truncate_SingleCharWithMax1_ReturnsUnchanged()
        {
            var result = CallTruncate("x", 1);
            Assert.AreEqual("x", result);
        }

        [TestMethod]
        public void Truncate_TwoCharsWithMax1_ReturnsTruncated()
        {
            var result = CallTruncate("ab", 1);
            Assert.AreEqual("a…", result);
        }

        [TestMethod]
        public void Truncate_DefaultMaxIs100()
        {
            // Default max parameter is 100
            var shortValue = new string('x', 100);
            Assert.AreEqual(shortValue, CallTruncateDefault(shortValue));

            var longValue = new string('y', 101);
            var result = CallTruncateDefault(longValue);
            Assert.AreEqual(101, result.Length);
            Assert.IsTrue(result.EndsWith("…"));
        }

        [TestMethod]
        public void Truncate_UnicodeContent_TruncatesCorrectly()
        {
            var value = new string('日', 150);
            var result = CallTruncate(value, 50);
            Assert.AreEqual(51, result.Length);
            Assert.IsTrue(result.EndsWith("…"));
        }

        [TestMethod]
        public void Truncate_NewlinesInValue_TruncatesAtMax()
        {
            var value = "line1\nline2\nline3\nline4\nline5\nline6\nline7\nline8\nline9\nline10" +
                        "line11\nline12\nline13\nline14\nline15\nline16\nline17\nline18";
            var result = CallTruncate(value, 20);
            Assert.AreEqual(21, result.Length);
            Assert.IsTrue(result.EndsWith("…"));
        }

        // ====================================================================
        // VariableService lifecycle — Initialize, Current, Dispose
        //
        // These use NoInlining wrappers because VariableService references
        // ThreadHelper in method bodies.  The paths tested here (construction,
        // disposal, SetKnownKernels) never execute VS SDK code, but JIT could
        // attempt to resolve the types if inlined into the test method.
        // ====================================================================

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CallInitialize() => VariableService.Initialize();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static VariableService GetCurrent() => VariableService.Current;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CallDispose(VariableService svc) => svc.Dispose();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int GetVariablesCount(VariableService svc) => svc.Variables.Count;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CallSetKnownKernels(VariableService svc, string[] kernels)
            => svc.SetKnownKernels(kernels);

        [TestMethod]
        public void Initialize_CreatesNewInstance()
        {
            CallInitialize();
            var svc = GetCurrent();
            Assert.IsNotNull(svc);
            CallDispose(svc);
        }

        [TestMethod]
        public void Initialize_CalledTwice_ReplacesInstance()
        {
            CallInitialize();
            var first = GetCurrent();

            CallInitialize();
            var second = GetCurrent();

            Assert.AreNotSame(first, second);
            CallDispose(second);
        }

        [TestMethod]
        public void Current_AfterInitialize_ReturnsSameInstance()
        {
            CallInitialize();
            var a = GetCurrent();
            var b = GetCurrent();
            Assert.AreSame(a, b);
            CallDispose(a);
        }

        [TestMethod]
        public void Variables_Collection_DefaultsToEmpty()
        {
            CallInitialize();
            var svc = GetCurrent();
            Assert.AreEqual(0, GetVariablesCount(svc));
            CallDispose(svc);
        }

        [TestMethod]
        public void Dispose_IsIdempotent()
        {
            CallInitialize();
            var svc = GetCurrent();
            CallDispose(svc);

            bool threw = false;
            try { CallDispose(svc); }
            catch { threw = true; }
            Assert.IsFalse(threw, "Second Dispose() must not throw");
        }

        [TestMethod]
        public void Dispose_ClearsCurrentSingleton()
        {
            CallInitialize();
            var svc = GetCurrent();
            CallDispose(svc);

            // Accessing Current after dispose should create a new instance
            var fresh = GetCurrent();
            Assert.AreNotSame(svc, fresh);

            CallDispose(fresh);
        }

        // ====================================================================
        // SetKnownKernels
        // ====================================================================

        [TestMethod]
        public void SetKnownKernels_DoesNotThrow()
        {
            CallInitialize();
            var svc = GetCurrent();

            bool threw = false;
            try { CallSetKnownKernels(svc, new[] { "csharp", "fsharp", "pwsh" }); }
            catch { threw = true; }
            Assert.IsFalse(threw, "SetKnownKernels must not throw");

            CallDispose(svc);
        }

        [TestMethod]
        public void SetKnownKernels_WithEmptyList_DoesNotThrow()
        {
            CallInitialize();
            var svc = GetCurrent();

            bool threw = false;
            try { CallSetKnownKernels(svc, Array.Empty<string>()); }
            catch { threw = true; }
            Assert.IsFalse(threw, "SetKnownKernels with empty list must not throw");

            CallDispose(svc);
        }

        // NOTE: RefreshVariablesAsync, GetFullValueAsync, and SendVariableAsync all
        // have null-client guards (`if (_kernelClient == null) return;`) that short-
        // circuit before any VS SDK or KernelClient code.  However, JIT resolves
        // Microsoft.VisualStudio.Threading for the full method body, which is not
        // available in the test runner.  These methods are verified by code inspection
        // rather than unit tests.
    }
}

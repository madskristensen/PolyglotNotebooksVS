using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyglotNotebooks.IntelliSense;
using PolyglotNotebooks.Protocol;

#pragma warning disable MSTEST0037

namespace PolyglotNotebooks.Test
{
    [TestClass]
    public class KernelInfoCacheTests
    {
        /// <summary>
        /// KernelInfoCache.Default is a singleton. Tests mutate it, so we must
        /// reset after every test to avoid cross-test pollution.
        /// </summary>
        [TestCleanup]
        public void Cleanup()
        {
            KernelInfoCache.Default.Reset();
        }

        // ── GetAvailableKernels ──────────────────────────────────────

        [TestMethod]
        public void GetAvailableKernels_BeforePopulate_ReturnsFallbackDefaults()
        {
            var kernels = KernelInfoCache.Default.GetAvailableKernels();

            Assert.IsNotNull(kernels);
            Assert.IsTrue(kernels.Count > 0, "Fallback list should not be empty");
        }

        // ── FallbackList contents ────────────────────────────────────

        [TestMethod]
        public void FallbackList_ContainsExpectedDefaults()
        {
            var kernels = KernelInfoCache.Default.GetAvailableKernels();

            var expected = new[] { "csharp", "fsharp", "pwsh", "javascript", "sql", "kql", "html" };
            foreach (var name in expected)
            {
                bool found = false;
                foreach (var k in kernels)
                {
                    if (k == name) { found = true; break; }
                }
                Assert.IsTrue(found, $"Expected fallback kernel '{name}' not found");
            }
        }

        // ── Populate ─────────────────────────────────────────────────

        [TestMethod]
        public void Populate_Null_DoesNotChangeCache()
        {
            var before = KernelInfoCache.Default.GetAvailableKernels();

            KernelInfoCache.Default.Populate(null!);

            var after = KernelInfoCache.Default.GetAvailableKernels();
            Assert.AreSame(before, after, "Cache reference should not change when Populate receives null");
        }

        [TestMethod]
        public void Populate_EmptyList_DoesNotChangeCache()
        {
            var before = KernelInfoCache.Default.GetAvailableKernels();

            var ready = new KernelReady { KernelInfos = new List<KernelInfo>() };
            KernelInfoCache.Default.Populate(ready);

            var after = KernelInfoCache.Default.GetAvailableKernels();
            Assert.AreSame(before, after, "Cache reference should not change when Populate receives empty list");
        }

        [TestMethod]
        public void Populate_ValidKernelInfos_UpdatesCache()
        {
            var ready = new KernelReady
            {
                KernelInfos = new List<KernelInfo>
                {
                    new KernelInfo { LocalName = "python" },
                    new KernelInfo { LocalName = "ruby" },
                }
            };

            KernelInfoCache.Default.Populate(ready);

            var kernels = KernelInfoCache.Default.GetAvailableKernels();
            Assert.AreEqual(2, kernels.Count);
            Assert.AreEqual("python", kernels[0]);
            Assert.AreEqual("ruby", kernels[1]);
        }

        [TestMethod]
        public void Populate_ValidKernelInfos_FiresKernelsChanged()
        {
            bool fired = false;
            KernelInfoCache.Default.KernelsChanged += () => fired = true;

            var ready = new KernelReady
            {
                KernelInfos = new List<KernelInfo>
                {
                    new KernelInfo { LocalName = "go" },
                }
            };
            KernelInfoCache.Default.Populate(ready);

            Assert.IsTrue(fired, "KernelsChanged event should fire on valid Populate");
        }

        [TestMethod]
        public void Populate_WhitespaceOnlyNames_Filtered()
        {
            var ready = new KernelReady
            {
                KernelInfos = new List<KernelInfo>
                {
                    new KernelInfo { LocalName = "  " },
                    new KernelInfo { LocalName = "" },
                    new KernelInfo { LocalName = "rust" },
                }
            };

            KernelInfoCache.Default.Populate(ready);

            var kernels = KernelInfoCache.Default.GetAvailableKernels();
            Assert.AreEqual(1, kernels.Count, "Only non-whitespace names should survive");
            Assert.AreEqual("rust", kernels[0]);
        }

        // ── Reset ────────────────────────────────────────────────────

        [TestMethod]
        public void Reset_RestoresFallbackList()
        {
            var ready = new KernelReady
            {
                KernelInfos = new List<KernelInfo>
                {
                    new KernelInfo { LocalName = "custom1" },
                }
            };
            KernelInfoCache.Default.Populate(ready);

            KernelInfoCache.Default.Reset();

            var kernels = KernelInfoCache.Default.GetAvailableKernels();
            Assert.IsTrue(kernels.Count >= 7, "After reset, the fallback list should be restored");
            bool hasCsharp = false;
            foreach (var k in kernels)
            {
                if (k == "csharp") { hasCsharp = true; break; }
            }
            Assert.IsTrue(hasCsharp, "Fallback list should contain 'csharp' after reset");
        }

        [TestMethod]
        public void Reset_FiresKernelsChanged()
        {
            bool fired = false;
            KernelInfoCache.Default.KernelsChanged += () => fired = true;

            KernelInfoCache.Default.Reset();

            Assert.IsTrue(fired, "KernelsChanged event should fire on Reset");
        }

        // ── Edge cases ───────────────────────────────────────────────

        [TestMethod]
        public void Populate_AllWhitespaceNames_DoesNotChangeCache()
        {
            var before = KernelInfoCache.Default.GetAvailableKernels();

            var ready = new KernelReady
            {
                KernelInfos = new List<KernelInfo>
                {
                    new KernelInfo { LocalName = "   " },
                    new KernelInfo { LocalName = "" },
                }
            };
            KernelInfoCache.Default.Populate(ready);

            var after = KernelInfoCache.Default.GetAvailableKernels();
            Assert.AreSame(before, after,
                "When all names are whitespace-only, cache reference should not change");
        }
    }
}

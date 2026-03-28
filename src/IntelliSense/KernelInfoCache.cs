using PolyglotNotebooks.Protocol;

using System.Collections.Generic;

namespace PolyglotNotebooks.IntelliSense
{
    /// <summary>
    /// Caches the list of available kernels reported by dotnet-interactive on startup.
    /// Pre-populated with known defaults; refreshed when the kernel sends KernelReady.
    /// Thread-safe: reads and writes via a volatile reference swap.
    /// </summary>
    internal sealed class KernelInfoCache
    {
        // Must be declared BEFORE Default so it's initialized first
        private static readonly IReadOnlyList<string> _fallbackKernels = new[]
        {
            "csharp", "fsharp", "pwsh", "html"
        };

        public static readonly KernelInfoCache Default = new KernelInfoCache();

        private volatile IReadOnlyList<string> _kernels;

        private KernelInfoCache()
        {
            _kernels = _fallbackKernels;
        }

        /// <summary>
        /// Fired (potentially on a background thread) when the cached kernel list changes.
        /// Subscribers should marshal to the UI thread before updating UI.
        /// </summary>
        public event Action? KernelsChanged;

        /// <summary>Returns the current list of available canonical kernel names.</summary>
        public IReadOnlyList<string> GetAvailableKernels() => _kernels ?? _fallbackKernels;

        /// <summary>
        /// Populates the cache from a KernelReady event payload. Ignored if the event contains
        /// no useful kernel infos, so the fallback defaults remain active until the first real response.
        /// </summary>
        public void Populate(KernelReady ready)
        {
            if (ready?.KernelInfos == null || ready.KernelInfos.Count == 0)
                return;

            var names = new List<string>(ready.KernelInfos.Count);
            foreach (var ki in ready.KernelInfos)
            {
                if (!string.IsNullOrWhiteSpace(ki.LocalName))
                    names.Add(ki.LocalName);
            }

            if (names.Count == 0)
                return;

            _kernels = names;
            KernelsChanged?.Invoke();
        }

        /// <summary>
        /// Resets to the default kernel list — call after a kernel restart so the UI
        /// shows sensible defaults while the new kernel initialises.
        /// </summary>
        public void Reset()
        {
            _kernels = _fallbackKernels;
            KernelsChanged?.Invoke();
        }
    }
}

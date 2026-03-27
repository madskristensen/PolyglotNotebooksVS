using System;
using System.Collections.Generic;

namespace PolyglotNotebooks.Kernel
{
    public sealed class KernelConnectionInfo
    {
        public int ProcessId { get; set; }
        public DateTime StartTime { get; set; }
        public string WorkingDirectory { get; set; } = string.Empty;
        public string DotnetInteractiveVersion { get; set; } = string.Empty;

        /// <summary>Populated after KernelReady event is received from the protocol layer.</summary>
        public IReadOnlyList<string> AvailableKernels { get; set; } = Array.Empty<string>();
    }
}

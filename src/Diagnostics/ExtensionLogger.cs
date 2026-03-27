using System;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.Shell;

namespace PolyglotNotebooks.Diagnostics
{
    /// <summary>
    /// Centralized logging helper that writes to the Visual Studio ActivityLog.
    /// All methods are safe to call from any thread and swallow logging failures
    /// (including when VS assemblies are not present, e.g. during unit tests).
    /// </summary>
    public static class ExtensionLogger
    {
        private const string Source = "PolyglotNotebooks";

        /// <summary>Logs an informational message.</summary>
        public static void LogInfo(string context, string message)
        {
            try { DoLogInformation(Source, FormatMessage(context, message)); }
            catch { /* VS ActivityLog unavailable (e.g. test runner) or before package init */ }
        }

        /// <summary>Logs a warning message.</summary>
        public static void LogWarning(string context, string message)
        {
            try { DoLogWarning(Source, FormatMessage(context, message)); }
            catch { }
        }

        /// <summary>Logs an error message.</summary>
        public static void LogError(string context, string message)
        {
            try { DoLogError(Source, FormatMessage(context, message)); }
            catch { }
        }

        /// <summary>Logs an exception with a descriptive message.</summary>
        public static void LogException(string context, string message, Exception exception)
        {
            if (exception is null)
            {
                LogError(context, message);
                return;
            }

            var text = FormatMessage(context,
                $"{message}: [{exception.GetType().Name}] {exception.Message}");
            try { DoLogError(Source, text); }
            catch { }
        }

        private static string FormatMessage(string context, string message)
            => $"[{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}] [{context}] {message}";

        // NoInlining defers JIT compilation of these methods so that a FileNotFoundException
        // thrown when VS assemblies are absent is caught by the callers above, not during
        // LogInfo/LogWarning/LogError's own JIT compilation.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DoLogInformation(string source, string message)
            => ActivityLog.LogInformation(source, message);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DoLogWarning(string source, string message)
            => ActivityLog.LogWarning(source, message);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DoLogError(string source, string message)
            => ActivityLog.LogError(source, message);
    }
}

using Microsoft.VisualStudio.Shell;
using PolyglotNotebooks.Diagnostics;
using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace PolyglotNotebooks.Debugging
{
    /// <summary>
    /// Helper for attaching the Visual Studio managed debugger to a running process.
    /// Uses the DTE automation model (EnvDTE) which is the proven, reliable way to
    /// attach to an already-running process from a VS extension.
    /// </summary>
    internal static class DebuggerAttacher
    {
        // Tracks whether JMC was enabled before we disabled it, so we can restore.
        private static bool? _previousJustMyCodeSetting;

        /// <summary>
        /// Writes to both ExtensionLogger and System.Diagnostics.Debug so messages
        /// appear in the VS Debug output pane when debugging the experimental instance.
        /// </summary>
        private static void DebugLog(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[PolyglotNotebooks.Debugger] {message}");
            ExtensionLogger.LogInfo(nameof(DebuggerAttacher), message);
        }

        /// <summary>
        /// Attaches the VS managed debugger to the process with the given PID.
        /// Tries Attach2 with the CoreCLR engine GUID, falling back to Attach().
        /// After attaching, waits until the debugger is confirmed attached before returning.
        /// </summary>
        public static async Task<bool> AttachAsync(int processId)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (processId <= 0)
            {
                DebugLog($"Invalid process ID: {processId}. Cannot attach debugger.");
                return false;
            }

            if (IsAttached(processId))
            {
                DebugLog($"Debugger is already attached to process {processId}.");
                return true;
            }

            try
            {
                var dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null)
                {
                    DebugLog("ERROR: Failed to get DTE service.");
                    return false;
                }

                EnvDTE.Process targetProcess = null;
                foreach (EnvDTE.Process proc in dte.Debugger.LocalProcesses)
                {
                    if (proc.ProcessID == processId)
                    {
                        targetProcess = proc;
                        break;
                    }
                }

                if (targetProcess == null)
                {
                    DebugLog($"ERROR: Process {processId} not found in LocalProcesses.");
                    return false;
                }

                DebugLog($"Attaching debugger to process {processId} ({targetProcess.Name})...");

                // Log available debug engines for diagnostic purposes.
                LogAvailableEngines(dte);

                // Attempt attach with the CoreCLR managed debug engine for .NET 5+/Core processes.
                // Use Attach2 with the engine GUID directly — display names vary by VS version.
                string engineUsed = AttachWithManagedEngine(targetProcess);
                DebugLog($"Attached using: {engineUsed}");

                // Wait for the debugger to fully settle.
                await WaitForAttachConfirmationAsync(processId).ConfigureAwait(false);

                DebugLog($"Successfully attached debugger to process {processId}.");
                return true;
            }
            catch (Exception ex)
            {
                DebugLog($"EXCEPTION attaching to {processId}: [{ex.GetType().Name}] {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Tries to attach using Attach2 with known managed engine identifiers,
        /// falling back to auto-detect Attach(). Returns a description of what worked.
        /// Note: Managed/Native mixed-mode doesn't work well for .NET Core processes
        /// because it requires native PDBs. The managed-only engine resolves managed
        /// frames correctly, even if the top frame shows "[Managed to Native Transition]".
        /// </summary>
        private static string AttachWithManagedEngine(EnvDTE.Process targetProcess)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string[] engineNames = new[]
            {
                "Managed (.NET Core, .NET 5+)",    // VS 2022+ CoreCLR
                "Managed (CoreCLR)",               // Older VS naming
                "Managed",                         // Generic managed
            };

            if (targetProcess is EnvDTE80.Process2 process2)
            {
                foreach (var name in engineNames)
                {
                    try
                    {
                        process2.Attach2(name);
                        return $"Attach2(\"{name}\")";
                    }
                    catch (Exception ex)
                    {
                        DebugLog($"Attach2(\"{name}\") failed: {ex.Message}");
                    }
                }
            }

            // All Attach2 attempts failed — fall back to auto-detect.
            targetProcess.Attach();
            return "Attach() [auto-detect]";
        }

        /// <summary>
        /// Logs available debug engines from the default transport for diagnostic purposes.
        /// </summary>
        private static void LogAvailableEngines(EnvDTE.DTE dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (dte.Debugger is EnvDTE80.Debugger2 debugger2)
                {
                    var transports = debugger2.Transports;
                    foreach (EnvDTE80.Transport transport in transports)
                    {
                        if (transport.Name == "Default")
                        {
                            var engines = transport.Engines;
                            for (int i = 1; i <= engines.Count; i++)
                            {
                                var engine = engines.Item(i);
                                DebugLog($"Available engine [{i}]: Name=\"{engine.Name}\", ID={engine.ID}");
                            }
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog($"Could not enumerate engines: {ex.Message}");
            }
        }

        /// <summary>
        /// Polls until the debugger is confirmed attached to the given process,
        /// or a timeout (3 seconds) is reached.
        /// </summary>
        private static async Task WaitForAttachConfirmationAsync(int processId)
        {
            const int maxWaitMs = 3000;
            const int pollIntervalMs = 100;
            int waited = 0;

            while (waited < maxWaitMs)
            {
                await Task.Delay(pollIntervalMs).ConfigureAwait(false);
                waited += pollIntervalMs;

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (IsAttached(processId))
                {
                    DebugLog($"Debugger attach confirmed after {waited}ms.");
                    return;
                }
            }

            DebugLog($"WARNING: Debugger attach confirmation timed out after {maxWaitMs}ms.");
        }

        /// <summary>
        /// Issues a Break All from the VS side after a short delay.
        /// This pauses the target process wherever it is currently executing.
        /// </summary>
        public static async Task BreakAllAsync(int delayMs = 500, CancellationToken ct = default)
        {
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            try
            {
                var dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null) return;

                // Debugger5.Break(false) = break without waiting for the process to be at a known state.
                if (dte.Debugger is EnvDTE100.Debugger5 debugger5)
                {
                    debugger5.Break(false);
                    DebugLog("Issued Break All via Debugger5.Break(false).");
                }
                else
                {
                    dte.Debugger.Break();
                    DebugLog("Issued Break All via Debugger.Break().");
                }
            }
            catch (Exception ex)
            {
                DebugLog($"Failed to issue Break All: [{ex.GetType().Name}] {ex.Message}");
            }
        }

        /// <summary>
        /// Issues Break All after an initial delay, scans all threads for user code,
        /// and switches to the user-code thread if found. If no user code is found,
        /// resumes and retries with proper settling delays between state transitions.
        /// </summary>
        public static async Task BreakAllWithRetryAsync(
            int delayMs = 4000,
            int retries = 10,
            int retryIntervalMs = 2000,
            CancellationToken ct = default)
        {
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }

            for (int attempt = 0; attempt <= retries; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                // ── 1. Issue Break All if not already broken ─────────────────
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

                EnvDTE.DTE dte;
                try
                {
                    dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(EnvDTE.DTE));
                    if (dte == null) return;
                }
                catch (Exception ex)
                {
                    DebugLog($"Attempt {attempt}: cannot get DTE: {ex.Message}");
                    return;
                }

                try
                {
                    var mode = dte.Debugger.CurrentMode;
                    if (mode != EnvDTE.dbgDebugMode.dbgBreakMode)
                    {
                        if (mode == EnvDTE.dbgDebugMode.dbgDesignMode)
                        {
                            DebugLog($"Attempt {attempt}: debugger is in design mode (not attached). Aborting.");
                            return;
                        }

                        if (dte.Debugger is EnvDTE100.Debugger5 debugger5)
                        {
                            debugger5.Break(false);
                        }
                        else
                        {
                            dte.Debugger.Break();
                        }
                        DebugLog($"Issued Break All, attempt {attempt}. Waiting for break mode...");

                        // Wait for break mode to settle.
                        bool settled = false;
                        for (int wait = 0; wait < 20; wait++)
                        {
                            await Task.Delay(250, ct).ConfigureAwait(false);
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
                            try
                            {
                                if (dte.Debugger.CurrentMode == EnvDTE.dbgDebugMode.dbgBreakMode)
                                {
                                    settled = true;
                                    DebugLog($"Break mode settled after {(wait + 1) * 250}ms.");
                                    break;
                                }
                            }
                            catch { /* debugger transitioning */ }
                        }

                        if (!settled)
                        {
                            DebugLog($"Attempt {attempt}: break mode did not settle. Retrying...");
                            await Task.Delay(retryIntervalMs, ct).ConfigureAwait(false);
                            continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLog($"Attempt {attempt}: Break All failed: [{ex.GetType().Name}] {ex.Message}");
                    await Task.Delay(retryIntervalMs, ct).ConfigureAwait(false);
                    continue;
                }

                // ── 2. Scan all threads for user code ────────────────────────
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

                bool foundUserThread = false;
                try
                {
                    int threadCount = 0;
                    foreach (EnvDTE.Thread thread in dte.Debugger.CurrentProgram.Threads)
                    {
                        threadCount++;
                        try
                        {
                            var frames = thread.StackFrames;
                            if (frames == null || frames.Count == 0) continue;

                            // On first attempt, log the full stack (up to 8 frames) for diagnostics.
                            if (attempt == 0)
                            {
                                int frameIdx = 0;
                                foreach (EnvDTE.StackFrame f in frames)
                                {
                                    if (frameIdx >= 8) break;
                                    var fn = f.FunctionName ?? "(null)";
                                    var ln = "(no lang)";
                                    try { ln = f.Language ?? "(null)"; } catch { }
                                    DebugLog($"  T{thread.ID}[{frameIdx}]: {fn}  lang={ln}");
                                    frameIdx++;
                                }
                            }
                            else
                            {
                                // Subsequent attempts — just log top frame.
                                try
                                {
                                    var topFrame = frames.Item(1);
                                    DebugLog($"  [{attempt}] Thread {thread.ID}: top='{topFrame.FunctionName}'");
                                }
                                catch { }
                            }

                            foreach (EnvDTE.StackFrame frame in frames)
                            {
                                var funcName = frame.FunctionName ?? string.Empty;
                                var lang = string.Empty;
                                try { lang = frame.Language ?? string.Empty; } catch { }

                                // Skip transition/placeholder frames.
                                if (funcName.StartsWith("[") || string.IsNullOrEmpty(funcName))
                                    continue;

                                // Roslyn scripting compiles cell code into Submission#N types.
                                // These are always user code, regardless of declared language.
                                bool isSubmission = funcName.StartsWith("Submission#", StringComparison.Ordinal);

                                bool isCSharp = isSubmission ||
                                    lang.IndexOf("C#", StringComparison.OrdinalIgnoreCase) >= 0;
                                bool isKnownNamespace =
                                    funcName.StartsWith("System.", StringComparison.Ordinal) ||
                                    funcName.StartsWith("Microsoft.", StringComparison.Ordinal) ||
                                    funcName.StartsWith("Internal.", StringComparison.Ordinal) ||
                                    funcName.StartsWith("Interop.", StringComparison.Ordinal);

                                if (isCSharp && !isKnownNamespace)
                                {
                                    DebugLog($"  Found user code on thread {thread.ID}: '{funcName}' lang={lang}");
                                    dte.Debugger.CurrentThread = thread;
                                    foundUserThread = true;
                                    break;
                                }
                            }

                            if (foundUserThread) break;
                        }
                        catch (Exception ex)
                        {
                            DebugLog($"  Error inspecting thread {thread.ID}: {ex.Message}");
                        }
                    }

                    DebugLog($"Attempt {attempt}: scanned {threadCount} threads, userCodeFound={foundUserThread}");
                }
                catch (Exception ex)
                {
                    DebugLog($"Attempt {attempt}: thread scan failed: [{ex.GetType().Name}] {ex.Message}");
                }

                if (foundUserThread)
                {
                    DebugLog($"Switched to user-code thread on attempt {attempt}. Done.");
                    return;
                }

                // ── 3. No user code — resume and wait before retrying ────────
                try
                {
                    DebugLog($"Attempt {attempt}: no user code. Resuming execution...");
                    dte.Debugger.Go(false);
                }
                catch (Exception ex)
                {
                    DebugLog($"Attempt {attempt}: Go(false) failed: {ex.Message}");
                }

                // Wait for run mode to settle, then wait the retry interval.
                for (int wait = 0; wait < 20; wait++)
                {
                    await Task.Delay(250, ct).ConfigureAwait(false);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
                    try
                    {
                        if (dte.Debugger.CurrentMode == EnvDTE.dbgDebugMode.dbgRunMode)
                            break;
                    }
                    catch { /* transitioning */ }
                }

                await Task.Delay(retryIntervalMs, ct).ConfigureAwait(false);
            }

            DebugLog("Exhausted all Break All retry attempts.");
        }

        /// <summary>
        /// Checks if the VS debugger is currently attached to the process with the given PID.
        /// Must be called on the UI thread.
        /// </summary>
        public static bool IsAttached(int processId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (processId <= 0)
                return false;

            try
            {
                var dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null)
                    return false;

                foreach (EnvDTE.Process proc in dte.Debugger.DebuggedProcesses)
                {
                    if (proc.ProcessID == processId)
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Detaches the VS debugger from all debugged processes without terminating them.
        /// This ends the debug session and returns VS to design mode while leaving
        /// the kernel process alive for future cell executions.
        /// </summary>
        public static async Task DetachAllAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null) return;

                if (dte.Debugger.CurrentMode == EnvDTE.dbgDebugMode.dbgDesignMode)
                {
                    DebugLog("DetachAll: already in design mode, nothing to detach.");
                    return;
                }

                // If in break mode, resume first so Detach doesn't hang.
                if (dte.Debugger.CurrentMode == EnvDTE.dbgDebugMode.dbgBreakMode)
                {
                    dte.Debugger.Go(false);
                    // Brief wait for run mode to settle before detaching.
                    await Task.Delay(200).ConfigureAwait(false);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                }

                dte.Debugger.DetachAll();
                DebugLog("DetachAll: detached from all debugged processes.");
            }
            catch (Exception ex)
            {
                DebugLog($"DetachAll failed: [{ex.GetType().Name}] {ex.Message}");
            }
        }

        /// <summary>
        /// Waits for the debugger to hit the <c>Debugger.Break()</c> preamble (break mode),
        /// then waits for the user to continue (run mode), then detaches from the kernel
        /// and restores JMC. This runs concurrently with the terminal event wait so that
        /// the kernel is free to finish execution and send the completion event through stdio.
        /// </summary>
        public static async Task WaitForUserContinueAndDetachAsync(CancellationToken ct = default)
        {
            const int pollIntervalMs = 250;
            const int maxWaitForBreakMs = 30_000;
            const int maxWaitForContinueMs = 300_000; // 5 minutes max debug session

            // ── 1. Wait for break mode (Debugger.Break() hit) ──────────────
            bool sawBreakMode = false;
            for (int waited = 0; waited < maxWaitForBreakMs; waited += pollIntervalMs)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(pollIntervalMs, ct).ConfigureAwait(false);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

                try
                {
                    var dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(EnvDTE.DTE));
                    if (dte == null) return;

                    var mode = dte.Debugger.CurrentMode;
                    if (mode == EnvDTE.dbgDebugMode.dbgBreakMode)
                    {
                        sawBreakMode = true;
                        DebugLog("WaitForUserContinue: break mode detected (Debugger.Break() hit).");
                        break;
                    }
                    if (mode == EnvDTE.dbgDebugMode.dbgDesignMode)
                    {
                        DebugLog("WaitForUserContinue: design mode detected, debugger already detached.");
                        return;
                    }
                }
                catch { /* debugger transitioning */ }
            }

            if (!sawBreakMode)
            {
                DebugLog("WaitForUserContinue: break mode never detected, detaching.");
                await DetachAllAsync().ConfigureAwait(false);
                return;
            }

            // ── 2. Wait for user to continue (exit break mode) ─────────────
            for (int waited = 0; waited < maxWaitForContinueMs; waited += pollIntervalMs)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(pollIntervalMs, ct).ConfigureAwait(false);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

                try
                {
                    var dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(EnvDTE.DTE));
                    if (dte == null) return;

                    var mode = dte.Debugger.CurrentMode;
                    if (mode == EnvDTE.dbgDebugMode.dbgDesignMode)
                    {
                        DebugLog("WaitForUserContinue: design mode detected, debugger already detached.");
                        return;
                    }
                    if (mode == EnvDTE.dbgDebugMode.dbgRunMode)
                    {
                        DebugLog("WaitForUserContinue: user continued. Detaching...");
                        break;
                    }
                }
                catch { /* debugger transitioning */ }
            }

            // ── 3. Restore JMC and detach ──────────────────────────────────
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            RestoreJustMyCode();
            await DetachAllAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Temporarily disables "Just My Code" so that dynamically-compiled Roslyn
        /// scripting code is not filtered out by the debugger.
        /// Call <see cref="RestoreJustMyCode"/> when debugging is complete.
        /// Must be called on the UI thread.
        /// </summary>
        public static void DisableJustMyCode()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null) return;

                var props = dte.Properties["Debugging", "General"];
                var jmcProp = props.Item("EnableJustMyCode");
                bool currentValue = (bool)jmcProp.Value;

                _previousJustMyCodeSetting = currentValue;

                if (currentValue)
                {
                    jmcProp.Value = false;
                    DebugLog("Temporarily disabled 'Just My Code'.");
                }
                else
                {
                    DebugLog("'Just My Code' was already disabled.");
                }
            }
            catch (Exception ex)
            {
                DebugLog($"Failed to disable JMC: [{ex.GetType().Name}] {ex.Message}");
            }
        }

        /// <summary>
        /// Restores the "Just My Code" setting to its value before
        /// <see cref="DisableJustMyCode"/> was called. Safe to call even if
        /// <see cref="DisableJustMyCode"/> was never called.
        /// Must be called on the UI thread.
        /// </summary>
        public static void RestoreJustMyCode()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_previousJustMyCodeSetting == null)
                return;

            try
            {
                var dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null) return;

                var props = dte.Properties["Debugging", "General"];
                props.Item("EnableJustMyCode").Value = _previousJustMyCodeSetting.Value;

                DebugLog($"Restored 'Just My Code' to {_previousJustMyCodeSetting.Value}.");
            }
            catch (Exception ex)
            {
                DebugLog($"Failed to restore JMC: [{ex.GetType().Name}] {ex.Message}");
            }
            finally
            {
                _previousJustMyCodeSetting = null;
            }
        }

        /// <summary>
        /// Finds the deepest descendant process of the given parent PID.
        /// The dotnet CLI host (<c>dotnet.exe</c>) spawns a child process to run
        /// <c>dotnet-interactive</c>. We need to attach to the actual kernel process,
        /// not the CLI host that just sits in <c>Process.WaitForExit</c>.
        /// Uses WMI to walk the process tree and returns the leaf child PID.
        /// If no children are found, returns the original parent PID.
        /// </summary>
        public static int ResolveKernelProcessId(int parentPid)
        {
            DebugLog($"Resolving kernel process from parent PID {parentPid}...");

            try
            {
                int currentPid = parentPid;

                // Walk down the process tree to find the leaf child.
                // dotnet CLI → dotnet-interactive (sometimes with another shim in between).
                for (int depth = 0; depth < 5; depth++)
                {
                    var children = GetChildProcessIds(currentPid);
                    if (children.Count == 0)
                    {
                        break;
                    }

                    // Log all children at this level and resolve their names.
                    int bestChild = -1;
                    foreach (int childPid in children)
                    {
                        string childName = "(unknown)";
                        try
                        {
                            var childProc = System.Diagnostics.Process.GetProcessById(childPid);
                            childName = childProc.ProcessName;
                        }
                        catch { }
                        DebugLog($"  Depth {depth}: child PID={childPid} name={childName}");

                        // Prefer the kernel process over infrastructure (conhost, etc.).
                        var lower = childName.ToLowerInvariant();
                        if (lower == "dotnet-interactive" || lower.Contains("interactive"))
                        {
                            bestChild = childPid;
                        }
                        else if (bestChild == -1 && (lower == "dotnet" || lower == "dotnet.exe"))
                        {
                            bestChild = childPid;
                        }
                    }

                    // Fall back to first non-conhost child, or first child overall.
                    if (bestChild == -1)
                    {
                        foreach (int childPid in children)
                        {
                            string childName = "(unknown)";
                            try { childName = System.Diagnostics.Process.GetProcessById(childPid).ProcessName; }
                            catch { }
                            if (!childName.Equals("conhost", StringComparison.OrdinalIgnoreCase))
                            {
                                bestChild = childPid;
                                break;
                            }
                        }
                    }

                    if (bestChild == -1)
                    {
                        bestChild = children[0];
                    }

                    currentPid = bestChild;
                }

                if (currentPid != parentPid)
                {
                    string resolvedName = "(unknown)";
                    try
                    {
                        var resolved = System.Diagnostics.Process.GetProcessById(currentPid);
                        resolvedName = resolved.ProcessName;
                    }
                    catch { }
                    DebugLog($"Resolved kernel process: PID={currentPid} name={resolvedName} (from parent {parentPid})");
                }
                else
                {
                    DebugLog($"No child processes found for PID {parentPid}. Using parent directly.");
                }

                return currentPid;
            }
            catch (Exception ex)
            {
                DebugLog($"Failed to resolve child process: [{ex.GetType().Name}] {ex.Message}. Using parent PID {parentPid}.");
                return parentPid;
            }
        }

        /// <summary>
        /// Uses WMI to find all child process IDs of the given parent.
        /// </summary>
        private static List<int> GetChildProcessIds(int parentPid)
        {
            var children = new List<int>();

            using (var searcher = new ManagementObjectSearcher(
                $"SELECT ProcessId FROM Win32_Process WHERE ParentProcessId = {parentPid}"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    var pidObj = obj["ProcessId"];
                    if (pidObj != null)
                    {
                        children.Add(Convert.ToInt32(pidObj));
                    }
                }
            }

            return children;
        }
    }
}

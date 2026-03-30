using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using PolyglotNotebooks.Diagnostics;

namespace PolyglotNotebooks.Execution
{
    /// <summary>
    /// Collects assembly references from the currently loaded Visual Studio solution
    /// and generates <c>#r</c> directives that can be submitted to the dotnet-interactive
    /// kernel so notebook cells can use project types and NuGet packages without manual
    /// <c>#r</c> ceremony.  Only activates when the notebook file belongs to a project
    /// in the loaded solution.
    /// </summary>
    internal static class WorkspaceReferenceCollector
    {
        /// <summary>
        /// Builds a preamble string containing <c>#r</c> directives for every project
        /// output assembly and resolved metadata reference (NuGet packages) in the
        /// current solution.  Returns <c>null</c> when the notebook is not part of a
        /// project, no solution is loaded, or no references are found.
        /// </summary>
        /// <param name="notebookFilePath">
        /// Full path of the notebook file.  If the file is not included in any project
        /// in the current solution, no references are collected.
        /// </param>
        public static string? CollectPreamble(string notebookFilePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (string.IsNullOrEmpty(notebookFilePath))
                return null;

            try
            {
                if (!IsFileInSolutionProject(notebookFilePath))
                {
                    ExtensionLogger.LogInfo(nameof(WorkspaceReferenceCollector),
                        $"Notebook '{Path.GetFileName(notebookFilePath)}' is not part of a project; " +
                        "skipping workspace reference injection.");
                    return null;
                }
                var componentModel = (IComponentModel?)Package.GetGlobalService(typeof(SComponentModel));
                if (componentModel == null)
                {
                    ExtensionLogger.LogWarning(nameof(WorkspaceReferenceCollector),
                        "IComponentModel not available; cannot collect workspace references.");
                    return null;
                }

                var workspace = componentModel.GetService<VisualStudioWorkspace>();
                if (workspace == null)
                {
                    ExtensionLogger.LogWarning(nameof(WorkspaceReferenceCollector),
                        "VisualStudioWorkspace not available; cannot collect workspace references.");
                    return null;
                }

                var solution = workspace.CurrentSolution;
                if (solution == null || !solution.Projects.Any())
                {
                    ExtensionLogger.LogInfo(nameof(WorkspaceReferenceCollector),
                        "No solution or projects loaded; skipping workspace reference injection.");
                    return null;
                }

                var assemblyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var project in solution.Projects)
                {
                    // Project output assembly (e.g. bin/Debug/net8.0/MyProject.dll)
                    if (!string.IsNullOrEmpty(project.OutputFilePath) &&
                        File.Exists(project.OutputFilePath))
                    {
                        assemblyPaths.Add(project.OutputFilePath);
                    }

                    // Resolved metadata references (NuGet packages and framework assemblies
                    // that MSBuild already resolved)
                    foreach (var metaRef in project.MetadataReferences)
                    {
                        if (metaRef is PortableExecutableReference peRef &&
                            !string.IsNullOrEmpty(peRef.FilePath) &&
                            File.Exists(peRef.FilePath))
                        {
                            // Skip .NET runtime/framework assemblies - dotnet-interactive
                            // already has these.  We only want NuGet and project assemblies.
                            if (IsFrameworkAssembly(peRef.FilePath))
                                continue;

                            assemblyPaths.Add(peRef.FilePath);
                        }
                    }
                }

                if (assemblyPaths.Count == 0)
                {
                    ExtensionLogger.LogInfo(nameof(WorkspaceReferenceCollector),
                        "No assemblies found to inject.");
                    return null;
                }

                var sb = new StringBuilder();
                foreach (var path in assemblyPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    // Use forward slashes to avoid escaping issues in the kernel.
                    sb.AppendLine("#r \"" + path.Replace('\\', '/') + "\"");
                }

                var preamble = sb.ToString();
                ExtensionLogger.LogInfo(nameof(WorkspaceReferenceCollector),
                    $"Collected {assemblyPaths.Count} workspace references for kernel injection.");

                return preamble;
            }
            catch (Exception ex)
            {
                ExtensionLogger.LogException(nameof(WorkspaceReferenceCollector),
                    "Failed to collect workspace references.", ex);
                return null;
            }
        }

        /// <summary>
        /// Returns <c>true</c> for assemblies that live inside the .NET runtime or
        /// reference assemblies directories - these are already available in the
        /// dotnet-interactive kernel and should not be re-loaded via <c>#r</c>.
        /// </summary>
        private static bool IsFrameworkAssembly(string filePath)
        {
            // Normalized for case-insensitive comparison.
            var normalized = filePath.Replace('\\', '/');

            // .NET SDK / runtime packs
            if (normalized.IndexOf("/packs/", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // dotnet shared framework
            if (normalized.IndexOf("/dotnet/shared/", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // .NET Framework reference assemblies
            if (normalized.IndexOf("/Reference Assemblies/", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="filePath"/> is included in at least
        /// one project in the currently loaded solution.  Uses the VS hierarchy APIs so
        /// linked files and non-standard project structures are handled correctly.
        /// </summary>
        private static bool IsFileInSolutionProject(string filePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(Package.GetGlobalService(typeof(SVsSolution)) is IVsSolution solution))
                return false;

            // VSHPROPID_ProjectDir could be used, but the most reliable check is
            // IVsSolution.GetProjectOfUniqueName — however that works on project paths,
            // not arbitrary files.  Instead, enumerate hierarchies and ask each one
            // whether it contains the file via ParseCanonicalName.
            var guid = Guid.Empty;
            if (ErrorHandler.Failed(solution.GetProjectEnum(
                    (uint)__VSENUMPROJFLAGS.EPF_LOADEDINSOLUTION, ref guid, out var enumHierarchies)))
                return false;

            var hierarchies = new IVsHierarchy[1];
            while (enumHierarchies.Next(1, hierarchies, out uint fetched) == VSConstants.S_OK && fetched == 1)
            {
                if (hierarchies[0] != null &&
                    ErrorHandler.Succeeded(hierarchies[0].ParseCanonicalName(filePath, out uint itemId)) &&
                    itemId != (uint)VSConstants.VSITEMID.Nil)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

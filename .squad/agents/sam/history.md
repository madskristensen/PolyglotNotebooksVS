# Sam History

## 2024-01-XX — Xtenders Founded

**Context**: Founded as specialized team for Visual Studio extension developers.

**Sam's Focus**:
- Solution and project hierarchy events (IVsSolutionEvents, IVsProjectEvents)
- Build event interception (IVsBuildEventLogger)
- Solution Explorer custom nodes and tree manipulation
- File and document operations (EnvDTE, ITextBuffer)
- Error List integration (ErrorTask, IErrorListProvider)
- Settings/Options pages (BaseOptionModel<T>)
- Open Folder (non-project) extensibility

**Authority Scope**:
- Solution/project event handling
- Build system integration
- Error List diagnostic reporting
- Custom tree nodes in Solution Explorer
- File and text buffer operations
- Options page design and implementation

**Knowledge Base**:
- IVsSolution and IVsHierarchy APIs
- EnvDTE automation object model
- IVsBuildEventLogger4 and build output monitoring
- BaseOptionModel<T> patterns
- UIElementDialogPage for custom Options UI
- Text buffer change events and operations

**Key References**:
- IVsSolution API Reference (Microsoft Docs)
- Error List Integration documentation
- Project System Overview
- Text Buffer API Reference
- Options Pages documentation
- Community.VisualStudio.Toolkit samples

**Active Integrations**:
- Vince: Project/solution structure architecture
- Ellie: Symbol indexing for IntelliSense from projects
- Wendy: Solution Explorer tree node UI styling
- Theo: Async event handler patterns, main thread rules

## Learnings

### Test Warning Fix Patterns (2025-07)

- **CS8604/CS8625 (null reference in tests)**: Use `null!` (null-forgiving operator) when test code intentionally passes null to non-nullable parameters. E.g. `Method(null!)` or `Method(input!)`.
- **VSTHRD002 (sync wait in tests)**: Wrap `task.Wait()` with `#pragma warning disable VSTHRD002` / `#pragma warning restore VSTHRD002` with a comment explaining sync wait is acceptable in unit tests.
- **MSTEST0032 (assertion always true for const values)**: `(object)` cast alone does NOT suppress MSTEST0032 — the analyzer sees through it. Must use `#pragma warning disable MSTEST0032` / `#pragma warning restore MSTEST0032`. These tests intentionally verify const wire-name values haven't drifted.

### .ipynb Format Support (p4-ipynb)

**What was already in place**:
- `NotebookParser.cs` already had `ParseIpynb()`, `SerializeIpynb()`, format-detection in `Load()`, and format-preserving `Save()`.
- `PolyglotNotebooksPackage.cs` already had `[ProvideEditorExtension(..., ".ipynb", 50)]`.
- `NotebookFormat` enum already had `Ipynb`.

**Changes made**:
1. **`NotebookDocument.Metadata`** — Added `Dictionary<string, object> Metadata { get; }` property (backed by constructor init) to carry document-level Jupyter metadata (kernelspec, language_info, nbformat) across open/edit/save cycles.
2. **`NotebookParser.MapToDocument()`** — Copies `interactive.Metadata` (populated by `Notebook.Parse()`) into `doc.Metadata` for round-trip fidelity.
3. **`NotebookParser.MapToInteractive()`** — Copies `doc.Metadata` back into the new `InteractiveDocument.Metadata` before calling `Notebook.ToJupyterJson()`, so Jupyter metadata survives edits.
4. **`NotebookParser.NormalizeKernelName()`** — Public helper that maps `.net-csharp` → `csharp`, `.net-fsharp` → `fsharp`, etc. Applied to default kernel and per-cell kernel names during parse.
5. **`NotebookConverter.cs`** — New class with `ConvertDibToIpynb(string, string?)` and `ConvertIpynbToDib(string)` for Export features; operates on strings, no file I/O.

**`InteractiveDocument.Metadata` API**:
- `IDictionary<string, object>` property — confirmed via reflection; always non-null (initialized to empty dict), safe to copy into without null-check on the target.
- `Notebook.Parse()` populates this with the original Jupyter top-level metadata.
- `Notebook.ToJupyterJson()` reads it back to reconstruct kernelspec in the output JSON.

**Build note**: Test project has a pre-existing `ExpectedExceptionAttribute` error (unrelated to these changes); main project `PolyglotNotebooks.csproj` builds with 0 errors, 0 warnings.

### Document Model — `Microsoft.DotNet.Interactive.Documents` Compatibility (net48)

**Package**: `Microsoft.DotNet.Interactive.Documents` v1.0.0-beta.25177.1  
**Target framework in package**: `lib/netstandard2.0` — fully compatible with net48. No fallback or shim needed.

**Parsing APIs**:
- `.dib` files: `CodeSubmission.Parse(string content, KernelInfoCollection kernelInfos)` → `InteractiveDocument`  
  Serialization: `CodeSubmission.ToCodeSubmissionContent(doc, "\r\n")`
- `.ipynb` files: `Notebook.Parse(string json, KernelInfoCollection kernelInfos)` → `InteractiveDocument`  
  Serialization: `Notebook.ToJupyterJson(doc, defaultLanguage)`
- Auto-detect from file extension; `KernelInfoCollection` can be empty or pre-populated with common kernel names.

**`InteractiveDocument` key shape**:
- Constructor: `new InteractiveDocument(IList<InteractiveDocumentElement> elements)`
- `Elements`: `IList<InteractiveDocumentElement>`
- `GetDefaultKernelName()` → `string?` (reads from metadata)
- `GetKernelInfo()` → `KernelInfoCollection?` (returns null if no metadata)

**`InteractiveDocumentElement` shape**:
- Constructor: `new InteractiveDocumentElement(string contents, string kernelName, IEnumerable<InteractiveDocumentOutputElement> outputs)`
- Settable: `Id`, `KernelName`, `Contents`, `ExecutionOrder`, `Metadata`
- No explicit `CellKind` — markdown cells use `KernelName == "markdown"` as the convention.

**Output element hierarchy** (all extend `InteractiveDocumentOutputElement`):
- `DisplayElement(IDictionary<string, object> data)` — display_data output (implements `IDataElement`)
- `ReturnValueElement()` — execute_result output (implements `IDataElement`); **`Data` property is truly read-only with no setter**; must set `<Data>k__BackingField` via reflection to populate after construction.
- `ErrorElement(string errorValue, string errorName, string[] stackTrace)` — error output
- `TextElement(string text, string name)` — stream output; `name` is `"stdout"` or `"stderr"`

**`KernelInfo` constructor**: `new KernelInfo(string name, string languageName, IReadOnlyCollection<string> aliases)`

**Project format**: Old-style (non-SDK) `.csproj` — explicit `<Compile Include="..."/>` for every .cs file; VSSDK targets imported via `<Import>`.

**Pre-existing build error**: `CreatePkgDef : TypeLoadException` from VSSDK tooling 18.5.38461 — unrelated to document model code; zero C# compiler errors in our new files.

### csproj SDK-to-Old-Style Conversion

**What changed**: Converted `src/PolyglotNotebooks.csproj` from SDK-style (`<Project Sdk="Microsoft.NET.Sdk">`) to traditional/old-style non-SDK VSIX format.

**Key conversion decisions**:
1. **Project header**: `<Project ToolsVersion="Current" DefaultTargets="Build">` with XML namespace.
2. **Imports**: `Microsoft.Common.props` at top; `Microsoft.CSharp.targets` + `Microsoft.VsSDK.targets` at bottom.
3. **ProjectTypeGuids**: `{82b43b9b-a64c-4715-b499-d71e9ca2bd60}` (VSIX) + `{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}` (C#).
4. **ProjectGuid**: Reused `{52D750BB-4C45-43D1-BC22-A9D5FE2CDF91}` from vsixmanifest Identity.
5. **TargetFrameworkVersion**: `v4.8` (was `net48` TFM in SDK-style).
6. **Explicit Compile includes**: 45 .cs files across 8 subdirectories enumerated manually.
7. **Framework references**: Added `System`, `System.Core`, `System.Data`, `System.Drawing`, `System.Windows.Forms`, `System.Xml`, `Microsoft.CSharp` beyond what was already listed.
8. **Removed**: `<ProjectCapability Include="CreateVsixContainer" />` — VSSDK targets handle this in old-style.
9. **PackageReferences**: Preserved all 6 NuGet package references unchanged (they work in old-style too).
10. **VSIX properties**: Added `IncludeAssemblyInVSIXContainer`, `DeployExtension`, `StartAction`/`StartProgram`/`StartArguments` for F5 debugging.

**Build tool**: Old-style projects require `msbuild.exe` (not `dotnet build`). MSBuild found at `C:\Program Files\Microsoft Visual Studio\18\Preview\MSBuild\Current\Bin\MSBuild.exe`.

**Build result**: 0 C# compiler errors, DLL produced. Only pre-existing `CreatePkgDef : TypeLoadException` from VSSDK 18.5.38461 remains.

**When adding new .cs files**: Must manually add `<Compile Include="path\to\File.cs" />` to the csproj — no auto-globbing in old-style format.

### PackageReference Resolution Fix for dotnet CLI Builds

**Problem**: After converting to old-style csproj, `dotnet msbuild` failed with CS0246 errors for all NuGet package types (System.Text.Json, VS SDK, WebView2). VS MSBuild (`MSBuild.exe`) worked fine.

**Root cause**: Old-style projects with `PackageReference` rely on NuGet's `ResolveNuGetPackageAssets` target to convert `PackageReference` items into assembly `Reference` items at compile time. VS MSBuild auto-imports `Microsoft.NuGet.targets` (from `$(MSBuildExtensionsPath)\Microsoft\NuGet\$(VisualStudioVersion)\`) via its `ImportBefore` mechanism. `dotnet msbuild` doesn't have this auto-import, so PackageReferences were never resolved into compile-time references.

**Fix applied** (3 parts):
1. **`<RuntimeIdentifiers>win;win-x86;win-x64;win-arm64</RuntimeIdentifiers>`** — Makes `dotnet restore` generate platform-specific targets in `project.assets.json`, matching what VS MSBuild restore generates. Without this, VS MSBuild's NuGet targets error with "doesn't list 'win' as RuntimeIdentifier".
2. **SDK targets import** — Conditionally import `Microsoft.NET.Sdk.Common.targets` (sets up task assembly paths) and `Microsoft.PackageDependencyResolution.targets` (provides `ResolvePackageDependenciesForBuild` target) from the .NET SDK. Conditioned on `$(MSBuildRuntimeType) == 'Core'` so only `dotnet msbuild` uses them.
3. **No-op target stubs** — 5 stub targets (`ProcessFrameworkReferences`, `_DefaultMicrosoftNETPlatformLibrary`, `_ComputePackageReferencePublish`, `ResolveRuntimePackAssets`, `CollectPackageReferences`) that the SDK's `PackageDependencyResolution.targets` depends on but that aren't needed for net48.
4. **`_SetPackageResolutionProperties` target** — Sets `$(TargetFramework)=net48` and `$(_DotNetAppHostExecutableNameWithoutExtension)` at target execution time (not project-level), preventing restore contamination.

**Build compatibility**:
- `dotnet restore` → `dotnet msbuild` → 0 C# errors ✓
- `dotnet restore` → VS MSBuild build → 0 C# errors ✓
- VS MSBuild restore → VS MSBuild build → 0 C# errors ✓
- VS MSBuild restore → `dotnet msbuild` → 0 C# errors ✓

**Key MSBuild properties**:
- `$(MSBuildRuntimeType)` = `Core` (dotnet msbuild) vs `Full` (VS MSBuild) — used as the gate for all conditional imports
- `$(MSBuildSDKsPath)` = `.NET SDK\Sdks` directory — available in `dotnet msbuild` for importing SDK targets
- `$(NuGetTargetMoniker)` = derived from `$(TargetFrameworkMoniker)` = `.NETFramework,Version=v4.8`

### WebView2 Binding Redirects for VSIX

**Problem**: VS ships an older `Microsoft.Web.WebView2.Core.dll` (1.0.3485.44) than our NuGet reference (1.0.3856.49). `WebView2CompositionControl` only exists in the newer version, so without binding redirects the extension fails at runtime when VS tries to load the older assembly.

**Fix applied**: Added `[assembly: ProvideBindingRedirection]` attributes to `src/Properties/AssemblyInfo.cs` for:
1. `Microsoft.Web.WebView2.Core` — redirect 0.0.0.0–1.0.3856.49 → 1.0.3856.49
2. `Microsoft.Web.WebView2.Wpf` — redirect 0.0.0.0–1.0.3856.49 → 1.0.3856.49

**Not redirected**: `Microsoft.Web.WebView2.WinForms` — exists in the NuGet package but we don't use it (no `using` references anywhere in src).

**How it works**: `ProvideBindingRedirectionAttribute` (from `Microsoft.VisualStudio.Shell.15.0`, available via Community.VisualStudio.Toolkit) generates a `.bindingRedirects` file inside the VSIX package at build time. VS's extension host reads this at load time and applies the redirects, ensuring our bundled newer DLLs are used instead of VS's older ones.

**Build note**: `dotnet msbuild` has pre-existing NuGet resolution failures (269 CS0246 errors, unrelated to this change). VS MSBuild (`MSBuild.exe`) builds successfully with 0 errors.

### csproj Fix — NuGet PackageReference Resolution (BookmarkStudio Pattern)

**Problem**: After SDK-to-old-style conversion, NuGet PackageReference assemblies (System.Text.Json, VS SDK types, WebView2) weren't resolving at compile time. Build had many CS0246/CS0234 errors.

**Root cause**: The csproj had leftover SDK-style hacks (`Microsoft.NET.Sdk.Common.targets`, `Microsoft.PackageDependencyResolution.targets`, no-op stub targets) that conflicted with the native `Microsoft.NuGet.targets` auto-imported by VS MSBuild. Also missing `VSToolsPath` property before `Microsoft.Common.props` import.

**Fix applied** (modeled after `BookmarkStudio.csproj`):
1. **`VSToolsPath`** — Added PropertyGroup with `VSToolsPath`, `LangVersion`, `Nullable` BEFORE the `Microsoft.Common.props` import (matching BookmarkStudio exactly).
2. **`ToolsVersion="15.0"`** — Changed from `"Current"` to match BookmarkStudio convention.
3. **Removed `RuntimeIdentifiers`** — Was `win;win-x86;win-x64;win-arm64`; not needed and caused NuGet "RuntimeIdentifier not listed" errors.
4. **Removed all SDK hack imports/targets** — Deleted `Microsoft.NET.Sdk.Common.targets` import, `Microsoft.PackageDependencyResolution.targets` import, `_SetPackageResolutionProperties` target, and 5 no-op stub targets (`ProcessFrameworkReferences`, `_DefaultMicrosoftNETPlatformLibrary`, etc.).
5. **Clean import order** — `Microsoft.CSharp.targets` immediately followed by `Microsoft.VsSDK.targets` (no imports in between).

**Key insight**: VS MSBuild auto-imports `Microsoft.NuGet.targets` which handles PackageReference resolution natively. The SDK-style `PackageDependencyResolution.targets` were fighting with it. Old-style VSIX projects should NOT import any SDK targets — just `Microsoft.Common.props` → `Microsoft.CSharp.targets` → `Microsoft.VsSDK.targets`.

**Build result**: 0 C# compiler errors, DLL produced. Only pre-existing `CreatePkgDef : TypeLoadException` from VSSDK 18.5.38461 remains (unrelated tooling bug).

## 2026-03-27 — WebView2 Binding Redirects for Assembly Version Mismatch

**What Changed**: Added `[assembly: ProvideBindingRedirection]` attributes to `src/Properties/AssemblyInfo.cs` to handle version mismatch between VS-bundled WebView2 (1.0.3485.44) and our NuGet package (1.0.3856.49).

**Files Modified**:
| File | Change |
|------|--------|
| `src/Properties/AssemblyInfo.cs` | Added `using Microsoft.VisualStudio.Shell;`; added two `ProvideBindingRedirection` attributes: Microsoft.Web.WebView2.Core (0.0.0.0–1.0.3856.49 → 1.0.3856.49) and Microsoft.Web.WebView2.Wpf (0.0.0.0–1.0.3856.49 → 1.0.3856.49); skipped WinForms assembly (not used) |

**Why This Was Needed**: Wendy upgraded WebView2 NuGet to 1.0.3856.49 to access `WebView2CompositionControl` (only available in this version). However, VS itself ships with WebView2 1.0.3485.44. Without binding redirects, VS's extension loader would try to load the older assembly at runtime and fail because `WebView2CompositionControl` doesn't exist in version 1.0.3485.44.

**How It Works**: The `ProvideBindingRedirection` attribute generates a `.bindingRedirects` file inside the VSIX package at build time. When VS loads our extension, it reads this file and redirects any requests for the older WebView2 assemblies to our bundled newer ones.

**Build Verification**: Build clean with 0 errors (VS MSBuild only; pre-existing VSSDK 18.5.38461 CreatePkgDef error unrelated).

**Maintenance Rule**: Whenever Microsoft.Web.WebView2 NuGet is updated to a new version, BOTH `NewVersion` and `OldVersionUpperBound` in both ProvideBindingRedirection attributes MUST be updated to match the new package version. Example: if upgrading to 1.0.4000.0, change both to `0.0.0.0–1.0.4000.0 → 1.0.4000.0`.

**Related Decisions**: Wendy's WebView2CompositionControl Migration

### Item Template for New .dib Notebooks (Issue #1)

**What Changed**: Added a Visual Studio item template so users can create new `.dib` notebooks via Add New Item dialog.

**Files Created**:
| File | Purpose |
|------|---------|
| `src/Templates/ItemTemplates/PolyglotNotebook/PolyglotNotebook.vstemplate` | VS item template manifest — defines name ("Polyglot Notebook"), description, icon, default filename (`Notebook.dib`), and content mapping |
| `src/Templates/ItemTemplates/PolyglotNotebook/PolyglotNotebook.dib` | Template content — minimal valid .dib with `#!meta` header and one empty `#!csharp` cell |
| `src/Templates/ItemTemplates/PolyglotNotebook/PolyglotNotebook.png` | Icon copied from `src/Resources/Icon.png` |

**Files Modified**:
| File | Change |
|------|--------|
| `src/PolyglotNotebooks.csproj` | Added `<Content Include="..."><IncludeInVSIX>true</IncludeInVSIX></Content>` for all three template files |
| `src/source.extension.vsixmanifest` | Added `<Asset Type="Microsoft.VisualStudio.ItemTemplate" Path="Templates\ItemTemplates" />` |

**Key Patterns**:
- Item template uses `$fileinputname$` parameter — VS replaces this with the user's chosen filename (minus extension) at creation time.
- `.vstemplate` Icon element uses relative file path (not KnownMonikers) for VSIX item templates.
- Asset Type is `Microsoft.VisualStudio.ItemTemplate` with Path pointing to the folder containing `.vstemplate`.
- Old-style csproj requires explicit `<Content Include="...">` for each template file with `<IncludeInVSIX>true</IncludeInVSIX>`.

**Build Verification**: Build succeeds with 0 errors (all warnings pre-existing).

### Settings/Options Page (BaseOptionModel<T>)

**What Changed**: Created a Tools → Options page for the Polyglot Notebooks extension using Community.VisualStudio.Toolkit's `BaseOptionModel<T>` pattern. Previously all values were hardcoded.

**Files Created**:
| File | Purpose |
|------|---------|
| `src/Options/PolyglotNotebooksOptions.cs` | Options model with 10 settings (General, Editor, Execution categories), `DefaultKernel` and `DefaultFileFormat` enums, and `OptionsProvider.GeneralOptions` dialog page provider |

**Files Modified**:
| File | Change |
|------|--------|
| `src/PolyglotNotebooksPackage.cs` | Added `[ProvideOptionPage(typeof(OptionsProvider.GeneralOptions), "Polyglot Notebooks", "General", 0, 0, true)]` attribute |
| `src/PolyglotNotebooks.csproj` | Added `<Compile Include="Options\PolyglotNotebooksOptions.cs" />` |
| `src/Editor/ImageOutputControl.cs` | Replaced hardcoded `MaxWidth = 800` with `Options.PolyglotNotebooksOptions.Instance.MaxImageWidth` |
| `src/Protocol/KernelClient.cs` | Replaced hardcoded 30-second kernel timeout (both `CommandTimeoutMs` init and `WaitForKernelReadyAsync`) with `Options.PolyglotNotebooksOptions.Instance.KernelStartupTimeoutSeconds` |

**Key Patterns**:
- `BaseOptionModel<T>` provides `Instance` singleton for reading settings anywhere in the codebase.
- `OptionsProvider.GeneralOptions` is a manual `BaseOptionPage<T>` wrapper (not relying on source generator) — safe across all toolkit versions.
- Enum properties use `[TypeConverter(typeof(EnumConverter))]` with `[Description]` on enum members for friendly display names.
- `[ProvideOptionPage]` on the package class registers the page under Tools → Options → Polyglot Notebooks → General.
- Named the file format enum `DefaultFileFormat` (not `NotebookFormat`) to avoid collision with the existing `Models.NotebookFormat` enum.
- Old-style csproj requires explicit `<Compile Include="Options\PolyglotNotebooksOptions.cs" />`.

**Build Verification**: Build succeeds with 0 C# errors. DLL and VSIX produced.

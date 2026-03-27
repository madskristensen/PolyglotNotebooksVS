# Foundational Decisions

Key architectural and strategic decisions for Xtenders.

## Decision 1: Use Community.VisualStudio.Toolkit as Foundation

**Date**: 2024-01-XX
**Lead**: Vince
**Status**: ACTIVE

### Rationale
- Abstracts raw VS SDK complexity
- Maintained by Mads Kristensen (@madskristensen) and the extensibility community
- Provides modern async-first patterns (AsyncPackage, JoinableTaskFactory)
- Reduces threading bugs through compiler-checked patterns
- Active GitHub issues/PRs with quick community support

### Implications
1. All new extensions target Community.VisualStudio.Toolkit v17+ (VS 2022)
2. Raw SDK APIs used only when Toolkit lacks abstraction
3. We maintain expertise in both layers (Toolkit + underlying SDK)
4. Extensions inherit Toolkit version compatibility (VS 2022+, VS 2019 via explicit targeting)

### Related Decision
- Multi-version support requires explicit `.vsixmanifest` InstallationTarget ranges
- Toolkit v16 for VS 2019; Toolkit v17 for VS 2022

## Decision 2: Async-First, ThreadHelper-Based Threading Model

**Date**: 2024-01-XX
**Lead**: Theo (Reliability Engineer)
**Status**: ACTIVE

### Rationale
- VS 2022 enforces UI thread safety; deadlocks are common with synchronous patterns
- Microsoft.VisualStudio.SDK.Analyzers enforces async rules at compile time
- JoinableTaskFactory patterns prevent deadlocks and main thread stalls
- Community.VisualStudio.Toolkit wraps ThreadHelper, making async transparent

### Implications
1. All package/command/tool window code uses `ThreadHelper.JoinableTaskFactory.RunAsync()`
2. No `Task.Wait()` or `.Result` synchronization (violates analyzer rules)
3. Extension initialization in `InitializeAsync()`, not `Initialize()`
4. External async APIs require `SwitchToMainThreadAsync()` before UI access
5. Analyzers enforced in CI/CD pipeline; violations block merges

### Code Pattern
```csharp
// REQUIRED pattern
await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
// ... now on UI thread, safe to call EnvDTE, modify UI

// FORBIDDEN pattern
var result = Task.Run(() => { ... }).Result;  // DEADLOCK RISK
```

## Decision 3: Target Visual Studio 2022 as Baseline, Support 2019+ via Explicit Configuration

**Date**: 2024-01-XX
**Lead**: Vince
**Status**: ACTIVE

### Rationale
- VS 2022 is current; 2019 extended support through 2025
- Toolkit v17+ is VS 2022-native
- Explicit InstallationTarget ranges in .vsixmanifest allow multi-version publishing
- Separate CI/CD builds for version-specific extensions when needed

### Implications
1. Default scaffolding targets `<InstallationTarget Version="[17.0,18.0)">`
2. VS 2019 support requires explicit Toolkit v16 configuration + version range
3. VS 2017 support deprecated; historical extensions may be archived
4. CI/CD produces versioned VSIX packages (e.g., `extension-v17.vsix`, `extension-v16.vsix`)
5. Marketplace listings include version compatibility badges

### Configuration Examples
```xml
<!-- VS 2022 (default) -->
<InstallationTarget Version="[17.0,18.0)" />

<!-- VS 2022 + 2019 multi-version -->
<InstallationTarget Version="[16.0,18.0)" />

<!-- VS 2022 stable + preview (17.0-17.9) -->
<InstallationTarget Version="[17.0,18.0)" />
<InstallationTarget Version="[17.10,18.0)" />  <!-- LTSC-->
```

---

## Decision 4: MSTest v4 ThrowsException Pattern

**Date**: 2026-03-27
**Lead**: Theo (Threading & Reliability Engineer)
**Status**: ACTIVE
**Participants**: Theo

### Rationale

During Phase 1 unit test authoring, `Assert.ThrowsException<T>()` was found to be unavailable in MSTest.Sdk 4.0.1 (net48 target). The compiler reports `CS0117: 'Assert' does not contain a definition for 'ThrowsException'`, even with explicit `using Microsoft.VisualStudio.TestTools.UnitTesting;`.

### Decision

Replace all `Assert.ThrowsException<T>()` usages with explicit try/catch + `Assert.IsTrue(threw, ...)`:

```csharp
// INSTEAD OF (doesn't compile on MSTest.Sdk 4.0.1 / net48):
Assert.ThrowsException<ArgumentNullException>(() => new SomeClass(null));

// USE (works reliably):
bool threw = false;
try { new SomeClass(null); }
catch (ArgumentNullException) { threw = true; }
Assert.IsTrue(threw, "Expected ArgumentNullException for null argument");
```

### Implications

1. All new test code in this project should use try/catch for exception-assertion tests
2. `[ExpectedException]` attribute is an alternative but gives less control over the message
3. This pattern is more verbose but explicit and unambiguous
4. If MSTest version is ever upgraded and `ThrowsException<T>` becomes available again, this is a style-only refactor with no behavior change

### Related Decisions

- Phase 1 unit tests: `ProtocolClientTests.cs`, `DocumentModelTests.cs`, `KernelProcessManagerTests.cs`

---

## Decision 5: Custom Editor Architecture for Polyglot Notebooks

**Date**: 2026-03-27
**Lead**: Vince (Extension Architect)
**Status**: ACTIVE
**Participants**: Vince, Theo, Sam

### Rationale

VS custom editors require separate view and data objects only when multiple views can share the same data (e.g., split-pane). Notebook files have a single canonical view, so there is no value in splitting. Combining view + data in one class reduces complexity and eliminates cross-object coordination overhead. This is the same pattern used by the VS XAML designer and other complex custom editors.

### Decision

`NotebookEditorPane` serves as **both the view and the document data** object — `ppunkDocView` and `ppunkDocData` both point to the same instance. The pane inherits `WindowPane` (IVsWindowPane) and implements `IVsPersistDocData`.

### Document Load Sequence

1. `CreateEditorInstance` — creates `NotebookEditorPane` with file path, returns marshaled COM pointers. Does **not** load the document.
2. `IVsWindowPane.CreatePaneWindow` (VS-called) — triggers `WindowPane.Initialize()` which creates `NotebookControl` (with null document) and sets it as `Content`.
3. `IVsPersistDocData.LoadDocData` (VS-called) — loads document via `NotebookDocumentManager.OpenAsync`, wires events, updates `NotebookControl.Document`.

### NotebookDocumentManager Role

- One `NotebookDocumentManager` instance lives on `NotebookEditorFactory` (factory is a singleton per package lifetime)
- The manager tracks all open notebook documents; `NotebookEditorPane` calls `OpenAsync` on load and `CloseAsync` on close
- Added `RegisterDocument(filePath, doc)` to support rename without disk re-read

### Save Flows

| VSSAVEFLAGS | Behavior |
|---|---|
| VSSAVE_Save / SilentSave | `doc.Save()` in place |
| VSSAVE_SaveAs | Show `SaveFileDialog`, call `doc.SaveAs(newPath)` |
| VSSAVE_SaveCopyAs | Serialize to new path via `NotebookParser`; do not change `FilePath` or dirty state |

### Implications

- **Wendy (UI)**: Replace `NotebookControl` in Phase 2.2. The `Document` property setter is the integration point — set it and the control should render. Subscribe to `PropertyChanged` and `Cells.CollectionChanged` as needed.
- **Theo (Reliability)**: `IVsPersistDocData.OnRegisterDocData` must be implemented (returns S_OK stub). Easy to miss from reference examples.
- **Sam (Tests)**: The `Assert.ThrowsException` failures in the test project are pre-existing and unrelated to this phase.

---

## Decision 6: .ipynb Jupyter Metadata Round-Trip Strategy

**Date**: 2026-03-27
**Lead**: Sam (Jupyter Format Specialist)
**Status**: ACTIVE
**Participants**: Sam, Vince

### Rationale

`Notebook.Parse()` populates `InteractiveDocument.Metadata` with the full original Jupyter metadata. When we convert to our model and back, we must restore that metadata to the `InteractiveDocument` before calling `Notebook.ToJupyterJson()`, otherwise the output loses `kernelspec`/`language_info`. Storing it on `NotebookDocument` (rather than hidden in a private field of the parser) makes it accessible for future features (e.g., displaying kernel info in the UI, kernel switching).

### Decision

Document-level Jupyter metadata (`kernelspec`, `language_info`, `nbformat`, etc.) is carried through open/edit/save cycles by storing it in `NotebookDocument.Metadata` — a `Dictionary<string, object>` populated from `InteractiveDocument.Metadata` on load and written back to a new `InteractiveDocument` before serialization.

### Kernel Name Normalization

Jupyter notebooks from the .NET Interactive SDK may use `.net-csharp`, `.net-fsharp`, `.net-pwsh` as kernel names. These are normalized to `csharp`, `fsharp`, `pwsh` (canonical dotnet-interactive names) during parse via `NotebookParser.NormalizeKernelName()`. This keeps the internal model clean while preserving the original Jupyter metadata for round-tripping.

### Converter Pattern

`NotebookConverter` provides string-to-string conversion (`ConvertDibToIpynb`, `ConvertIpynbToDib`) that are file-system-free. These use an empty string for the `filePath` parameter of `ParseDib/ParseIpynb`, which is safe because `filePath` is only stored and never read during conversion.

### Implications

- `NotebookDocument` gains a new `Metadata` property — **non-breaking** (additive, initialized empty)
- Existing `.dib` workflows unaffected: empty `Metadata` dict is a no-op

---

## Decision 7: CI/CD & Open Source Setup for PolyglotNotebooksVS

**Date**: 2026-03-27
**Lead**: Penny (VSIX Packaging & Marketplace Publisher)
**Status**: ACTIVE
**Participants**: Penny

### CI/CD (p5-cicd)

The existing `build.yaml` already matches the BookmarkStudio reference pattern exactly. **No changes were needed.** Key aspects confirmed and locked:

- Triggers: `push` to `master`, `pull_request_target` to `master`, `workflow_dispatch`
- Uses `pull_request_target` (not `pull_request`) to allow access to secrets for test reporting on PRs from forks, with security guard: only runs test reporter for same-repo PRs
- VSIX version stamp via `timheuer/vsix-version-stamp@v2` on every build
- NuGet cache keyed on all solution/project file hashes
- Build output to `/_built` (absolute path, Windows runner)
- Test runner: `dotnet vstest /_built/PolyglotNotebooks.Test.dll --logger trx`
- Test reporting: `dorny/test-reporter@v2.6.0` with dotnet-trx reporter
- Open VSIX Gallery: every push to `master` (continuous preview)
- VS Marketplace: only on `workflow_dispatch` or `[release]` commit message tag
- VSIX signing: placeholder not yet added — future task when certificate is available

### Open Source Files

The following files were created as part of p5-opensource:

| File | Purpose |
|------|---------|
| `CONTRIBUTING.md` | Dev setup, build, test, debug, architecture, PR guidelines |
| `.github/ISSUE_TEMPLATE/bug_report.md` | VS version, OS, repro steps, notebook content |
| `.github/ISSUE_TEMPLATE/feature_request.md` | Description, use case, proposed solution |
| `.github/PULL_REQUEST_TEMPLATE.md` | What/how to test/checklist |
| `README.md` | Full project README (replaced Xtenders meta-content) |

`LICENSE` verified: MIT, Mads Kristensen 2026 — correct.

### Pre-existing Build Issues Found

Three pre-existing build failures exist, unrelated to this task:

1. **`src/Models/NotebookDocument.cs`** — `Metadata` property had duplicate declaration (doc-comment version at line ~38 plus an extra without doc comment added in error). **FIXED by Penny during this phase.**
2. **`src/Editor/NotebookEditorPane.cs`** — Missing `IVsPersistDocData.OnRegisterDocData` implementation and missing `NotebookControl` type. Incomplete implementation — needs Wendy or Ellie.
3. **`test/` files** — `Assert.ThrowsException<T>` unavailable with `UseVSTest=true` on `net48` target. Pre-existing in `KernelProcessManagerTests.cs` and `DocumentModelTests.cs`. Documented in Decision 4.

### Recommendation

The `UseVSTest=true` + `net48` combination in the test project may be restricting the MSTest API surface. Consider removing `UseVSTest=true` or migrating to `net8.0` for tests. Route to Theo for investigation.

---

## Decision Review Schedule

- **Q1**: Review Toolkit version deprecations, LSP migration status
- **Q2**: Assess VS 2025 preview compatibility
- **Q3**: Evaluate new MEF composition models (if any)
- **Q4**: Threading analyzer rule updates from Microsoft

---

## Decision 8: Cell UI Code-Only WPF Pattern

**Date**: 2026-03-27
**Lead**: Wendy (UI & Tool Window Specialist)
**Status**: ACTIVE
**Participants**: Wendy

### Rationale

Phase 2.2 built the cell-based notebook editor UI entirely in C# (no XAML). This was specified in the task brief due to net48 lacking XAML compilation setup, and it worked well. All controls are programmatically constructed with VS theming integrated via `DynamicResource` bindings.

### Decision

All UI code is in C#, using `new StackPanel()`, `new Grid()`, `new TextBox()` pattern. No XAML files required.

**System.Xaml Reference Required**: When using `new Binding(...)` / `element.SetBinding(...)` in net48 code-only WPF (no .xaml files), `System.Xaml.dll` must be explicitly referenced. It is NOT pulled in by `PresentationFramework` alone in this SDK-style project. Added `<Reference Include="System.Xaml" />` to `PolyglotNotebooks.csproj`.

### CellRunRequested Event Contract for Phase 2.3

`NotebookControl` exposes `event EventHandler<CellRunEventArgs>? CellRunRequested` as the single integration seam for execution. Phase 2.3 (Ellie) should subscribe to this event on the `NotebookControl` instance (accessible via `NotebookEditorPane._control`) and dispatch execution against `CellRunEventArgs.Cell`.

After execution, Ellie should:
- Set `cell.ExecutionStatus = CellExecutionStatus.Running` before starting
- Set `cell.ExecutionOrder = <N>` and append `CellOutput` items to `cell.Outputs`
- Set `cell.ExecutionStatus = CellExecutionStatus.Succeeded` or `Failed` when done

The UI will update automatically via its existing `PropertyChanged` and `CollectionChanged` subscriptions.

### Full Rebuild on CollectionChanged (v1 Tradeoff)

`NotebookControl.RebuildCells()` does a full `_cellStack.Children.Clear()` + rebuild on every `Cells.CollectionChanged` event. This is O(n) per change but simple and correct. For typical notebook sizes (<100 cells) this is imperceptible. A future optimization could do incremental insert/remove if needed.

### Implications

1. Any future WPF code using `Binding`, `DataTemplate`, or markup extensions programmatically must ensure `System.Xaml` is referenced.
2. Integration point for Ellie (Phase 2.3) is `NotebookControl.CellRunRequested` event.
3. UI updates are automatic (PropertyChanged/CollectionChanged subscriptions) — no manual re-rendering needed.

---

## Decision 9: ExtensionLogger JIT-Safety Pattern

**Date**: 2026-03-27
**Lead**: Theo (Threading & Reliability Engineer)
**Status**: ACTIVE
**Participants**: Theo

### Context

`ExtensionLogger` wraps `Microsoft.VisualStudio.Shell.ActivityLog` to provide centralized logging. Unit tests run in a process that does NOT have VS assemblies on the probing path. When a class under test calls `ExtensionLogger.LogInfo(...)`, the CLR attempts to JIT-compile that method. JIT compilation fails with `FileNotFoundException` because `Microsoft.VisualStudio.Shell.Framework` is not present.

The naive fix — wrapping the call in `try { ActivityLog.Log...; } catch { }` — does NOT work because the exception occurs during JIT compilation of the method body, *before* the try block can execute.

### Decision

Any method that calls VS-only types (from assemblies not present in test runners) must be split into two layers:

1. A **public wrapper** that calls the private helper inside `try { } catch { }`:
   ```csharp
   public static void LogInfo(string ctx, string msg)
   {
       try { DoLogInformation(Source, FormatMessage(ctx, msg)); }
       catch { }
   }
   ```

2. A **private `[MethodImpl(NoInlining)]` helper** that contains the actual VS call:
   ```csharp
   [MethodImpl(MethodImplOptions.NoInlining)]
   private static void DoLogInformation(string source, string message)
       => ActivityLog.LogInformation(source, message);
   ```

`NoInlining` is critical: it prevents the JIT from inlining `DoLogInformation` into `LogInfo`, which would bring the VS type reference into `LogInfo`'s own JIT scope and break the deferral.

When `LogInfo` is JIT-compiled, it sees only a call-site to `DoLogInformation` — no VS types are referenced. When `DoLogInformation` is called at runtime and its JIT compilation fails, the exception bubbles up through `LogInfo`'s catch block and is silently swallowed.

### Scope

This pattern applies to any helper class in the `src/` project that:
- Is called from test-exercisable code (e.g. `KernelProcessManager`, `KernelClient`)
- Internally calls types from `Microsoft.VisualStudio.Shell.Framework`, `Microsoft.VisualStudio.Interop`, or other VS-only assemblies

### Implications

1. Apply this pattern to any new VS-facade helpers (InfoBar helpers, VS UI helpers, etc.)
2. When adding new VS calls to `ExtensionLogger`, always add a corresponding `[NoInlining]` private method
3. Do NOT add using-level direct calls to VS types in any method that tests can reach without VS

### Related Decisions

- Decision 4: MSTest v4 ThrowsException Pattern (similar testing constraint)

---

## Decision 10: Resource Folder Setup & License Configuration

**Date**: 2026-03-27
**Lead**: Penny (VSIX Packaging & Marketplace Publisher)
**Status**: ACTIVE
**Participants**: Penny

### Summary

Replaced linked/virtual folder approach with a real `src/Resources/` folder on disk. Moved `Icon.png` into Resources. Replaced MIT license with Apache 2.0. Updated csproj and vsixmanifest.

### What Changed

**1. Removed linked/virtual folder approach in csproj**

The csproj previously used `<Link>` to pull files from outside `src/` into virtual paths:
- `<Content Include="..\LICENSE" Link="Resources\LICENSE" />` — linked MIT license from repo root
- `<Content Include="..\art\logo.png" Condition="...">` — conditionally linked from `art/` folder

Both were removed.

**2. Created real `src/Resources/` folder on disk**

`src/Resources/` is now a genuine directory, not a virtual project folder.

**3. Moved `Icon.png` into `src/Resources/`**

`src/Icon.png` was sitting loose at the top of `src/`. Moved to `src/Resources/Icon.png` and referenced directly (no link needed — it lives inside the project tree).

**4. Replaced MIT license with Apache 2.0**

The root `LICENSE` file contained MIT. Per direction, this project uses **Apache 2.0**.
- Created `LICENSE.txt` at repo root with full Apache License 2.0 text, copyright 2026 Mads Kristensen.
- The old `LICENSE` (MIT) file was left in place (not deleted) — decision maker should decide whether to remove it.
- The csproj now links `LICENSE.txt` into the project: `<Content Include="..\LICENSE.txt" Link="Resources\LICENSE.txt" />`

**5. Updated `source.extension.vsixmanifest`**

- `<License>` changed from `Resources\LICENSE` → `Resources\LICENSE.txt`
- `<Icon>` changed from `Resources\logo.png` → `Resources\Icon.png`
- `<PreviewImage>` changed from `Resources\logo.png` → `Resources\Icon.png`

### Implications

1. Real folder structure is industry standard (VSIX Cookbook pattern)
2. License file linked from repo root (single source of truth)
3. Old `LICENSE` (MIT) at root is stale and may confuse license scanning tools

### Related Decisions

- Decision 1: Community.VisualStudio.Toolkit Foundation (affects packaging)

---

---

## Decision 11: Execution Engine Architecture

**Date**: 2026-03-27
**Lead**: Ellie (Editor Extension Specialist)
**Status**: ACTIVE
**Participants**: Ellie

### Rationale

When wiring the Run button to kernel execution, several architectural patterns emerged from integrating `CellExecutionEngine` and `ExecutionCoordinator` with the dotnet-interactive protocol client.

### Decision

**KernelClient Lifecycle**: `KernelClient` takes a `Process` in its constructor and must be created *after* `KernelProcessManager.StartAsync()` completes. `ExecutionCoordinator` owns both the process manager and client, creating the client lazily on first execution request.

**Coordinator Lifetime Token**: `KernelClient.Start()` receives `_lifetimeCts.Token` (lives as long as the editor pane), separate from per-execution `CancellationTokenSource`. Mixing these causes the reader loop to die on first cancellation.

**Output Streaming via Event Correlation**: Intermediate kernel events (ReturnValueProduced, etc.) are filtered by `e.Command?.Token == envelope.Token` directly on the `KernelClient.Events` observable subscription, avoiding polling and buffering.

**Fire-and-Forget Pattern**: Output marshalling uses `ThreadHelper.JoinableTaskFactory.RunAsync(...)` with `#pragma warning disable VSTHRD110, VSSDK007` (`.Forget()` not available in Community.VisualStudio.Toolkit.17 v17.0.549). All exceptions are caught inside the lambda.

**Ownership Hierarchy**: `NotebookEditorPane` owns `KernelProcessManager` and `ExecutionCoordinator`. Both are disposed in `Close()` and `Dispose(bool)`. The coordinator disposes `KernelClient` and `CellExecutionEngine`.

### Implications

1. Any component holding a `KernelClient` reference must obtain it from `ExecutionCoordinator`, never independently.
2. Per-execution cancellation tokens must be distinct from the coordinator's lifetime token.
3. Exception handling for output events is required; unobserved exceptions are caught at the lambda boundary.
4. Fire-and-forget pattern is safe because exceptions are always handled internally; pragma suppression is acceptable.

### Integration Points

- **UI Hook**: `NotebookEditorPane._control.CellRunRequested` event
- **Cell State**: Update `ExecutionStatus`, `ExecutionOrder`, `Outputs` collection
- **Cancellation**: Send `CancelCommand` ("Cancel" type string) to kernel

### Related Decisions

- Decision 5: Custom Editor Architecture (NotebookEditorPane is view + data)
- Decision 2: Async-First, ThreadHelper-Based Threading Model

---

## Decision 12: VizSurface Color Keys for Notebook Semantic Status Indicators

**Date**: 2026-03-27
**Lead**: Wendy (UI & Tool Window Specialist)
**Status**: ACTIVE
**Participants**: Wendy

### Rationale

The notebook cell status indicator needs to show Running / Succeeded / Failed states with meaningful colors that respond to VS theme changes (Light, Dark, Blue, High Contrast). VS `EnvironmentColors` lacks dedicated semantic color keys for general UI use, but the `VizSurface*` palette exists specifically for data-visualization status colors.

### Decision

Use `VsBrushes.VizSurface*MediumKey` for semantic status colors in the notebook UI. These keys are:
- Registered in the VS color table and updated on every theme change
- Correct to use via `SetResourceReference` (fully dynamic, no static brush instances)
- Available for all VS themes including High Contrast

**Mapping**:

| Status | Old | New Key |
|--------|-----|---------|
| Running | `Colors.Orange` (hardcoded) | `VsBrushes.VizSurfaceGoldMediumKey` |
| Succeeded | `#4EC94E` (hardcoded) | `VsBrushes.VizSurfaceGreenMediumKey` |
| Failed | `#F44444` (hardcoded) | `VsBrushes.VizSurfaceRedMediumKey` |
| Error output text | `#F44444` (hardcoded) | `VsBrushes.VizSurfaceRedMediumKey` |

### Rule Established

**Any future semantic color in the notebook UI (badges, indicators, charts) MUST use `SetResourceReference` with a `VsBrushes.*Key` constant.** Static `new SolidColorBrush(...)` assignments are forbidden for theme-sensitive properties.

### High Contrast Compliance

Status meaning is conveyed by both text labels AND color. Text labels ("Running", "✓", "✗") alone provide sufficient HC compliance (WCAG). Color is supplementary polish. All `SetResourceReference` calls are automatically HC-aware because VS maps `VsBrushes` keys to system colors in HC mode.

### Implications

1. All existing semantic-color assignments in `CellToolbar.cs` and `OutputControl.cs` replaced with `SetResourceReference` in this phase.
2. New UI features must follow this pattern; violations cause theming regressions in dark/high-contrast modes.
3. Available VizSurface colors: Green, Red, Gold, Brown, Plum, SteelBlue, StrongBlue, SoftBlue, DarkGold — each with Light/Medium/Dark variants.

### Related Decisions

- Decision 8: Cell UI Code-Only WPF Pattern (System.Xaml reference required for SetResourceReference)

---

## Decision 13: IntelliSense Architecture for Custom TextBox Editor

**Date**: 2026-03-27
**Lead**: Ellie (Editor Extension Specialist)
**Status**: ACTIVE
**Task**: p3-intellisense

### Context

The notebook editor uses a plain WPF `TextBox` (code-only, net48). IntelliSense features (completions, hover, signature help, diagnostics) must be built on top of this without VS editor infrastructure (no ITextView, no MEF-based completion sessions).

### Decisions

#### 1. Provider-per-cell pattern
Each `CellControl` gets its own set of 4 providers (`CompletionProvider`, `HoverProvider`, `SignatureHelpProvider`, `DiagnosticsProvider`). `IntelliSenseManager` owns the dictionary and manages lifecycle via `AttachToCell` / `DetachFromCell`.

**Rationale**: Cells are independent kernel sub-contexts; each needs isolated debounce state and popup positioning.

#### 2. Lazy kernel — IntelliSense inactive until first run
`IntelliSenseManager.SetKernelClient()` is only called when the kernel becomes available (triggered by first cell execution). Until then, providers are attached but silently inactive (null-client fast-path).

**Rationale**: Kernel startup is deferred to first execution. IntelliSense providers are cheap; the kernel process is not.

#### 3. Adorner for diagnostics underlines
`DiagnosticsProvider` uses a `DiagnosticAdorner : Adorner` subclass on the `TextBox`'s adorner layer. Setup is deferred to `TextBox.Loaded` since `AdornerLayer.GetAdornerLayer` requires the visual tree to be established.

**Rationale**: The only way to draw non-interactive overlays (squiggly underlines) on a WPF `TextBox` without replacing it.

#### 4. `CellControl.CodeEditor` internal property
`CellControl` exposes `internal TextBox CodeEditor => _editor;` so `IntelliSenseManager` (same assembly) can attach providers to the TextBox directly.

**Rationale**: Keeps TextBox as an implementation detail of CellControl while still allowing IntelliSense wiring within the `PolyglotNotebooks` assembly.

#### 5. `NotebookControl.IntelliSenseManager` property triggers RebuildCells
Setting the `IntelliSenseManager` property on `NotebookControl` calls `RebuildCells()` so that already-rendered cells get providers attached immediately.

**Rationale**: `RebuildCells` is already the single source of truth for cell rendering; reusing it avoids a separate pass.

### Implications

1. Any future replacement of `TextBox` with a richer editor (e.g., AvalonEdit) must re-implement the 4 providers using that editor's APIs.
2. `RequestSignatureHelpAsync` was added to `KernelClient` — follows the exact same pattern as `RequestCompletionsAsync`/`RequestHoverTextAsync`.
3. `ExecutionCoordinator.KernelClientAvailable` event is now a public seam for any future feature that needs the kernel client after startup.

---

## Decision 14: Rich Output Rendering Architecture

**Date**: 2026-03-27
**Lead**: Wendy (UI & Tool Window Specialist)
**Status**: ACTIVE
**Task**: p3-rich-output

### Summary

Upgraded `OutputControl` to render rich MIME types using WebView2 and WPF Image controls.
Two new helper classes support the expanded rendering pipeline.

### Decisions Made

#### 1. WebView2 as the HTML/Markdown/SVG/CSV Renderer

**Choice**: All HTML-family output (text/html, text/markdown, text/csv, image/svg+xml) renders
inside `WebView2OutputHost` — a `Border` subclass wrapping `Microsoft.Web.WebView2.Wpf.WebView2`.

**Rationale**:
- WebView2 is already in the csproj (`Microsoft.Web.WebView2` v1.0.3179.45).
- WPF has no native HTML or SVG renderer; alternatives (e.g. AvalonEdit + Markdown) require
  additional packages and produce lower-fidelity output.
- WebView2 supports full CSS and layout, making VS-themed HTML output look polished.

**Fallback**: If `EnsureCoreWebView2Async` fails (runtime not installed), a TextBlock with an
install link is shown. `_initFailed = true` prevents repeated init attempts.

#### 2. WebView2 User-Data Folder at `%LOCALAPPDATA%\PolyglotNotebooksVS\WebView2Cache`

**Choice**: Pass an explicit `userDataFolder` to `CoreWebView2Environment.CreateAsync`.

**Rationale**: Without an explicit folder, WebView2 defaults to a subfolder of the host executable
(`devenv.exe`), which is under `Program Files` and may require admin rights. Using LocalAppData
guarantees a writable, per-user cache location.

#### 3. VS Theme Colours via WPF Resource Lookup

**Choice**: Extract CSS colour strings from `Application.Current.TryFindResource(VsBrushes.*Key)`
at navigation time rather than using `VSColorTheme.GetThemedColor(EnvironmentColors.*ColorKey)`.

**Rationale**: The WPF resource lookup uses the same resource dictionary as all other UI controls
and is guaranteed to return the current theme's colour. It avoids a dependency on
`Microsoft.VisualStudio.PlatformUI.VSColorTheme` and `EnvironmentColors`, which require the
`Microsoft.VisualStudio.PlatformUI` namespace and separate using directives.

#### 4. OutputControl Targeted Replace for DisplayedValueUpdated

**Choice**: `OnOutputsChanged` handles `NotifyCollectionChangedAction.Replace` with a targeted
`ReplaceOutputAt(int, CellOutput)` path that disposes only the old slot's IDisposable and swaps
the UIElement in-place, rather than triggering a full `Rebuild()`.

**Rationale**: Full Rebuild destroys and re-creates WebView2 controls, causing visible flicker and
re-initialisation cost. Targeted replace keeps all other slots intact.

**Contract for execution layer (Ellie)**: To update a live display value, replace the matching
`CellOutput` in `cell.Outputs` at its original index using the ObservableCollection indexer
(`cell.Outputs[i] = newOutput`). The UI will update only that slot.

#### 5. Output Area Capped at 500 px with ScrollViewer

**Choice**: The outputs StackPanel is wrapped in a `ScrollViewer { MaxHeight = 500,
VerticalScrollBarVisibility = Auto }`. WebView2 hosts are additionally capped at 480 px
(auto-sized by JS height query after navigation).

**Rationale**: Prevents runaway output from pushing cell content off-screen. Consistent with how
Jupyter and VS Code notebooks handle tall output.

### Implications for Other Agents

- **Ellie (Execution)**: Use `cell.Outputs[index] = newOutput` (replace, not append) to trigger
  in-place update for live displays. Track the index when initially appending the output.
- **Theo (Testing)**: `WebView2OutputHost` requires WebView2 runtime; unit tests that exercise
  `OutputControl.Rebuild()` will not fail even without WebView2 installed (fallback path handles
  init failure gracefully).
- **Penny (Packaging)**: `Microsoft.Web.WebView2` is already in the csproj — no additional
  package references needed.

---

## Decision 15: Notebook Toolbar Architecture

**Date**: 2026-03-27
**Lead**: Vince (Extension Architect)
**Status**: ACTIVE
**Task**: p4-toolbar

### Decision

`NotebookToolbar` is a standalone `Border`-based WPF control (no XAML). It raises events (`RunAllRequested`, `InterruptRequested`, `RestartKernelRequested`, `ClearAllOutputsRequested`) that `NotebookControl` forwards as its own public events. `NotebookEditorPane` handles those events and dispatches to `ExecutionCoordinator` and `KernelProcessManager`.

### Kernel Status Thread-Safety Pattern

`KernelProcessManager.StatusChanged` fires on background threads. The `NotebookEditorPane` handler marshals to the UI thread via:

```csharp
_ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
{
    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
    _control?.UpdateKernelStatus(status);
});
```

`#pragma warning disable VSTHRD110` is required (fire-and-forget pattern, same as `HandleCellRunRequested`). This is the correct VS pattern per Decision 2.

### ExecutionCoordinator Run-All / Cancel Contract

- `HandleRunAllRequested(NotebookDocument)` — fire-and-forget; shares `_currentCts` with single-cell execution. Interrupt (`CancelCurrentExecution()`) cancels Run All mid-flight.
- `CancelCurrentExecution()` — atomically swaps `_currentCts` to null, cancels, and disposes.

### VsBrushes.Key Type

`VsBrushes.*Key` properties return `object`, not `string`. Variables that hold a brush key for later use in `SetResourceReference` must be typed `object`.

### Implications

- **Ellie (Execution)**: No changes to the single-cell execution path. `CancelCurrentExecution()` is available if a cell-level interrupt UI is needed.
- **Wendy (UI)**: The toolbar's `UpdateKernelStatus(KernelStatus)` public API is the integration seam for kernel state display. Future Busy/pulse animation can be added here.
- **Theo (Reliability)**: The `OnKernelStatusChanged` handler in `NotebookEditorPane` must not be called after `Close()`. Current unsubscription in both `Close()` and `Dispose(bool)` covers all paths.

---

## Adding New Decisions

When the Squad makes a new architectural decision:
1. Schedule decision review ceremony (Vince, relevant specialists)
2. Document here with Date, Lead, Rationale, Implications
3. Update routing.md if decision affects specialist routing
4. Notify all agents via squad init

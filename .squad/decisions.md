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

---

## Decision 15: IWpfTextViewHost Keyboard & Resilience Fixes

**Author**: Ellie (Editor Extension Specialist)  
**Date**: 2026-03-28  
**Status**: ACTIVE  

### Context

IWpfTextViewHost was integrated for code cells but had three runtime issues: keyboard input not reaching the text view, no syntax highlighting engagement, and dead adorner code remaining in the file.

### Decision

1. **Try/Catch with TextBox Fallback**: `BuildCodeCellContent` is now wrapped in try/catch. If IWpfTextViewHost creation fails (e.g., MEF services unavailable, content type resolution failure), it logs to ExtensionLogger + ActivityLog and falls back to `BuildCodeCellFallback()` — a plain TextBox with visible text. This ensures cells are always editable.

2. **Explicit Text View Roles**: Changed from `textEditorFactory.DefaultRoles` to explicitly specifying `Editable`, `Interactive`, `Document`, `Zoomable` roles via `CreateTextViewRoleSet()`. This guarantees the text view accepts keyboard input regardless of what `DefaultRoles` contains.

3. **WPF Focus Routing**: Added `hostControl.Focusable = true` and a `GotFocus` handler that calls `Keyboard.Focus(textView.VisualElement)`. This ensures WPF keyboard focus reaches the actual text view surface when the host control receives focus.

4. **Content Type Diagnostic Logging**: `ResolveContentType` now logs: which content type was resolved, whether it fell back to "text", and any lookup failures. This enables debugging syntax highlighting issues at runtime via the VS Activity Log.

5. **Dead Code Cleanup**: Removed `_syntaxAdorner` field, `SetupSyntaxAdorner()`, and `OnCellPropertyChanged()` from CellControl.cs. Removed `using PolyglotNotebooks.Editor.SyntaxHighlighting;`. The SyntaxHighlighting folder files remain (they don't hurt compilation and may be useful for markdown cells later).

6. **Null-Safety for `_editor`**: `_editor` is now `TextBox?` (nullable). For code cells with IWpfTextViewHost, `_editor` is null and `CodeEditor` returns null. IntelliSenseManager skips attachment when `CodeEditor` is null (VS editor provides its own IntelliSense). `AdvanceFocusToNextCell` uses `TextView.VisualElement` for code cells.

### Impact

- **CellControl.cs**: Major refactor of `BuildCodeCellContent`, new `BuildCodeCellFallback`, dead code removal
- **NotebookControl.cs**: `AdvanceFocusToNextCell` now handles IWpfTextView, TextBox, and null cases
- **IntelliSenseManager.cs**: Null guard on `CodeEditor` — skips custom IntelliSense for IWpfTextViewHost cells

### Validation

- Build: Clean (0 errors)
- Tests: 309 passing
- Requires: Live keyboard input testing to validate WPF focus fix

### Future Work

If keyboard input still doesn't work at runtime with the WPF focus fix, the next step is `IVsEditorAdaptersFactoryService.CreateVsTextViewAdapter()` + `IVsTextView.Initialize()` to create views with full OLE command target routing. This would require adding a `Microsoft.VisualStudio.Editor` reference and restructuring view creation.
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

## Decision 10: View Code / View Designer Logical View Strategy

**Date**: 2026-03-28
**Lead**: Ellie
**Status**: ACTIVE
**Participants**: Ellie

### Rationale

We needed F7 (View Code) and Shift+F7 (View Designer) support for .dib/.ipynb files, matching the behavior of .vsixmanifest, .resx, and other VS file types with designer surfaces. Standard VS pattern: claim the views you handle, return `E_NOTIMPL` for views you don't. VS automatically falls back to the built-in text editor for unclaimed logical views.

### Decision

- Our editor factory claims `LOGVIEWID_Primary` and `LOGVIEWID_Designer` (returns `S_OK`)
- Our editor factory does NOT claim `LOGVIEWID_Code` or `LOGVIEWID_TextView` (returns `E_NOTIMPL`)
- `ProvideEditorLogicalView` attribute registered only for `Designer_string`
- Default open (Primary) maps to our WPF notebook UI
- F7 opens raw file content in the text editor without custom text editor implementation — VS does the work

### Implications

1. F7 hotkey now opens file in default text editor (VS built-in fallback)
2. Shift+F7 returns to WPF designer view
3. No need to implement a separate code/text editor UI
4. Pattern aligns with VS conventions for multi-view file types
5. Future designer enhancements (Formula Bar, Property Grid) can extend Designer logical view

### Files Changed

- `src/PolyglotNotebooksPackage.cs` — added `ProvideEditorLogicalView` attribute
- `src/Editor/NotebookEditorFactory.cs` — updated `MapLogicalView` to claim Designer, decline Code/TextView

---

## Decision 5: Markdown Cell UI Architecture (Pure WPF, Rendered Display)

**Date**: 2026-03-27  
**Lead**: Wendy (UI Specialist)  
**Status**: ACTIVE  
**Participants**: Wendy

### Rationale

Markdown cells need fundamentally different UX from code cells: no execution, no kernel selection, and a rendered/edit toggle pattern. Avoids WebView2 overhead by using pure WPF TextBlock-based rendering with Inlines (Bold, Italic, Run).

### Key Decisions

1. **Rendered view uses pure WPF** — StackPanel of TextBlocks with Inlines. No WebView2 for markdown cell display (WebView2 is reserved for output rendering). Keeps cells lightweight.
2. **Double-click to edit, blur to render** — Follows VS designer/XAML pattern. Single click focuses, double-click enters raw text mode, losing focus re-renders.
3. **Dual add-cell buttons** — "＋ Code" and "＋ Markdown" side-by-side replaces single "Add Cell" button.
4. **CellToolbar branches on CellKind** — Markdown cells show "Markdown" label instead of kernel combo. Run, clear, and counter controls hidden. Simplified ··· menu.
5. **Markdown kernel convention** — Markdown cells use `KernelName = "markdown"` and `CellKind = CellKind.Markdown`.

### Implications

- CellControl now branches in constructor based on CellKind; future cell types follow same pattern
- IntelliSense only attached to code cells (guarded in NotebookControl.RebuildCells)
- Shift+Enter on markdown cells advances focus without run events
- InsertCellAt in CellToolbar now requires CellKind parameter

### Files Changed

- `src/Editor/CellControl.cs` — Major (markdown rendering, edit/view toggle, dual content paths)
- `src/Editor/CellToolbar.cs` — Major (markdown-aware toolbar, simplified menu, InsertCellAt signature)
- `src/Editor/NotebookControl.cs` — Moderate (dual add-cell buttons, conditional IntelliSense, focus handling)

### Related Decisions

- Decision 6: Syntax Highlighting via Adorner Overlay (SUPERSEDED)
- Decision 7: ITextViewHost for Code Cell Editors (ACTIVE)

---

## Decision 6: Syntax Highlighting via Adorner Overlay

**Date**: 2026-03-27  
**Lead**: Ellie (Editor Specialist)  
**Status**: SUPERSEDED  
**Participants**: Ellie

### Rationale

Notebook code cells used plain TextBox with no syntax coloring. Adorner overlay pattern avoids TextBox replacement complexity: adorner layer renders colored text while TextBox remains transparent underneath (preserves all existing TextBox APIs and IntelliSense provider integration).

### How It Works

1. `TextBox.Foreground = Transparent` — Text invisible but functional (input, caret, selection intact)
2. `TextBox.CaretBrush` set via SetResourceReference to keep caret visible
3. `SyntaxHighlightAdorner` (WPF Adorner) renders colored text via FormattedText with per-token SetForegroundBrush
4. Regex-based `SyntaxTokenizer` with named groups classifies tokens per language

### Why NOT RichTextBox

All 4 IntelliSense providers (CompletionProvider, HoverProvider, SignatureHelpProvider, DiagnosticsProvider) depend on TextBox-specific APIs: `.Text`, `.CaretIndex`, `.GetRectFromCharacterIndex()`, `.GetCharacterIndexFromPoint()`. Replacing would require rewriting all 4 providers (FlowDocument API is fundamentally different).

### Languages Supported

C#, F#, JavaScript/TypeScript, Python, HTML, SQL, PowerShell via static registry.

### Implications

1. Any future IntelliSense provider can still take TextBox; no API change
2. DiagnosticsProvider adorner coexists on same TextBox layer (no conflicts)
3. Token colors hardcoded to VS Dark theme (frozen brushes); not dynamically theme-aware for syntax tokens
4. If future editor replaces TextBox (e.g., AvalonEdit), adorner AND all IntelliSense providers must migrate together

### New Files

- `src/Editor/SyntaxHighlighting/SyntaxTokenizer.cs` — Base tokenizer + 7 language implementations
- `src/Editor/SyntaxHighlighting/SyntaxHighlightAdorner.cs` — WPF adorner rendering

### Modified Files

- `src/Editor/CellControl.cs` — Adorner wired in BuildCodeCellContent(), subscribes to KernelName changes
- `src/PolyglotNotebooks.csproj` — Compile includes for new files

### Superseded By

- Decision 7: ITextViewHost for Code Cell Editors (2026-03-28) — Native VS editor provides superior theming, IntelliSense, find/replace, code folding

---

## Decision 7: Replace TextBox with IWpfTextViewHost in Code Cells

**Date**: 2026-03-27  
**Lead**: Ellie (Editor Specialist)  
**Status**: ACTIVE  
**Participants**: Ellie

### Rationale

Code cells used TextBox with SyntaxHighlightAdorner overlay for syntax coloring. Limited: no real language services, no VS theming integration, adorner duplicated what VS editor already provides. Hosted IWpfTextViewHost gives native syntax highlighting, VS theming, and all built-in editor features (undo/redo, find/replace, code folding, IntelliSense infrastructure) for free via VS editor platform.

### Decision

Replace TextBox in `BuildCodeCellContent` with hosted VS editor (IWpfTextViewHost) using MEF services. Kernel names map to VS content types. Two-way sync between ITextBuffer and NotebookCell.Contents with suppression flag to prevent loops.

### Key Details

- MEF services obtained via `IComponentModel` / `SComponentModel`
- Services: ITextEditorFactoryService, ITextBufferFactoryService, IContentTypeRegistryService
- Content type resolved from kernel name → VS content type (e.g., "csharp" → "CSharp")
- Two-way sync with suppression flag; content type updates dynamically on kernel change
- `_editor` (TextBox) kept as nullable field for backward compat with IntelliSense providers
- New public `TextView` property exposed for future IWpfTextView consumers
- SyntaxHighlightAdorner no longer used for code cells (files retained in project)
- Markdown cells unchanged (still use TextBox)

### Impact

- **IntelliSense providers** (CompletionProvider, HoverProvider, SignatureHelpProvider, DiagnosticsProvider): Still reference TextBox. Require separate migration to IWpfTextView APIs.
- **SyntaxHighlightAdorner/SyntaxTokenizer**: Files retained; may be used by markdown cells or removed if not needed.
- **Theming**: Code cells automatically respect VS theme (dark/light/high contrast) via content type integration
- **Performance**: No more regex tokenization per keystroke; VS editor handles rendering

### Supersedes

- Decision 6: Syntax Highlighting via Adorner Overlay (SUPERSEDED) — ITextViewHost provides superior solution

### Related Decisions

- Decision 5: Markdown Cell UI Architecture (markdown cells retain TextBox pattern)
- Decision 2: Async-First Threading Model (applies to MEF service resolution)

### User Directive (Copilot)

This decision implements user directive 2026-03-28T00:18:00Z: "Use ITextViewHost for syntax highlighting in notebook cells. Host real VS editor instances (IWpfTextView/ITextViewHost) inside each cell's WPF panel instead of plain TextBox. Map kernel names to VS content types. Do NOT use custom regex tokenizers or TextMateSharp NuGet."

---

## Adding New Decisions

When the Squad makes a new architectural decision:
1. Schedule decision review ceremony (Vince, relevant specialists)
2. Document here with Date, Lead, Rationale, Implications
3. Update routing.md if decision affects specialist routing
4. Notify all agents via squad init


# Decision: Quick-Win Performance Fixes (AutoLoad + VariableService)

**Author:** Vince (Extension Architect)  
**Date:** 2025-07-15  
**Status:** IMPLEMENTED  
**Requested by:** Brady Gaster

## Context

The performance audit (Decision 7) identified two easy, high-value fixes for startup time.

## Fix 1 — Remove ProvideAutoLoad Attributes

**Problem:** Three `[ProvideAutoLoad]` attributes caused the package to load on every VS startup — NoSolution, SolutionExists, and FolderOpened — even when no notebook file is present.

**Solution:** Removed all three. The package still loads on-demand via `[ProvideEditorExtension]` when a `.dib` or `.ipynb` file is opened. This is the correct VS extension pattern — load only when needed.

**Risk:** None. The editor factory registration (`ProvideEditorExtension`) handles on-demand loading. Menu commands are registered lazily by the toolkit.

## Fix 2 — Lazy VariableService Singleton

**Problem:** `VariableService.Initialize()` was called in `InitializeAsync`, creating a singleton with `SemaphoreSlim` and `ObservableCollection` allocations on every VS startup.

**Solution:** Made `VariableService.Current` lazy-init — the singleton is created on first property access instead of during package initialization. This defers all allocations until a notebook is actually opened or the Variable Explorer is shown.

**Approach chosen:** Lazy property getter over moving to `LoadDocData`, because:
- No double-init guard needed (property getter is naturally idempotent)
- No changes to `NotebookEditorPane` initialization order
- Works for both code paths that access `Current` (editor pane + tool window)

**Risk:** Low. `Initialize()` still disposes any previous instance before creating a new one. The property getter is not thread-safe, but `Current` is only accessed from the UI thread context (package init, editor pane, tool window creation).

## Files Changed

- `src/PolyglotNotebooksPackage.cs` — Removed 3 `[ProvideAutoLoad]` lines, removed `VariableService.Initialize()` call
- `src/Variables/VariableService.cs` — Changed `Current` from passive getter to lazy-init property
- `src/Editor/NotebookEditorPane.cs` — Removed null-conditional (`?.`) on `Current` access (no longer nullable)


# Decision: Two-Phase Deferred Code Window Loading

**Author:** Ellie (Editor Extension Specialist)
**Date:** 2025-07-25
**Status:** Implemented

## Context

Opening a .dib/.ipynb file blocked the UI thread for 1-2 seconds on a 10-cell notebook because `CellControl` created an `IVsCodeWindow` (heavy COM object, ~100-200ms each) synchronously in its constructor during `LoadDocData`.

## Decision

Adopted a two-phase loading pattern:

1. **Phase 1 (immediate):** Constructor creates a lightweight read-only `TextBox` placeholder with monospaced font and VS theme colors. No COM calls — renders in <10ms per cell.

2. **Phase 2 (deferred):** After the `Loaded` event, `Dispatcher.BeginInvoke(DispatcherPriority.Background)` swaps in the real `IVsCodeWindow`. UI stays responsive during the heavy work.

Additionally, the kernel installation check (`KernelInstallationDetector`) was changed from blocking `JoinableTaskFactory.Run` to fire-and-forget `RunAsync` so it no longer delays document rendering.

## Files Modified

- `src/Editor/CellControl.cs` — Added `BuildCodeCellPlaceholder()`, `UpgradeToCodeWindow()`, and supporting fields/event handlers
- `src/Editor/NotebookEditorPane.cs` — Changed kernel check from blocking to async fire-and-forget

## Trade-offs

- Users see plain text (no syntax highlighting) for ~200ms per cell during upgrade. Acceptable because the visual transition is smooth (same font, same theme colors).
- The placeholder is read-only — users can't edit until upgrade completes. Upgrade happens within 200ms of visibility, so this is barely noticeable.
- `IntelliSenseManager.AttachToCell()` already no-ops for IVsCodeWindow cells, so no special re-attachment needed.


# Decision: Execution Freeze Fix, IClassifierProvider, Button Hover

**Date**: 2025  
**Author**: Ellie (Editor Extension Specialist)  
**Commit**: 000082f

## Decisions Made

### 1. Use Task.WhenAny for kernel timeouts (not CancellationToken alone)

The `WaitForReadyAsync` and `WaitForTerminalEventAsync` methods used `TaskCompletionSource` with no deadline. Added `Task.WhenAny(tcs.Task, Task.Delay(30s/60s, ct))` pattern. Chosen over a linked CTS with timeout because it preserves the original cancellation semantics while adding a hard deadline.

**Timeouts**: 30s for KernelReady, 60s for command terminal events.

### 2. Immediate ExecutionStatus=Running before kernel startup

Run button disable is driven entirely by `NotebookCell.ExecutionStatus` via `CellToolbar.UpdateStatusIndicator`. The status was previously only set inside `CellExecutionEngine.ExecuteCellAsync` which runs after 5-10s kernel startup. Fix: set `Running` immediately in the coordinator's UI-thread handlers before the async work starts.

### 3. ExecutionCompleted event on ExecutionCoordinator

Added `public event EventHandler? ExecutionCompleted` to `ExecutionCoordinator`, fired from the `finally` blocks of both `HandleCellRunRequested` and `FireAndForget`. This lets `NotebookEditorPane` re-enable the toolbar via `SetExecuting(false)` without polling or callback parameters.

### 4. IClassifierProvider over ITaggerProvider

Multiple failed attempts with `ITaggerProvider`/`[TagType(typeof(ClassificationTag))]`. Switched to `IClassifierProvider` which routes through VS's `IClassifierAggregatorService` pipeline rather than the tagger aggregator. Also calling `IClassifierAggregatorService.GetClassifier(buffer)` immediately after buffer creation forces MEF to instantiate the provider for that buffer without waiting for the editor to request it.

### 5. Regex 250ms timeout on all language patterns

All `LanguagePattern` regex constructors now include `TimeSpan.FromMilliseconds(250)`. This prevents `RegexMatchTimeoutException` crashes reported by the user on pathological input (e.g. long lines, deeply nested constructs). Exceptions are caught in `GetClassificationSpans` and logged as warnings — graceful degradation.

### 6. Remove Button.BackgroundProperty from toolbar buttons

Setting `Button.BackgroundProperty` via `SetResourceReference` overrides the WPF `ButtonBase` ControlTemplate's trigger-based visual states (normal/hover/pressed). Removing this line lets the VS theme's default button template handle hover highlighting correctly.


# Decision: Custom MEF Classifier for Notebook Cell Syntax Highlighting

**Author:** Ellie (Editor Extension Specialist)  
**Date:** 2025  
**Status:** Implemented

## Context

Syntax highlighting was not working for any language in notebook cell buffers hosted via `IVsCodeWindow` adapters. The previous approach (WPF adorner overlay + regex tokenizer on a TextBox) was effectively dead code after the keyboard-input refactor moved cells to `IVsCodeWindow`. VS's built-in classifiers were not engaging for the hosted buffers even when the content type and language service GUID were set correctly.

## Decision

Implement a MEF `ITaggerProvider` + `ITagger<ClassificationTag>` that:

1. Is registered on the `"text"` content type (base of all content types) so it fires unconditionally for any buffer.
2. Immediately gates on a `"PolyglotNotebook.KernelName"` sentinel property set on the buffer in `CellControl.cs`. Non-notebook buffers get `null` returned from `CreateTagger` and are unaffected.
3. Uses VS standard classification type names (`"keyword"`, `"string"`, `"comment"`, `"number"`, `"class name"`) so highlighting respects the user's color theme automatically.
4. Re-uses the per-language regex patterns already developed for the old adorner, ported to emit `ITagSpan<ClassificationTag>`.

## Alternatives Considered

- **Fix VS built-in classifiers**: Attempted via `SetLanguageServiceID` and `ChangeContentType`. VS's built-in language classifiers appear not to engage in HWND-hosted adapter buffers. Not pursued further.
- **Keep adorner approach**: The adorner rendered text using `FormattedText` drawn over a transparent TextBox — incompatible with the `IVsCodeWindow` architecture where VS owns all rendering.

## Key Technical Notes

- `PredefinedClassificationTypeNames` constants require an extra assembly reference not present in this project. Use string literals directly (`"keyword"`, etc.) — these are stable VS constants.
- Register on `"text"` content type, NOT on language-specific types like `"CSharp"`. The buffer adapter assigns content types, but the specific type that activates MEF providers for hosted adapters is the base `"text"` type.
- The `"PolyglotNotebook.KernelName"` property must be added to the **data buffer** (returned by `GetDataBuffer()`) before any MEF tagger providers are invoked, which happens lazily on first view creation.

## Impact

- All 8 kernel languages (C#, F#, JS, TS, Python, PowerShell, SQL, HTML) now get keyword/string/comment/number classification.
- Zero performance impact on non-notebook buffers (null guard at `CreateTagger`).
- Classification colors follow VS theme (no hard-coded brushes).


# Decision: MEF Classifier Engagement Strategy

**Date:** 2026-03-27  
**Author:** Ellie (Editor Extension Specialist)  
**Requested by:** Mads Kristensen

## Context

`NotebookClassifierProvider` is a MEF `ITaggerProvider` registered for the `"text"` content type. It checks for a `"PolyglotNotebook.KernelName"` buffer property to gate activation. Despite being correctly exported, it was not engaging.

## Root Cause

`CellControl.BuildCodeCell` called `IVsTextBuffer.SetLanguageServiceID(ref langServiceId)` on the buffer adapter. This installs the legacy COM language service colorizer (e.g. Roslyn's C# colorizer) which takes precedence over and suppresses MEF `ITaggerProvider` classifiers on that buffer. This is a fundamental VS extensibility constraint: COM colorizers and MEF classifiers are in conflict.

Secondary issues also removed:
- `buffer.ChangeContentType()` forced re-classification in a way that could race with MEF setup
- `ITextDocumentFactoryService.CreateTextDocument()` + temp file write attempted (and failed) to engage Roslyn's MiscellaneousFilesWorkspace, adding noise with no benefit

## Decision

1. **Remove `SetLanguageServiceID`** — our custom MEF classifier handles highlighting; we do not need the legacy COM colorizer.
2. **Remove content type forcing** — the adapter factory already assigns the correct content type.
3. **Remove temp file / ITextDocument creation** — Roslyn engagement is not a goal right now.
4. **Set `PolyglotNotebook.KernelName` property on both data buffer and view's `TextBuffer`** — the VS adapter stack can return different `ITextBuffer` instances; setting on both ensures the tagger sees it.
5. **Add diagnostic logging** to `NotebookClassifierProvider.CreateTagger` — essential for verifying whether the provider is invoked and whether the property check passes.

## Alternatives Considered

- **Manually invoke the classifier via `IClassifierAggregatorService`**: More invasive; deferred unless logging confirms the tagger is never called.
- **Keep `SetLanguageServiceID` and additionally register as a COM colorizer**: Adds significant complexity; not aligned with the MEF-first architecture.

## Follow-up

If diagnostic logs show `CreateTagger` IS called but the property is not found, investigate whether the VS adapter is creating a projection buffer layer that does not carry the property bag forward.


# Decision: Status Icon & Classifier Diagnostics

**Author:** Ellie (Editor Extension Specialist)
**Date:** 2025-07-25

## Task 1: Checkmark → KnownMonikers.TestCoveredPassing

**What changed:** In `CellToolbar.cs`, the success status after cell execution now shows a 20×20 `CrispImage` with `KnownMonikers.TestCoveredPassing` instead of a Unicode "✓" in a TextBlock.

**Approach:** Added a `_statusIcon` CrispImage field alongside the existing `_statusIndicator` TextBlock. Both live in the same DockPanel (Dock.Right). `UpdateStatusIndicator()` toggles visibility between them — CrispImage for Succeeded, TextBlock for Running/Failed/default.

**Why 20×20:** Standard button icons use 16×16. The status icon is slightly larger for visual weight/emphasis per Mads's request.

## Task 2: Classifier Syntax Highlighting Fix

**Root causes addressed:**
1. **"class name" null:** `registry.GetClassificationType("class name")` can return null in some VS configurations. Added fallback to `"type"`.
2. **No initial paint:** Classifier may be created after VS has already painted the buffer. Added a 100ms delayed `ClassificationChanged` event to force re-classification on startup.
3. **No diagnostic visibility:** Added logging of all 5 classification type null-checks in constructor, and per-call logging in `GetClassificationSpans` (kernel, text length, span count).

**Files modified:**
- `src/Editor/CellToolbar.cs`
- `src/Editor/SyntaxHighlighting/NotebookClassifier.cs`


# Decision: Use IVsCodeWindow Adapter for Hosted Editor Cells

**Date**: 2025-07-14  
**Author**: Ellie (Editor Extension Specialist)  
**Status**: ACTIVE  

## Summary

All code cells must be created using `IVsEditorAdaptersFactoryService.CreateVsCodeWindowAdapter()`, not `ITextEditorFactoryService.CreateTextView()`. This is the only pattern that provides a proper Win32 HWND with an IOleCommandTarget chain, which is required for keyboard input to work inside a tool window or pane.

## Context

`ITextEditorFactoryService.CreateTextView()` produces a pure WPF text view with no HWND. VS's Win32 message loop intercepts keystrokes before they reach the WPF visual tree, so they never arrive at the editor. This was discovered as the root cause of keyboard input not working in notebook cells.

## Required Pattern

### CellControl (per cell):
1. `IVsEditorAdaptersFactoryService.CreateVsTextBufferAdapter()` — creates buffer with content type
2. `bufferAdapter.InitializeContent(text, length)` — populate initial text
3. `editorAdapterFactory.GetDataBuffer((IVsTextBuffer)bufferAdapter)` — get MEF ITextBuffer for events
4. `ITextDocumentFactoryService.CreateTextDocument(buffer, fakeFile)` — Roslyn engagement
5. `IVsEditorAdaptersFactoryService.CreateVsCodeWindowAdapter(oleServiceProvider)` — code window with HWND
6. `((IVsCodeWindowEx)codeWindow).Initialize(CWB_DISABLESPLITTER, ...)` — disable splitter bar
7. `codeWindow.SetBuffer((IVsTextLines)bufferAdapter)` — associate buffer
8. `codeWindow.GetPrimaryView()` — get IVsTextView
9. `editorAdapterFactory.GetWpfTextViewHost(vsTextView)` — get WPF host

### NotebookEditorPane:
- Implements `IOleCommandTarget` explicitly to forward Exec/QueryStatus to focused cell's IVsTextView
- `PreProcessMessage` uses `IVsFilterKeys2.TranslateAcceleratorEx` with `VSTAEXF_UseTextEditorKBScope` for proper keyboard-to-command translation

### NotebookControl:
- `GetFocusedCommandTarget()` walks `_cellStack` for the focused cell and returns `cc.VsTextView as IOleCommandTarget`

## Namespace Gotcha

`VSUSERCONTEXTATTRIBUTEUSAGE` is in `Microsoft.VisualStudio.Shell.Interop`, even though `IVsCodeWindowEx` is in `Microsoft.VisualStudio.TextManager.Interop`. Both usings are needed.

## Assembly Reference

Add `Microsoft.VisualStudio.TextManager.Interop.8.0` v17.14.40260 as ExcludeAssets=Runtime PackageReference. The NuGet assemblies are type-forwarder stubs; MSBuild needs explicit reference entries to resolve the type chain correctly.

## What NOT to Do

- Do NOT use `ITextEditorFactoryService.CreateTextView()` for any editor that needs keyboard input
- Do NOT use Win32 SetFocus/GetFocus P/Invoke to route keyboard messages to WPF editors
- Do NOT add `GotFocus` or `PreviewMouseDown` hacks to grab Win32 focus for WPF text views

## Files

- `src/Editor/CellControl.cs` — per-cell editor creation
- `src/Editor/NotebookEditorPane.cs` — pane-level command routing
- `src/Editor/NotebookControl.cs` — focused cell lookup
- `src/PolyglotNotebooks.csproj` — assembly reference

---

## Decision 17: Defer dotnet-interactive Installation Check to First Execution

**Date**: 2026-03-28
**Lead**: Vince (Extension Architect)
**Status**: ACTIVE
**Participants**: Vince

### Rationale

`NotebookEditorPane.LoadDocData` is called synchronously by VS when a notebook file is opened. It previously contained a `JoinableTaskFactory.Run()` block that spawned `dotnet tool list -g` to check if dotnet-interactive is installed. This blocked the UI thread for seconds on cold SDK startup, antivirus interference, or slow PATH resolution.

### Decision

Move the installation check from `LoadDocData` into `ExecutionCoordinator.EnsureKernelStartedAsync()`:

- **LoadDocData** now only does file I/O + parsing (single `JoinableTaskFactory.Run` for `OpenAsync`)
- **EnsureKernelStartedAsync** checks installation lazily on first cell execution
- If not installed, shows `KernelNotInstalledDialog` from an async context
- If the user declines, throws `InvalidOperationException` (caught by fire-and-forget handlers)
- The `KernelInstallationDetector` instance lives as a field on `ExecutionCoordinator` for per-instance cache persistence

### Rationale

1. **Notebook opens instantly** — no child process spawn on the UI thread
2. **Content is always visible** — users can read/edit notebooks even without dotnet-interactive
3. **Natural prompt point** — the dialog appears when the user first tries to execute, which is when the tool is actually needed
4. **Existing error handling** — `EnsureKernelStartedAsync` already has try/catch for kernel failures; installation check fits naturally

### Files Changed

- `src/Editor/NotebookEditorPane.cs` — Removed installation check block from `LoadDocData`
- `src/Execution/ExecutionCoordinator.cs` — Added `KernelInstallationDetector` field; added check in `EnsureKernelStartedAsync` before `StartAsync`

### Constraints Respected

- `LoadDocData` still returns `VSConstants.S_OK` synchronously (VS API contract)
- Notebooks open and display content even without dotnet-interactive installed
- Installation dialog appears at first execution attempt, not at file open

---

## Decision 18: Incremental Cell Collection Change Handling

**Date**: 2026-03-28
**Lead**: Ellie (Editor Extension Specialist)
**Status**: ACTIVE
**Participants**: Ellie

### Rationale

`NotebookControl.OnCellsChanged` previously called `RebuildCells()` on every `NotifyCollectionChangedEventArgs`. This destroyed and recreated ALL CellControl instances — including their IVsCodeWindow HWNDs — even when only a single cell was added or removed. In a 50-cell notebook, adding one cell would tear down and rebuild all 50 embedded editors (~5-10 seconds of visible lag).

### Decision

Replace the full-rebuild handler with incremental handlers dispatched by `NotifyCollectionChangedAction`:

| Action | Handler | Behavior |
|--------|---------|----------|
| Add | `HandleCellsAdded` | Insert only the new CellControl(s) + AddButtons at the correct stack position |
| Remove | `HandleCellsRemoved` | Detach IntelliSense, remove only the affected CellControl + trailing AddButtons |
| Move | `HandleCellsMoved` | Remove at old position, create new CellControl at new position (1 HWND recycled) |
| Replace | `HandleCellsReplaced` | Swap the CellControl at the replaced position |
| Reset | `RebuildCells()` | Full rebuild (correct for clear-all / document swap) |

### Add-Cell Button Index Strategy

Add-cell buttons now compute their insertion index **dynamically at click time** from their position in `_cellStack.Children`, rather than capturing a fixed index at creation. This eliminates stale-index bugs after incremental insertions/removals.

### Layout Invariant

`_cellStack` children follow the pattern `[AddBtn, Cell, AddBtn, Cell, …, AddBtn]`:
- AddButtons panels at even indices (0, 2, 4, …)
- CellControls at odd indices (1, 3, 5, …)
- Cell at document index N → stack position `2*N + 1`

### Move Limitation

WPF fires `Unloaded` when a control is removed from the visual tree, which triggers `CellControl`'s cleanup handler (`_codeWindow?.Close()`). True reparenting without HWND destruction is not possible with the current CellControl teardown pattern. The Move handler creates one new CellControl (1 HWND destroyed + 1 created) — still far better than rebuilding all cells.

### Implications

1. Adding/removing cells is now O(1) in HWND operations instead of O(N)
2. `CreateWiredCellControl(NotebookCell)` is the single source of truth for cell creation + event wiring + IntelliSense attachment
3. Any new cell lifecycle setup must go through `CreateWiredCellControl` (not just `RebuildCells`)
4. If CellControl ever changes its `Unloaded` cleanup to an explicit `Dispose()`, Move could be optimized to true reparenting

### Files Changed

- `src/Editor/NotebookControl.cs` — All changes in this file


---

## Decision 10: Execution Counter Display Kept As-Is

**Date**: 2026-03-28
**Lead**: Ellie (Editor Specialist)
**Status**: ACTIVE
**Participants**: Ellie, User

### Rationale

During output overflow handling audit, discovered execution counter [0] display in CellToolbar.cs. Options considered:
1. Remove counter (simplify toolbar)
2. Rename to [N] per Jupyter convention
3. Keep as [0] (current implementation)

User decided to keep the [0] counter as-is. Rationale: Consistent with early-phase Polyglot design where cell execution numbering is not yet wired. Counter serves as visual placeholder; when execution modes mature, counter will auto-increment per execution flow.

### Implications

1. CellToolbar.cs execution counter remains as [0]
2. Future execution numbering updates will replace this placeholder without breaking changes
3. Variable Explorer, outline, and search don't depend on counter formatting
4. Deferred execution number binding can wire counter to NotebookCell.ExecutionOrder when ready

### Related Decisions

- Decision 9: Markdown Cell Rendering Pattern (toolbar branching applies to counter display too)

---

## Decision 11: Collapse Left Margin Container via GetTextViewMargin("left")

**Date**: 2026-03-28
**Lead**: Ellie (Editor)
**Status**: ACTIVE
**Participants**: Ellie

### Context

The code cell editor already disabled individual left-side margins via `SetOptionValue` (GlyphMarginId, LineNumberMarginId, SelectionMarginId, ChangeTrackingId). However, the margin container itself can still occupy space even when all child margins are disabled.

### Decision

Collapse the entire left margin container using `textViewHost.GetTextViewMargin("left")` and setting its `VisualElement.Visibility` to `Collapsed`. This follows the exact same pattern used for the bottom margin container.

### Rationale

- Consistent with the established bottom-margin collapse pattern
- Belt-and-suspenders: options disable the individual margins, container collapse removes any residual space
- Safe null-check pattern ensures no crash if the margin isn't found

### Implications

1. Cell text editors now have no left or bottom margin space allocated
2. Editing area utilization improved in the notebook interface
3. Pattern `GetTextViewMargin("left")` + `Visibility.Collapsed` is now the standard for margin removal

### Files Changed

- `src/Editor/CellControl.cs` — Left margin container collapse (lines 267-270)

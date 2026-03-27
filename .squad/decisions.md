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

## Adding New Decisions

When the Squad makes a new architectural decision:
1. Schedule decision review ceremony (Vince, relevant specialists)
2. Document here with Date, Lead, Rationale, Implications
3. Update routing.md if decision affects specialist routing
4. Notify all agents via squad init

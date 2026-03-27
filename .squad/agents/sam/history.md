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

**Project format**: SDK-style (`<Project Sdk="Microsoft.NET.Sdk">`) — all `.cs` files auto-included; no manual `<Compile>` entries needed.

**Pre-existing build error**: `CreatePkgDef : TypeLoadException` from VSSDK tooling 18.5.38461 — unrelated to document model code; zero C# compiler errors in our new files.

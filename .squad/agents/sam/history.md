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

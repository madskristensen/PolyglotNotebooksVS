# Ellie History

## 2024-01-XX — Xtenders Founded

**Context**: Founded as specialized team for Visual Studio extension developers.

**Ellie's Focus**:
- Complete editor extensibility pipeline (tokenizer → classifier → renderer)
- IntelliSense completion providers
- CodeLens indicators
- QuickInfo tooltips and hover information
- Text editor margins and adornments
- Language service integration (LSP support)
- TextMate grammar support

**Authority Scope**:
- Token-based tagging and classification
- ITagger, ITaggerProvider, IClassifier implementations
- IntelliSense session management
- CodeLens data point providers
- Quick Info provider design
- Outlining/folding implementations

**Knowledge Base**:
- Token-based editor pipeline architecture
- ITagger inheritance hierarchy
- MEF export patterns for editor components
- Community.VisualStudio.Toolkit editor samples
- Language Server Protocol (LSP) integration patterns

**Key References**:
- VS Editor API Reference (Microsoft Docs)
- Toolkit editor samples
- VSIX Cookbook language services section
- LSP Specification (microsoft.github.io/language-server-protocol)

**Active Integrations**:
- Vince: Architecture validation for editor component MEF exports
- Wendy: Editor UI components (completion popup, quick actions)
- Sam: Symbol indexing coordination for IntelliSense

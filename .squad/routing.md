# Routing Rules

This document defines how Squad work is routed to specialists.

## Extension Architecture & Scaffolding

**Route to: Vince**

- New extension project setup
- .vsixmanifest configuration
- Package class design (ToolkitPackage vs AsyncPackage decisions)
- .vsct command table authoring
- MEF composition planning (exports, imports, content types)
- Multi-VS-version targeting strategy
- Package loading optimization (async, VisibilityConstraints, ProvideAutoLoad)
- Code review authority on all PRs

## Editor Extensibility

**Route to: Ellie**

- Syntax highlighting & token-based classification
- IntelliSense completion providers
- CodeLens indicators
- QuickInfo tooltips (hover information)
- Text editor margins & adornments
- Language service integration
- TextMate grammar support (lightweight languages)
- Language Server Protocol (LSP) integration
- Any token → tagger → classifier → highlighter pipeline work

## User Interface & Tool Windows

**Route to: Wendy**

- Tool window design and implementation (BaseToolWindow<T>)
- WPF control development with VS theming
- Custom editors (IVsEditorFactory patterns)
- Dialogs, modal windows, message boxes
- Info bars, status bar messages, progress indicators
- Icon selection (KnownMonikers) and custom icon registration
- Fonts & colors category registration
- Theme compliance and accessibility

## Solution Explorer & Project System

**Route to: Sam**

- Solution/project event handling
- Solution Explorer node manipulation
- Custom project nodes and hierarchy modification
- File/document operations
- Text buffer manipulation
- Open Folder extensibility (non-project scenarios)
- Error List custom errors, warnings, messages
- Settings & Options pages (BaseOptionModel<T>, UIElementDialogPage)
- Build event interception

## Threading & Reliability

**Route to: Theo**

- JoinableTaskFactory pattern implementation
- ThreadHelper and main thread rules enforcement
- Microsoft.VisualStudio.SDK.Analyzers configuration
- Async/await conversion from synchronous APIs
- Extension error handling (ex.Log(), ex.LogAsync())
- ActivityLog.xml debugging and analysis
- MEF composition error diagnosis
- Performance profiling and optimization
- Protocol URI handling and validation

## VSIX Packaging & Publishing

**Route to: Penny**

- Extension project setup (.csproj, package references)
- .vsixmanifest generation and validation
- VSIX signing for auto-update support
- vs-publish.json configuration
- GitHub Actions CI/CD pipeline setup
- Marketplace listing optimization
- Open VSIX Gallery for nightly builds
- Private gallery hosting (Atom feeds)
- Pre-publish checklist and testing

## Multi-Specialist Work

Some projects require routing to multiple agents:

- **Editor with UI**: Ellie + Wendy (coordinate on editor margins, quick actions UI)
- **New language service**: Ellie + Sam (tokenizer + project system integration)
- **Packaging review**: Penny + Vince + Theo (manifest, architecture, async patterns)
- **Threading audit**: Theo + (specialist for specific component)
- **Architecture design**: Vince + (2-3 specialists based on feature set)

## Escalation

- **Design impasse**: Route to Vince (final authority)
- **Threading confusion**: Route to Theo (authority on VS threading)
- **Quality gate**: Vince reviews all completed work before merge
- **Marketplace issues**: Penny coordinates with Vince on publish decisions

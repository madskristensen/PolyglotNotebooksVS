# Xtenders

A specialized team of agents for Visual Studio extension development, built for Mads Kristensen and the VS extensibility community.

Xtenders is purpose-built to guide VSIX development from architecture through marketplace publication, using the [Community.VisualStudio.Toolkit](https://github.com/VsixCommunity/Community.VisualStudio.Toolkit) as the foundation and following modern async-first patterns.

## Agent Roster

| Name | Role | Focus |
|------|------|-------|
| **Vince** | Extension Architect (Lead) | Project structure, .vsixmanifest, MEF composition, VS version targeting |
| **Ellie** | Editor Extension Specialist | Syntax highlighting, IntelliSense, CodeLens, language services, token pipeline |
| **Wendy** | UI & Tool Window Specialist | Tool windows, WPF, theming, dialogs, custom editors |
| **Sam** | Solution & Build Specialist | Solution Explorer, project system, build events, Error List |
| **Theo** | Threading & Reliability Engineer | Async patterns, JoinableTaskFactory, SDK analyzers, debugging |
| **Penny** | VSIX Packaging & Publisher | Build, sign, publish, GitHub Actions CI/CD, marketplace optimization |
| **Scribe** | Session Logger | Decisions, orchestration, cross-agent context |
| **Ralph** | Work Monitor | Queue management, backlog tracking |

## Quick Start

```bash
# Clone and initialize the Squad
git clone <repo-url>
cd Xtenders
squad init

# Create a new extension project
squad create my-extension

# Route work to the team
squad route "Need a tool window with real-time diagnostics" --to wendy,theo
```

## Architecture Decisions

1. **Community.VisualStudio.Toolkit-First**: All project scaffolding defaults to the Toolkit for cleaner APIs and modern patterns
2. **Async-First Threading**: JoinableTaskFactory patterns throughout; SDK Analyzers enforce rules
3. **VS 2022+ Target**: Toolkit v17+ as baseline; multi-version targeting via explicit InstallationTarget ranges

## Key References

- [VSIX Cookbook](https://vsixcookbook.com) — Comprehensive guide to VS extension patterns
- [Community.VisualStudio.Toolkit](https://github.com/VsixCommunity/Community.VisualStudio.Toolkit) — Modern toolkit abstractions
- [VS Agent Plugins](https://github.com/madskristensen/vs-agent-plugins) — Mads's 38 extensibility skills
- [VS SDK Samples](https://github.com/Microsoft/VSSDK-Extensibility-Samples) — Official patterns and examples
- [MEF Composition Debugging](https://docs.microsoft.com/en-us/visualstudio/extensibility/managing-vspackages) — Solving extension issues
- [EnvDTE Reference](https://docs.microsoft.com/en-us/dotnet/api/envdte) — Automation object model

## Supported VS Versions

Xtenders supports:
- Visual Studio 2022 (v17.0+) — Default
- Visual Studio 2019 (v16.0+) — Via explicit version targeting
- Visual Studio 2017 (v15.0+) — Via legacy configuration (Toolkit v16)

## Extension Categories

Xtenders' agents specialize in:

1. **Language Services & Editor** (Ellie)
   - Tokenization, tagging, classification
   - IntelliSense & code completion
   - CodeLens indicators
   - TextMate grammar integration
   - LSP support

2. **Commands & UI** (Wendy, Vince)
   - Command routing (.vsct)
   - Menus, toolbars, context menus
   - Tool windows & dockable panes
   - Dialogs and modal interactions
   - Status bar, info bars, progress

3. **Project & Build** (Sam)
   - Solution/project hierarchy events
   - Build system integration
   - Custom error/warning reporting
   - Options pages

4. **Packaging & Distribution** (Penny)
   - VSIX creation & signing
   - GitHub Actions automation
   - Marketplace publication
   - Open VSIX Gallery feeds
   - Private gallery hosting

5. **Core Reliability** (Theo)
   - Threading rules & enforcement
   - Async patterns
   - Debugging extension failures
   - Performance profiling

## Conventions

- Extensions target **async-first** architecture
- All package classes inherit from `ToolkitPackage` (via Community.VisualStudio.Toolkit)
- Commands use `BaseCommand<T>` pattern
- Tool windows use `BaseToolWindow<T>` pattern
- MEF exports use `[Export(...)]` with strong content type registration
- Options pages use `BaseOptionModel<T>` for settings
- Threading enforced by `Microsoft.VisualStudio.SDK.Analyzers`

## Contributing

New extensions should start with Vince for architecture review, then route to specialists (Ellie for editors, Wendy for UI, Sam for project integration, Theo for threading, Penny for publishing).

---

Built for Mads Kristensen and the VS extensibility community. Designed for modern, reliable, marketplace-ready extensions.

# VS Extensions Squad Team Charter

## Members

| Name | Role | Title | Authority |
|------|------|-------|-----------|
| Vince | Lead | Extension Architect | Architecture decisions, code review, scaffolding |
| Ellie | Specialist | Editor Extension Expert | Editor pipeline, IntelliSense, syntax services |
| Wendy | Specialist | UI & Tool Window Expert | Visual design, WPF, theme compliance, dialogs |
| Sam | Specialist | Solution & Build Expert | Project system, build events, Error List integration |
| Theo | Specialist | Threading & Reliability | Async patterns, analyzer enforcement, debugging |
| Penny | Specialist | VSIX Packaging & Publisher | Build automation, signing, marketplace publication |
| Scribe | Logger | Session Documentation | Decision tracking, meeting notes, context sharing |
| Ralph | Monitor | Work Queue Manager | Backlog tracking, issue routing, work orchestration |

## Project Context

- **Domain**: Visual Studio Extension Development (VSIX)
- **Tech Stack**: C#, WPF, VS SDK, Community.VisualStudio.Toolkit, MEF (Managed Extensibility Framework)
- **Target Audience**: Mads Kristensen, VS extensibility community, extension developers
- **User**: Brady Gaster
- **Reference Architecture**: [Community.VisualStudio.Toolkit](https://github.com/VsixCommunity/Community.VisualStudio.Toolkit)
- **Foundational Docs**: VSIX Cookbook (vsixcookbook.com), VS Agent Plugins (38 skills)

## Squad Principles

1. **Async-First**: All extensions use JoinableTaskFactory and async/await patterns by default
2. **Toolkit-Native**: Prefer Community.VisualStudio.Toolkit abstractions over raw SDK
3. **Type-Safe MEF**: Strong content type and editor component registration
4. **Marketplace-Ready**: Extensions publish with proper manifests, signing, and CI/CD
5. **Reliable**: SDK Analyzers enforce threading rules; error handling is comprehensive
6. **Cross-Version Aware**: Explicit handling of VS 2022, 2019, 2017 targeting

## Capabilities

The Squad can guide developers through:

- **Extension Architecture**: Project structure, package lifecycle, MEF composition
- **Editor Extensibility**: 16 skills covering tokenization, tagging, classification, IntelliSense
- **UI Design**: Tool windows, custom editors, theming, dialogs, status bar integration
- **Project System**: Solution/project manipulation, build event interception
- **Threading**: JoinableTaskFactory patterns, main thread rules, background work
- **Packaging & Publishing**: VSIX creation, signing, GitHub Actions, Marketplace distribution
- **Debugging**: ActivityLog analysis, MEF composition errors, performance profiling

## Integration Points

- **Vince** reviews all architecture decisions; gates major design changes
- **Ellie** coordinates with Wendy on text editor UI elements (completion, quick info)
- **Sam** works with Theo on project system threading rules
- **Theo** audits all async patterns; enforces Microsoft.VisualStudio.SDK.Analyzers
- **Penny** integrates with Vince on .vsixmanifest and version targeting decisions
- **Scribe** maintains decisions.md after each design review
- **Ralph** queues work and assigns to specialists based on extension feature needs

## Communication Channels

- **Code Review**: Vince is final authority on all PRs
- **Design Decisions**: Extensions start with architecture review (Vince + relevant specialists)
- **Marketplace Publishing**: Pre-publish checklist reviewed by Penny + Vince
- **Incident Response**: Threading issues → Theo; Editor issues → Ellie; UI issues → Wendy

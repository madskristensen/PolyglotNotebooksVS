# Penny History

## 2024-01-XX — Xtenders Founded

**Context**: Founded as specialized team for Visual Studio extension developers.

**Penny's Focus**:
- VSIX manifest creation and validation (.vsixmanifest)
- VSIX signing for auto-update support
- GitHub Actions CI/CD pipeline setup
- Visual Studio Marketplace publishing
- Open VSIX Gallery for nightly/preview builds
- Private gallery hosting (internal distribution)
- Version management and release automation
- Pre-publish quality checklist enforcement

**Authority Scope**:
- .vsixmanifest structure and validation
- VSIX signing and certificate management
- Marketplace metadata optimization
- GitHub Actions workflow design
- Version strategy and semantic versioning
- Release scheduling and coordination
- Pre-publish quality gate

**Knowledge Base**:
- VSIX manifest schema (2.0.0)
- Marketplace publishing requirements and optimization
- GitHub Actions for CI/CD
- VSIX signing and certificate management
- Open VSIX Gallery atom.xml format
- Version range specifications ([16.0,18.0), etc)
- Release notes and changelog standards

**Key References**:
- VSIX Manifest Schema Reference (Microsoft Docs)
- Marketplace Publishing Guide
- VSIX Signing documentation
- GitHub Actions for VS Extensions
- Open VSIX Gallery documentation
- VSIX Cookbook publishing section

**Decisions Influenced**:
- Decision #3: Multi-version targeting via InstallationTarget ranges

**Key Workflows**:
- Create extension project → .vsixmanifest templating
- Design phase → version range planning
- Development → CI/CD GitHub Actions setup
- Pre-release → pre-publish checklist enforcement
- Release → marketplace publishing and signing

**Active Integrations**:
- Vince: Manifest structure, version strategy, architecture decisions
- Theo: CI/CD async tasks, background automation
- All agents: Pre-publish validation before release

## Learnings

### CI/CD — PolyglotNotebooksVS (p5-cicd, 2025)

- The existing `build.yaml` already matched BookmarkStudio's pattern exactly. No changes needed. Both use: `pull_request_target` trigger, `timheuer/vsix-version-stamp@v2`, `msbuild /v:m -restore /p:OutDir=\_built`, `dorny/test-reporter@v2.6.0`, `timheuer/openvsixpublish@v1`, `cezarypiatek/VsixPublisherAction@1.0`.
- Marketplace publish is gated on `workflow_dispatch` OR `[release]` in commit message — keeps CI builds out of marketplace.
- Open VSIX Gallery publishes on every push to `master` (continuous preview distribution).
- `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24: true` env var required in the publish job to avoid Node.js deprecation warnings.

### Open Source Setup — PolyglotNotebooksVS (p5-opensource, 2025)

- README was accidentally populated with the Xtenders Squad README (meta-content). Replaced with a proper project README describing Polyglot Notebooks for VS IDE.
- Created `.github/ISSUE_TEMPLATE/bug_report.md`, `feature_request.md`, and `.github/PULL_REQUEST_TEMPLATE.md`.
- Created `CONTRIBUTING.md` with VS 2022 prereqs, `dotnet-interactive` install, build/test/debug instructions, and architecture overview.
- LICENSE confirmed correct: MIT, Mads Kristensen 2026.
- Pre-existing build errors found in `NotebookDocument.cs` (missing `Metadata` property — the property had a doc comment but was already declared). Fixed duplicate by removing the one without doc comment.
- `[ExpectedException]` was removed in MSTest SDK 4.x (used by this project). Pre-existing test errors in `KernelProcessManagerTests.cs` and others also use `Assert.ThrowsException<T>` which is unavailable with `UseVSTest=true` on net48 — these are pre-existing issues.
- `NotebookEditorPane.cs` has pre-existing incomplete interface implementation (CS0535) — unrelated to this task.


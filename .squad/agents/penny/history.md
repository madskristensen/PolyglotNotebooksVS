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

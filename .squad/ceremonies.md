# Squad Ceremonies

Regular synchronization points for Xtenders.

## 1. Extension Design Review

**Frequency**: Per new extension or major feature
**Lead**: Vince (Architecture)
**Participants**: Vince, Ellie (if editor), Wendy (if UI), Sam (if project), Theo (threading audit)
**Duration**: 30-45 minutes

### Agenda
1. Feature scope and VS requirements
2. Architecture decisions (package type, MEF strategy, async patterns)
3. Component integration plan (who talks to whom)
4. Risk assessment (threading, compatibility, marketplace compliance)
5. Decisions logged to decisions.md

### Outputs
- Approved architecture document
- Agent assignment and ownership
- Implementation roadmap
- Threading audit checklist

## 2. Threading Audit

**Frequency**: Before code review
**Lead**: Theo (Reliability Engineer)
**Participants**: Theo, relevant component owners
**Duration**: 15-30 minutes

### Agenda
1. JoinableTaskFactory usage review
2. Main thread vs background thread classification
3. SDK Analyzer warnings/errors
4. Error handling coverage
5. Async/await conversion validation

### Outputs
- Threading sign-off
- SDK Analyzer pass
- Error handling improvements logged

## 3. Marketplace Publish Review

**Frequency**: Before publishing
**Lead**: Penny (VSIX Packaging)
**Participants**: Penny, Vince, extension owner
**Duration**: 20-30 minutes

### Agenda
1. .vsixmanifest validation (version, tags, description, license)
2. VSIX signing confirmation
3. Release notes and changelog
4. Marketplace listing review (screenshots, description)
5. GitHub Actions CI/CD verification
6. Pre-publish checklist sign-off

### Outputs
- Publish approval
- Release notes finalized
- Marketplace metadata confirmed
- Post-publish monitoring plan

## 4. API Surface & Extensibility Review

**Frequency**: When adding public APIs or Services
**Lead**: Vince (Architecture)
**Participants**: Vince, affected specialists
**Duration**: 20-45 minutes

### Agenda
1. Public API design (service interfaces, export types)
2. MEF registration and component discovery
3. Backward compatibility assessment
4. Documentation requirements
5. Versioning strategy

### Outputs
- API design approval
- MEF registration validated
- Documentation plan
- Breaking change assessment

---

## Meeting Notes Template

```
# [Extension Name] Design Review
**Date**: YYYY-MM-DD
**Attendees**: [Names]
**Lead**: [Name]

## Discussion
- Feature scope: [Description]
- Architecture decision: [Decision]
- Risk assessment: [Risks]
- Threading concerns: [Issues]

## Decisions
- [Decision 1] — Reasoning
- [Decision 2] — Reasoning

## Action Items
- [ ] Item 1 — Owner, Due Date
- [ ] Item 2 — Owner, Due Date

## Participants Sign-Off
- Vince: ✓
- Ellie: ✓
- [Others]: ✓
```

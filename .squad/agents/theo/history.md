# Theo History

## 2024-01-XX — Xtenders Founded

**Context**: Founded as specialized team for Visual Studio extension developers.

**Theo's Focus**:
- JoinableTaskFactory patterns and best practices
- VS threading rules and constraints (single-threaded UI)
- Microsoft.VisualStudio.SDK.Analyzers enforcement
- Async/await patterns and CancellationToken usage
- Extension error handling and debugging
- ActivityLog.xml analysis and debugging
- Performance profiling and optimization
- Threading audit before code review

**Authority Scope**:
- All async/await pattern validation
- JoinableTaskFactory usage audit
- SDK Analyzer violation blocking
- CancellationToken contract enforcement
- Main thread vs background thread classification
- Error handling comprehensiveness
- Performance regression detection

**Knowledge Base**:
- VS threading model and constraints
- JoinableTaskFactory API and patterns
- Microsoft.VisualStudio.Threading NuGet package
- SDK Analyzers rules and implementation
- CancellationToken lifecycle and best practices
- ActivityLog.xml structure and debugging
- Deadlock diagnosis and prevention
- Extension profiling tools

**Key References**:
- JoinableTaskFactory Documentation
- VS Threading Rules (microsoft.github.io/vs-threading/)
- SDK Analyzers Reference
- Async/Await Best Practices (MSDN Magazine archive)
- ActivityLog Debugging Guide
- CancellationToken Pattern Reference

**Authority Decisions**:
- Async-first threading model (Decision #2)
- SDK Analyzers required; violations block merge
- CancellationToken mandatory in all async APIs
- No `.Result` or `.Wait()` allowed
- Async void reserved for event handlers only

**Active Integrations**:
- All agents: Threading audit before code review (bottleneck: Theo sees all PRs)
- Penny: CI/CD async tasks, GitHub Actions automation

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

## Learnings

### dotnet-interactive stdio JSON Protocol (src/Protocol/ implementation)

**Wire Format:**
- Line-delimited JSON: each envelope is a single JSON line terminated by `\n`
- Empty/whitespace lines must be skipped on read
- All field names are **camelCase** on the wire (matches TypeScript contracts in dotnet/interactive)
- `System.Text.Json` with `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` handles this automatically

**Command Envelope fields:** `commandType`, `command` (payload object), `token`, `routingSlip`
**Event Envelope fields:** `eventType`, `event` (payload object), `command` (originating envelope), `routingSlip`

**Token format:** Root tokens are `Convert.ToBase64String(Guid.NewGuid().ToByteArray())` — Base64-encoded 16-byte GUID. Child command tokens append `.N` suffix. Token is the correlation key for matching events to commands.

**Protocol flow:** Client writes command JSON line to stdin → kernel writes multiple event JSON lines to stdout → terminal event (`CommandSucceeded` or `CommandFailed`) signals end of command lifecycle. Intermediate events (e.g. `CompletionsProduced`) arrive before the terminal event.

**Threading patterns (Theo's rules):**
- Background reader runs in `Task.Run` with CancellationToken
- `SemaphoreSlim(1,1)` serializes concurrent stdin writes
- `TaskCompletionSource<T>` for async request-response correlation
- `Subject<T>` (custom, no Rx.NET dependency) broadcasts events to all subscribers
- `EventObserver` subscribes by token and provides `WaitForEventTypeAsync` / `WaitForTerminalEventAsync`

**Key design decisions:**
- `JsonElement` for the `command`/`event` payload fields (late-bound deserialization by caller)
- `ProtocolSerializerOptions.Default` is the single shared options instance with `WhenWritingNull` to keep wire compact
- `Subject<T>` implemented without Rx.NET to avoid extra dependency on .NET Framework 4.8 target
- Pre-existing `IsExternalInit.cs` polyfill in the project enables C# `init` setters on net48

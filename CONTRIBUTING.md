# Contributing to Polyglot Notebooks for Visual Studio

Thank you for your interest in contributing! This document explains how to build, test, and submit changes.

## Prerequisites

- [Visual Studio 2022](https://visualstudio.microsoft.com/) (17.0 or later) with the **Visual Studio extension development** workload
- **.NET Framework 4.8 targeting pack** (included with the VS workload above)
- [`dotnet-interactive`](https://github.com/dotnet/interactive) global tool:
  ```bash
  dotnet tool install --global Microsoft.dotnet-interactive
  ```

## Building

```bash
dotnet build PolyglotNotebooks.slnx
```

Or open `PolyglotNotebooks.slnx` in Visual Studio 2022 and press **Ctrl+Shift+B**.

## Running Tests

```bash
dotnet test
```

## Debugging

1. Open `PolyglotNotebooks.slnx` in Visual Studio 2022.
2. Set `PolyglotNotebooks` as the startup project.
3. Press **F5** — this launches an experimental instance of Visual Studio with the extension loaded.

## Architecture Overview

```txt
src/
  PolyglotNotebooks.csproj   — Main VSIX project (net48, VS 2022+)
  PolyglotNotebooksPackage.cs — AsyncPackage entry point
  Editor/                    — Custom editor for .dib/.ipynb files
  Kernel/                    — dotnet-interactive kernel communication
  Models/                    — Notebook document model
  Protocol/                  — Kernel messaging protocol
  UI/                        — WPF-based notebook cell UI

test/
  PolyglotNotebooks.Test/    — Unit/integration tests
```

The extension uses [Community.VisualStudio.Toolkit](https://github.com/VsixCommunity/Community.VisualStudio.Toolkit) and an async-first threading model (see [decisions.md](.squad/decisions.md)).

## Pull Request Guidelines

- **One feature or fix per PR.** Large changes are harder to review.
- **Tests required** for new behavior. Add them in `test/PolyglotNotebooks.Test/`.
- **Build must pass** — run `dotnet build` and `dotnet test` before opening a PR.
- **No new warnings** — the project has analyzers enabled; fix all warnings.
- Keep commits focused; squash fixups before requesting review.

## Reporting Issues

Please use the [bug report](.github/ISSUE_TEMPLATE/bug_report.md) or [feature request](.github/ISSUE_TEMPLATE/feature_request.md) templates when opening issues.

## License

By contributing you agree that your contributions will be licensed under the [MIT License](LICENSE).

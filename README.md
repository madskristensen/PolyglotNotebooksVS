# Polyglot Notebooks for Visual Studio

[![Build](https://github.com/madskristensen/PolyglotNotebooksVS/actions/workflows/build.yaml/badge.svg)](https://github.com/madskristensen/PolyglotNotebooksVS/actions/workflows/build.yaml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> 🚧 **Under active development** — APIs and features may change between releases.

An interactive notebook experience for **Visual Studio IDE** (not VS Code). Execute C#, JavaScript, and SQL code in cells with rich output, IntelliSense, and cross-language variable sharing. Powered by [.NET Interactive](https://github.com/dotnet/interactive).

## Features (v1 scope)

- **Multi-language cells** — C#, JavaScript, and SQL in a single notebook
- **`.dib` and `.ipynb` file support** — open and save standard notebook formats
- **Rich output** — HTML, images, tables, and plain-text rendering
- **IntelliSense** — completion, signature help, and diagnostics in each cell
- **Cross-language variable sharing** — share values between C# and JavaScript cells
- **Integrated kernel management** — automatic `dotnet-interactive` kernel lifecycle

## Installation

Install from the [Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=MadsKristensen.PolyglotNotebooks):

1. In Visual Studio 2022: **Extensions → Manage Extensions**
2. Search for **Polyglot Notebooks**
3. Click **Download** and restart Visual Studio

Or install via the VSIX from the [latest release](../../releases/latest).

## Prerequisites

The extension requires the `dotnet-interactive` global tool:

```bash
dotnet tool install --global Microsoft.dotnet-interactive
```

Visual Studio 2022 (17.0+) is required.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for build instructions, architecture overview, and PR guidelines.

## License

[MIT](LICENSE) © Mads Kristensen

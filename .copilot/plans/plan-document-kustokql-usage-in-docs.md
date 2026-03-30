# 🎯 Document Kusto/KQL Usage in Docs

## Understanding
The user wants the docs/ markdown files to document how to pull data from Kusto (KQL), including how to connect to a Kusto cluster, run KQL queries in notebook cells, share KQL results with other kernels, and handle long-running queries (stop button, execution timeout). The documentation should follow the existing style and link into the doc index.

## Assumptions
- The existing docs use a consistent style: title, intro, horizontal rule, sections with tables and code blocks, tips, next steps, and a back-link to index
- KQL is already listed in the supported languages table in index.md as `kql` / `#!kql`
- The `#!connect` magic command is used to connect to Kusto clusters (standard dotnet-interactive pattern)
- The new Cell execution timeout setting and Stop button are implemented but not yet documented
- The running-code.md section on interrupting execution needs updating to mention the per-cell Stop button

## Approach
Create a new `docs/kusto-kql.md` guide covering end-to-end KQL usage: prerequisites, connecting to a cluster, running queries, viewing results, sharing data to other kernels, and handling long-running queries. Update the index to link it, update running-code.md to document the Stop button, and update settings.md to document the new Cell execution timeout setting.

## Key Files
- docs/kusto-kql.md - new file: KQL/Kusto guide
- docs/index.md - add KQL guide to the guides table
- docs/running-code.md - update interrupting execution section to cover Stop button
- docs/settings.md - add Cell execution timeout setting documentation

## Risks & Open Questions
- The exact `#!connect` syntax for Kusto may vary by dotnet-interactive version; using the standard documented pattern

**Progress**: 100% [██████████]

**Last Updated**: 2026-03-30 19:48:56

## 📝 Plan Steps
- ✅ **Create docs/kusto-kql.md with the full KQL guide**
- ✅ **Update docs/index.md to add the KQL guide to the guides table**
- ✅ **Update docs/running-code.md to document the per-cell Stop button and link to KQL guide**
- ✅ **Update docs/settings.md to document the Cell execution timeout setting**


# Wendy History

## 2024-01-XX — VS Extensions Squad Founded

**Context**: Founded as specialized team for Visual Studio extension developers.

**Wendy's Focus**:
- Tool window design and implementation (BaseToolWindow<T>)
- WPF UserControl development with VS theming
- Dialog and modal window design
- Status bar, info bar, and progress notifications
- Custom editor implementation (IVsEditorFactory)
- Icon management (KnownMonikers and custom icons)
- Fonts & Colors category registration
- Accessibility and theme compliance

**Authority Scope**:
- Tool window architecture and UX patterns
- WPF control theming ({DynamicResource} bindings, UseVsTheme)
- Dialog creation and styling
- Icon selection and custom icon registration
- Status bar messaging and notifications
- Accessibility validation

**Knowledge Base**:
- BaseToolWindow<T> lifecycle and patterns
- WPF XAML, bindings, data templates
- VS EnvironmentColors and theming
- KnownMonikers library
- Image manifest setup and registration
- Accessibility standards (WCAG, VS conventions)

**Key References**:
- VS Tool Windows API (Microsoft Docs)
- KnownImageIds Reference
- Environment Colors Reference
- Community.VisualStudio.Toolkit UI samples
- Image Service and Catalog documentation

**Active Integrations**:
- Vince: Tool window scaffolding and package registration
- Ellie: Editor UI components (completion, quick info popup styling)
- Sam: Error List UI, custom tree node styling

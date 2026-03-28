# Skill: Hosting VS Editor in WPF Controls

## When to Use
When you need to embed a full Visual Studio text editor (with syntax highlighting, theming, undo/redo) inside a custom WPF control within a VS extension.

## Pattern

```csharp
// 1. Get MEF services
var componentModel = (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
var textEditorFactory = componentModel.GetService<ITextEditorFactoryService>();
var textBufferFactory = componentModel.GetService<ITextBufferFactoryService>();
var contentTypeRegistry = componentModel.GetService<IContentTypeRegistryService>();

// 2. Create buffer with content type
var contentType = contentTypeRegistry.GetContentType("CSharp") ?? contentTypeRegistry.GetContentType("text");
var buffer = textBufferFactory.CreateTextBuffer(initialText, contentType);

// 3. Create view and host
var textView = textEditorFactory.CreateTextView(buffer, textEditorFactory.DefaultRoles);
textView.Options.SetOptionValue(DefaultTextViewOptions.WordWrapStyleId, WordWrapStyles.None);
textView.Options.SetOptionValue(DefaultTextViewHostOptions.LineNumberMarginId, false);
textView.Options.SetOptionValue(DefaultTextViewHostOptions.GlyphMarginId, false);

var host = textEditorFactory.CreateTextViewHost(textView, setFocus: false);
// host.HostControl is a WPF FrameworkElement — add to your Grid/Panel

// 4. Two-way sync with model (use suppression flag to prevent loops)
bool suppress = false;
buffer.Changed += (s, e) => {
    if (suppress) return;
    model.Text = buffer.CurrentSnapshot.GetText();
};
model.PropertyChanged += (s, e) => {
    if (e.PropertyName != "Text") return;
    var current = buffer.CurrentSnapshot.GetText();
    if (current != model.Text) {
        suppress = true;
        using (var edit = buffer.CreateEdit()) {
            edit.Replace(new Microsoft.VisualStudio.Text.Span(0, buffer.CurrentSnapshot.Length), model.Text ?? "");
            edit.Apply();
        }
        suppress = false;
    }
};

// 5. Cleanup on unload
host.HostControl.Unloaded += (s, e) => host.Close();
```

## Required Namespaces
```csharp
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
```

## Gotchas
- `Span` is ambiguous with `System.Windows.Documents.Span` — fully qualify as `Microsoft.VisualStudio.Text.Span`
- Content type names are case-sensitive: "CSharp" not "csharp", "F#" not "fsharp"
- Always call `host.Close()` on cleanup to avoid leaks
- Use `buffer.ChangeContentType()` to switch languages dynamically

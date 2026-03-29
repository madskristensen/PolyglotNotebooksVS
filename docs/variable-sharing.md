# Variable Sharing

One of the most powerful features of Polyglot Notebooks is the ability to share variables between different language kernels. Define a value in C#, use it in JavaScript. Query data in SQL, analyze it in F#. This guide explains how.

---

## How It Works

Each language kernel in a notebook has its own isolated scope. Variables defined in a C# cell are not automatically visible in a JavaScript cell. The `#!share` magic command bridges this gap by copying a variable's value from one kernel into another.

Under the hood, `dotnet-interactive` serializes the variable (typically as JSON), transfers it to the target kernel, and deserializes it into a native variable.

## The `#!share` Command

### Syntax

```
#!share --from <source-kernel> <variable-name>
```

### Example: C# → JavaScript

**Cell 1 (C#)**:

```csharp
var greeting = "Hello";
var target = "Polyglot Notebooks";
Console.WriteLine($"{greeting}, {target}!");
```

**Cell 2 (JavaScript)**:

```javascript
#!share --from csharp greeting
#!share --from csharp target

console.log(`${greeting}, ${target}! (from JavaScript)`);
```

### Example: JavaScript → C#

**Cell 3 (JavaScript)**:

```javascript
const jsMessage = "I was born in JavaScript";
```

**Cell 4 (C#)**:

```csharp
#!share --from javascript jsMessage

Console.WriteLine($"C# received: \"{jsMessage}\"");
```

## What Can Be Shared

- **Primitive types** — strings, numbers, booleans
- **Collections** — arrays, lists
- **Objects** — any type that can be serialized as JSON
- **Data tables** — query results, structured data

### Limitations

- **Complex types** must be JSON-serializable. Types with circular references or non-serializable fields may fail.
- **Shared values are copies.** Modifying a shared variable in the target kernel does not affect the original in the source kernel.
- **The source cell must be executed first.** The variable must exist in the source kernel's state before you can share it.

## The Variable Explorer

The Variable Explorer is a dedicated tool window that shows all live variables across all kernels.

### Opening the Variable Explorer

Go to **View → Polyglot Variables** in the Visual Studio menu bar.

### What It Shows

The Variable Explorer displays a table with four columns:

| Column | Description |
|--------|-------------|
| **Name** | The variable name |
| **Type** | The variable's type (e.g., `String`, `Int32`, `List<Product>`) |
| **Value** | A preview of the variable's value |
| **Kernel** | Which kernel owns this variable (e.g., `csharp`, `javascript`) |

### Using the Variable Explorer

- **Click any row** to see the full value in the detail pane at the bottom of the window.
- **Click Refresh** to update the variable list after running cells.
- **Sort by column** — click any column header to sort the table.
- The explorer shows an empty state ("Run a cell to see variables") until you execute at least one cell.

### When to Use It

The Variable Explorer is especially useful for:

- **Debugging** — check whether a variable has the value you expect
- **Exploring data** — see the shape and content of complex objects
- **Cross-kernel workflows** — verify that shared variables arrived correctly in the target kernel

## Multi-Language Data Pipelines

Variable sharing enables powerful multi-language workflows. Here's a real-world pattern:

### Step 1: Query Data in SQL

```sql
SELECT Name, Category, Price FROM Products WHERE Price > 10
```

### Step 2: Share Results to C#

```csharp
#!share --from sql queryResults

// Process the data with LINQ
var summary = queryResults
    .GroupBy(r => r.Category)
    .Select(g => new { Category = g.Key, Average = g.Average(r => r.Price) });

display(summary);
```

### Step 3: Share to JavaScript for Visualization

```javascript
#!share --from csharp summary

// Use the data for charting or custom rendering
console.log(JSON.stringify(summary, null, 2));
```

## Within-Kernel State Sharing

Variables are shared automatically between cells of the **same** kernel — no `#!share` needed:

**Cell 1 (C#)**:

```csharp
var message = "Defined in cell 1";
```

**Cell 2 (C#)**:

```csharp
// message is already available — same kernel
Console.WriteLine(message);
```

This works because all C# cells share a single kernel process. The same applies to all other languages.

## Tips

- **Execute cells in order.** The source variable must exist before you share it. If you run cells out of order, `#!share` may fail with "variable not found."
- **Use the Variable Explorer to verify.** After sharing, check the Variable Explorer to confirm the variable appears in the target kernel with the expected value.
- **Keep shared data simple.** Prefer primitive types, strings, and flat objects for reliable cross-kernel sharing.
- **Multiple shares in one cell.** You can include multiple `#!share` lines at the top of a cell to import several variables at once.

## Next Steps

- [Rich Output](rich-output.md) — display the data you're working with
- [Running Code](running-code.md) — understand execution order for reliable sharing
- [Troubleshooting](troubleshooting.md) — fix common sharing issues

← [Back to Documentation Index](index.md)

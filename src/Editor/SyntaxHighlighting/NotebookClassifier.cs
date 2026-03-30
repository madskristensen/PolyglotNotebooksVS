using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;

using PolyglotNotebooks.Diagnostics;

using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PolyglotNotebooks.Editor.SyntaxHighlighting
{
    /// <summary>
    /// Regex-based <see cref="IClassifier"/> that emits
    /// <see cref="ClassificationSpan"/> spans for notebook cell buffers.
    /// The language is determined by the <c>"PolyglotNotebook.KernelName"</c>
    /// property stored on the <see cref="ITextBuffer"/>.
    /// </summary>
    internal sealed class NotebookClassifier : IClassifier
    {
        private readonly ITextBuffer _buffer;
        private readonly IClassificationType _keyword;
        private readonly IClassificationType _string;
        private readonly IClassificationType _comment;
        private readonly IClassificationType _number;
        private readonly IClassificationType _typeName;
        private LanguagePattern? _language;
        private readonly SynchronizationContext? _syncContext;

        public event EventHandler<ClassificationChangedEventArgs> ClassificationChanged = delegate { };

        internal NotebookClassifier(ITextBuffer buffer, IClassificationTypeRegistryService registry)
        {
            _buffer = buffer;
            _syncContext = SynchronizationContext.Current;
            _keyword = registry.GetClassificationType("keyword");
            _string = registry.GetClassificationType("string");
            _comment = registry.GetClassificationType("comment");
            _number = registry.GetClassificationType("number");
            _typeName = registry.GetClassificationType("class name");

            var kernelName = buffer.Properties.GetProperty<string>("PolyglotNotebook.KernelName");
            _language = LanguagePattern.Get(kernelName);

            _buffer.Changed += OnBufferChanged;

            // Force VS to re-classify after the classifier is attached.
            // Without this, the editor may have already completed its initial
            // classification pass before the provider returned this classifier,
            // leaving the buffer un-highlighted.
            _ = Task.Delay(100).ContinueWith(_ =>
            {
                var snapshot = _buffer.CurrentSnapshot;
                if (snapshot.Length > 0)
                {
                    RaiseClassificationChanged(
                        new SnapshotSpan(snapshot, 0, snapshot.Length));
                }
            }, TaskScheduler.Default);
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            foreach (var change in e.Changes)
            {
                RaiseClassificationChanged(
                    new SnapshotSpan(e.After, change.NewSpan));
            }
        }

        /// <summary>
        /// Updates the cached language pattern when the cell's kernel name changes.
        /// Raises <see cref="ClassificationChanged"/> to force a full re-classify.
        /// </summary>
        internal void UpdateLanguage(string kernelName)
        {
            _language = LanguagePattern.Get(kernelName);
            var snapshot = _buffer.CurrentSnapshot;
            if (snapshot.Length > 0)
            {
                RaiseClassificationChanged(
                    new SnapshotSpan(snapshot, 0, snapshot.Length));
            }
        }

        /// <summary>
        /// Raises <see cref="ClassificationChanged"/> on the UI thread.
        /// VS expects this event on the main thread; buffer.Changed and
        /// Task continuations may fire on a ThreadPool thread.
        /// </summary>
        private void RaiseClassificationChanged(SnapshotSpan span)
        {
            var handler = ClassificationChanged;
            if (handler == null) return;

            var args = new ClassificationChangedEventArgs(span);
            if (_syncContext != null && SynchronizationContext.Current != _syncContext)
            {
#pragma warning disable VSSDK007 // Intentional fire-and-forget
                _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    handler(this, args);
                });
#pragma warning restore VSSDK007
            }
            else
            {
                handler(this, args);
            }
        }

        public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
        {
            var result = new List<ClassificationSpan>();
            if (_language == null)
                return result;

            var snapshot = span.Snapshot;

            try
            {
                var startLine = snapshot.GetLineFromPosition(span.Start);
                var endLine = snapshot.GetLineFromPosition(span.End > 0 ? span.End - 1 : span.End);

                var lineStart = startLine.Start.Position;
                var lineEnd = endLine.End.Position;
                if (lineEnd < lineStart || lineEnd > snapshot.Length)
                    return result;

                var lineSpan = new SnapshotSpan(snapshot, lineStart, lineEnd - lineStart);
                var text = lineSpan.GetText();
                var matches = _language.Pattern.Matches(text);
                foreach (Match m in matches)
                {
                    if (!m.Success || m.Length == 0) continue;

                    var classificationType = Classify(m);
                    if (classificationType == null) continue;

                    int spanStart = lineStart + m.Index;
                    int spanLength = m.Length;

                    // Guard against stale snapshot or out-of-bounds regex match
                    if (spanStart < 0 || spanLength <= 0 || spanStart + spanLength > snapshot.Length)
                        continue;

                    var tagSpan = new SnapshotSpan(snapshot, spanStart, spanLength);
                    if (tagSpan.IntersectsWith(span))
                        result.Add(new ClassificationSpan(tagSpan, classificationType));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                ExtensionLogger.LogWarning("NotebookClassifier", "Regex timed out during classification.");
            }
            catch (ArgumentOutOfRangeException)
            {
                // Buffer changed between getting text and creating spans — safe to ignore
            }

            return result;
        }

        private IClassificationType? Classify(Match m)
        {
            if (m.Groups["comment"].Success) return _comment;
            if (m.Groups["string"].Success) return _string;
            if (m.Groups["keyword"].Success) return _keyword;
            if (m.Groups["type"].Success) return _typeName;
            if (m.Groups["number"].Success) return _number;
            return null;
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Per-language regex patterns
    // ────────────────────────────────────────────────────────────────────────────

    internal sealed class LanguagePattern
    {
        private static readonly Dictionary<string, LanguagePattern> _registry =
            new Dictionary<string, LanguagePattern>(StringComparer.OrdinalIgnoreCase)
            {
                ["csharp"] = new LanguagePattern(BuildCSharp()),
                ["c#"] = new LanguagePattern(BuildCSharp()),
                ["fsharp"] = new LanguagePattern(BuildFSharp()),
                ["f#"] = new LanguagePattern(BuildFSharp()),
                ["javascript"] = new LanguagePattern(BuildJavaScript()),
                ["js"] = new LanguagePattern(BuildJavaScript()),
                ["typescript"] = new LanguagePattern(BuildTypeScript()),
                ["ts"] = new LanguagePattern(BuildTypeScript()),
                ["python"] = new LanguagePattern(BuildPython()),
                ["powershell"] = new LanguagePattern(BuildPowerShell()),
                ["pwsh"] = new LanguagePattern(BuildPowerShell()),
                ["sql"] = new LanguagePattern(BuildSql()),
                ["kql"] = new LanguagePattern(BuildKql()),
                ["kusto"] = new LanguagePattern(BuildKql()),
                ["html"] = new LanguagePattern(BuildHtml()),
            };

        public Regex Pattern { get; }

        private LanguagePattern(Regex pattern) => Pattern = pattern;

        public static LanguagePattern? Get(string kernelName)
        {
            if (string.IsNullOrEmpty(kernelName)) return null;
            if (_registry.TryGetValue(kernelName, out var lang))
                return lang;

            // Handle composite kernel names like "kql-Ddtelvsraw" or "sql-myServer"
            int dashIndex = kernelName.IndexOf('-');
            if (dashIndex > 0)
            {
                var baseKernel = kernelName.Substring(0, dashIndex);
                _registry.TryGetValue(baseKernel, out lang);
            }
            return lang;
        }

        // ── C# ──────────────────────────────────────────────────────────────

        private static Regex BuildCSharp() => new Regex(
            @"(?<comment>//[^\n]*|/\*[\s\S]*?\*/)" +
            @"|(?<string>@""(?:""""|[^""])*""|""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])')" +
            @"|(?<type>\b(?:bool|byte|char|decimal|double|float|int|long|object|sbyte|short|string|uint|ulong|ushort|var|dynamic|nint|nuint)\b)" +
            @"|(?<keyword>\b(?:abstract|as|async|await|base|break|case|catch|checked|class|const|continue|default|delegate|do|else|enum|event|explicit|extern|false|finally|fixed|for|foreach|goto|if|implicit|in|interface|internal|is|lock|namespace|new|null|operator|out|override|params|private|protected|public|readonly|record|ref|required|return|sealed|sizeof|stackalloc|static|struct|switch|this|throw|true|try|typeof|unchecked|unsafe|using|virtual|void|volatile|when|where|while|yield|global|init|nameof)\b)" +
            @"|(?<number>\b(?:0[xX][0-9a-fA-F_]+[uUlL]{0,2}|0[bB][01_]+[uUlL]{0,2}|\d[\d_]*(?:\.[\d_]+)?(?:[eE][+-]?\d+)?[fFdDmMuUlL]{0,2})\b)",
            RegexOptions.Compiled | RegexOptions.Multiline,
            TimeSpan.FromMilliseconds(250));

        // ── F# ──────────────────────────────────────────────────────────────

        private static Regex BuildFSharp() => new Regex(
            @"(?<comment>//[^\n]*|\(\*[\s\S]*?\*\))" +
            @"|(?<string>""""""[\s\S]*?""""""|""(?:\\.|[^""\\])*"")" +
            @"|(?<type>\b(?:bool|byte|char|decimal|double|float|float32|int|int16|int32|int64|sbyte|single|string|uint|uint16|uint32|uint64|unit|bigint|nativeint|unativeint)\b)" +
            @"|(?<keyword>\b(?:abstract|and|as|assert|base|begin|class|default|delegate|do|done|downcast|downto|elif|else|end|exception|extern|false|finally|for|fun|function|global|if|in|inherit|inline|interface|internal|lazy|let|match|member|module|mutable|namespace|new|not|null|of|open|or|override|private|public|rec|return|static|struct|then|to|true|try|type|upcast|use|val|void|when|while|with|yield)\b)" +
            @"|(?<number>\b(?:0[xX][0-9a-fA-F_]+|0[bB][01_]+|0[oO][0-7_]+|\d[\d_]*(?:\.[\d_]+)?(?:[eE][+-]?\d+)?)[ysnlLfFmM]?\b)",
            RegexOptions.Compiled | RegexOptions.Multiline,
            TimeSpan.FromMilliseconds(250));

        // ── JavaScript ──────────────────────────────────────────────────────

        private static Regex BuildJavaScript() => new Regex(
            @"(?<comment>//[^\n]*|/\*[\s\S]*?\*/)" +
            @"|(?<string>`(?:\\.|[^`\\])*`|""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*')" +
            @"|(?<keyword>\b(?:async|await|break|case|catch|class|const|continue|debugger|default|delete|do|else|export|extends|false|finally|for|from|function|get|if|import|in|instanceof|let|new|null|of|return|set|static|super|switch|this|throw|true|try|typeof|undefined|var|void|while|with|yield)\b)" +
            @"|(?<number>\b(?:0[xX][0-9a-fA-F_]+|0[bB][01_]+|0[oO][0-7_]+|\d[\d_]*(?:\.[\d_]+)?(?:[eE][+-]?\d+)?n?)\b)",
            RegexOptions.Compiled | RegexOptions.Multiline,
            TimeSpan.FromMilliseconds(250));

        // ── TypeScript ──────────────────────────────────────────────────────

        private static Regex BuildTypeScript() => new Regex(
            @"(?<comment>//[^\n]*|/\*[\s\S]*?\*/)" +
            @"|(?<string>`(?:\\.|[^`\\])*`|""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*')" +
            @"|(?<type>\b(?:any|boolean|never|number|object|string|symbol|unknown|bigint)\b)" +
            @"|(?<keyword>\b(?:abstract|as|async|await|break|case|catch|class|const|continue|debugger|declare|default|delete|do|else|enum|export|extends|false|finally|for|from|function|get|if|implements|import|in|instanceof|interface|keyof|let|module|namespace|new|null|of|package|private|protected|public|readonly|require|return|set|static|super|switch|this|throw|true|try|type|typeof|undefined|var|void|while|with|yield)\b)" +
            @"|(?<number>\b(?:0[xX][0-9a-fA-F_]+|0[bB][01_]+|0[oO][0-7_]+|\d[\d_]*(?:\.[\d_]+)?(?:[eE][+-]?\d+)?n?)\b)",
            RegexOptions.Compiled | RegexOptions.Multiline,
            TimeSpan.FromMilliseconds(250));

        // ── Python ──────────────────────────────────────────────────────────

        private static Regex BuildPython() => new Regex(
            @"(?<comment>#[^\n]*)" +
            @"|(?<string>""""""[\s\S]*?""""""|'''[\s\S]*?'''|""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*')" +
            @"|(?<type>\b(?:bool|bytes|bytearray|complex|dict|float|frozenset|int|list|memoryview|object|range|set|str|tuple|type)\b)" +
            @"|(?<keyword>\b(?:False|None|True|and|as|assert|async|await|break|class|continue|def|del|elif|else|except|finally|for|from|global|if|import|in|is|lambda|nonlocal|not|or|pass|raise|return|try|while|with|yield)\b)" +
            @"|(?<number>\b(?:0[xX][0-9a-fA-F_]+|0[bB][01_]+|0[oO][0-7_]+|\d[\d_]*(?:\.[\d_]+)?(?:[eE][+-]?\d+)?j?)\b)",
            RegexOptions.Compiled | RegexOptions.Multiline,
            TimeSpan.FromMilliseconds(250));

        // ── PowerShell ──────────────────────────────────────────────────────

        private static Regex BuildPowerShell() => new Regex(
            @"(?<comment>#[^\n]*|<#[\s\S]*?#>)" +
            @"|(?<string>@""[\s\S]*?""@|@'[\s\S]*?'@|""(?:`.|[^""`])*""|'[^']*')" +
            @"|(?<type>\$[a-zA-Z_]\w*)" +
            @"|(?<keyword>\b(?:begin|break|catch|class|continue|data|define|do|dynamicparam|else|elseif|end|enum|exit|filter|finally|for|foreach|from|function|hidden|if|in|param|process|return|static|switch|throw|trap|try|until|using|var|while)\b)" +
            @"|(?<number>\b(?:0[xX][0-9a-fA-F]+|\d+(?:\.\d+)?(?:[eE][+-]?\d+)?(?:kb|mb|gb|tb|pb)?)\b)",
            RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(250));

        // ── SQL ─────────────────────────────────────────────────────────────

        private static Regex BuildSql() => new Regex(
            @"(?<comment>--[^\n]*|/\*[\s\S]*?\*/)" +
            @"|(?<string>'(?:''|[^'])*'|N'(?:''|[^'])*')" +
            @"|(?<type>\b(?:INT|INTEGER|BIGINT|SMALLINT|TINYINT|BIT|DECIMAL|NUMERIC|FLOAT|REAL|MONEY|CHAR|VARCHAR|NCHAR|NVARCHAR|TEXT|NTEXT|DATE|DATETIME|DATETIME2|TIME|TIMESTAMP|BINARY|VARBINARY|IMAGE|XML|UNIQUEIDENTIFIER|SQL_VARIANT|CURSOR)\b)" +
            @"|(?<keyword>\b(?:SELECT|FROM|WHERE|AND|OR|NOT|INSERT|INTO|VALUES|UPDATE|SET|DELETE|CREATE|TABLE|ALTER|DROP|INDEX|VIEW|JOIN|INNER|LEFT|RIGHT|OUTER|FULL|ON|AS|ORDER|BY|GROUP|HAVING|DISTINCT|UNION|ALL|EXISTS|IN|BETWEEN|LIKE|IS|NULL|COUNT|SUM|AVG|MIN|MAX|CASE|WHEN|THEN|ELSE|END|DECLARE|BEGIN|COMMIT|ROLLBACK|EXEC|EXECUTE|PROCEDURE|FUNCTION|TRIGGER|TOP|LIMIT|OFFSET|PRIMARY|KEY|FOREIGN|REFERENCES|CONSTRAINT|DEFAULT|CHECK|UNIQUE|IDENTITY|WITH|GO|USE|PRINT|IF|WHILE|RETURN|GRANT|REVOKE|DENY|CROSS|ASC|DESC|DATABASE|SCHEMA)\b)" +
            @"|(?<number>\b\d+(?:\.\d+)?\b)",
            RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(250));

        // ── HTML ────────────────────────────────────────────────────────────

        private static Regex BuildHtml() => new Regex(
            @"(?<comment><!--[\s\S]*?-->)" +
            @"|(?<string>""[^""]*""|'[^']*')" +
            @"|(?<type>\b[a-zA-Z\-]+(?=\s*=))" +
            @"|(?<keyword></?[a-zA-Z][a-zA-Z0-9]*\b|/?>)",
            RegexOptions.Compiled | RegexOptions.Multiline,
            TimeSpan.FromMilliseconds(250));

        // ── KQL (Kusto Query Language) ──────────────────────────────────────

        private static Regex BuildKql() => new Regex(
            @"(?<comment>//[^\n]*)" +
            @"|(?<string>""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|@""(?:""""|[^""])*"")" +
            @"|(?<type>\b(?:bool|datetime|decimal|dynamic|guid|int|long|real|string|timespan)\b)" +
            @"|(?<keyword>\b(?:let|where|project|extend|summarize|count|sum|avg|min|max|take|limit|top|order|by|asc|desc|join|on|union|render|print|search|find|mv-expand|mv-apply|parse|evaluate|distinct|sort|as|between|contains|has|startswith|endswith|matches|regex|in|and|or|not|ago|now|bin|toscalar|datatable|range|materialize|externaldata|invoke|database|cluster|set|alias|restrict|access|pattern|declare|query_parameters|ingestion_time|pack|pack_all|bag_unpack|getschema|column_ifexists|iff|iif|case|coalesce|tostring|toint|tolong|todouble|todecimal|todatetime|totimespan|tobool|toguid|strlen|substring|trim|replace|split|strcat|strcat_delim|indexof|countof|extract|format_datetime|format_timespan|make_set|make_list|make_bag|arg_max|arg_min|any|dcount|dcountif|countif|sumif|avgif|percentile|percentiles|stdev|variance|hll|hll_merge|tdigest|tdigest_merge)\b)" +
            @"|(?<number>\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b)",
            RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(250));
    }
}

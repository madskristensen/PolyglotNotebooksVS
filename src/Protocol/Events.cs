using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PolyglotNotebooks.Protocol
{
    public class KernelReady
    {
        [JsonPropertyName("kernelInfos")]
        public List<KernelInfo> KernelInfos { get; set; } = new List<KernelInfo>();
    }

    public class KernelInfo
    {
        [JsonPropertyName("localName")]
        public string LocalName { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("languageName")]
        public string? LanguageName { get; set; }

        [JsonPropertyName("supportedKernelCommands")]
        public List<KernelCommandInfo> SupportedKernelCommands { get; set; } = new List<KernelCommandInfo>();
    }

    public class KernelCommandInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class CommandSucceeded
    {
        [JsonPropertyName("executionOrder")]
        public int? ExecutionOrder { get; set; }
    }

    public class CommandFailed
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("executionOrder")]
        public int? ExecutionOrder { get; set; }
    }

    public class CodeSubmissionReceived
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;
    }

    public class ReturnValueProduced
    {
        [JsonPropertyName("formattedValues")]
        public List<FormattedValue> FormattedValues { get; set; } = new List<FormattedValue>();

        [JsonPropertyName("valueId")]
        public string? ValueId { get; set; }
    }

    public class StandardOutputValueProduced
    {
        [JsonPropertyName("formattedValues")]
        public List<FormattedValue> FormattedValues { get; set; } = new List<FormattedValue>();
    }

    public class StandardErrorValueProduced
    {
        [JsonPropertyName("formattedValues")]
        public List<FormattedValue> FormattedValues { get; set; } = new List<FormattedValue>();
    }

    public class DisplayedValueProduced
    {
        [JsonPropertyName("formattedValues")]
        public List<FormattedValue> FormattedValues { get; set; } = new List<FormattedValue>();

        [JsonPropertyName("valueId")]
        public string? ValueId { get; set; }
    }

    public class DisplayedValueUpdated
    {
        [JsonPropertyName("formattedValues")]
        public List<FormattedValue> FormattedValues { get; set; } = new List<FormattedValue>();

        [JsonPropertyName("valueId")]
        public string? ValueId { get; set; }
    }

    public class CompletionsProduced
    {
        [JsonPropertyName("completions")]
        public List<CompletionItem> Completions { get; set; } = new List<CompletionItem>();
    }

    public class CompletionItem
    {
        [JsonPropertyName("displayText")]
        public string DisplayText { get; set; } = string.Empty;

        [JsonPropertyName("insertText")]
        public string InsertText { get; set; } = string.Empty;

        [JsonPropertyName("filterText")]
        public string? FilterText { get; set; }

        [JsonPropertyName("sortText")]
        public string? SortText { get; set; }

        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("documentation")]
        public string? Documentation { get; set; }
    }

    public class HoverTextProduced
    {
        [JsonPropertyName("content")]
        public List<FormattedValue> Content { get; set; } = new List<FormattedValue>();
    }

    public class SignatureHelpProduced
    {
        [JsonPropertyName("signatures")]
        public List<SignatureInformation> Signatures { get; set; } = new List<SignatureInformation>();

        [JsonPropertyName("activeSignatureIndex")]
        public int ActiveSignatureIndex { get; set; }

        [JsonPropertyName("activeParameterIndex")]
        public int ActiveParameterIndex { get; set; }
    }

    public class SignatureInformation
    {
        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("documentation")]
        public FormattedValue? Documentation { get; set; }

        [JsonPropertyName("parameters")]
        public List<ParameterInformation> Parameters { get; set; } = new List<ParameterInformation>();
    }

    public class ParameterInformation
    {
        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("documentation")]
        public FormattedValue? Documentation { get; set; }
    }

    public class DiagnosticsProduced
    {
        [JsonPropertyName("diagnostics")]
        public List<Diagnostic> Diagnostics { get; set; } = new List<Diagnostic>();

        [JsonPropertyName("formattedDiagnostics")]
        public List<FormattedValue> FormattedDiagnostics { get; set; } = new List<FormattedValue>();
    }

    public class Diagnostic
    {
        [JsonPropertyName("severity")]
        public string Severity { get; set; } = string.Empty;

        [JsonPropertyName("linePositionSpan")]
        public LinePositionSpan LinePositionSpan { get; set; } = new LinePositionSpan();

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public string? Code { get; set; }
    }

    public class LinePositionSpan
    {
        [JsonPropertyName("start")]
        public LinePosition Start { get; set; } = new LinePosition();

        [JsonPropertyName("end")]
        public LinePosition End { get; set; } = new LinePosition();
    }

    public class KernelInfoProduced
    {
        [JsonPropertyName("kernelInfo")]
        public KernelInfo? KernelInfo { get; set; }
    }

    public class KernelValueInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("typeName")]
        public string? TypeName { get; set; }

        [JsonPropertyName("formattedValue")]
        public FormattedValue? FormattedValue { get; set; }
    }

    public class ValueInfosProduced
    {
        [JsonPropertyName("valueInfos")]
        public List<KernelValueInfo> ValueInfos { get; set; } = new List<KernelValueInfo>();
    }

    public class ValueProduced
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("formattedValue")]
        public FormattedValue? FormattedValue { get; set; }
    }

    public class InputRequested
    {
        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonPropertyName("isPassword")]
        public bool IsPassword { get; set; }

        [JsonPropertyName("valueName")]
        public string? ValueName { get; set; }
    }

    /// <summary>
    /// String constants for all dotnet-interactive event type names.
    /// </summary>
    public static class KernelEventTypes
    {
        public const string KernelReady = "KernelReady";
        public const string KernelInfoProduced = "KernelInfoProduced";
        public const string CommandSucceeded = "CommandSucceeded";
        public const string CommandFailed = "CommandFailed";
        public const string CodeSubmissionReceived = "CodeSubmissionReceived";
        public const string ReturnValueProduced = "ReturnValueProduced";
        public const string StandardOutputValueProduced = "StandardOutputValueProduced";
        public const string StandardErrorValueProduced = "StandardErrorValueProduced";
        public const string DisplayedValueProduced = "DisplayedValueProduced";
        public const string DisplayedValueUpdated = "DisplayedValueUpdated";
        public const string CompletionsProduced = "CompletionsProduced";
        public const string HoverTextProduced = "HoverTextProduced";
        public const string SignatureHelpProduced = "SignatureHelpProduced";
        public const string DiagnosticsProduced = "DiagnosticsProduced";
        public const string ErrorProduced = "ErrorProduced";
        public const string ValueInfosProduced = "ValueInfosProduced";
        public const string ValueProduced = "ValueProduced";
        public const string InputRequested = "InputRequested";
    }
}

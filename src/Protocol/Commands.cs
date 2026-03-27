using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PolyglotNotebooks.Protocol
{
    public class SubmitCode
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("targetKernelName")]
        public string? TargetKernelName { get; set; }
    }

    public class RequestCompletions
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("linePosition")]
        public LinePosition LinePosition { get; set; } = new LinePosition();
    }

    public class RequestHoverText
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("linePosition")]
        public LinePosition LinePosition { get; set; } = new LinePosition();
    }

    public class RequestSignatureHelp
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("linePosition")]
        public LinePosition LinePosition { get; set; } = new LinePosition();
    }

    public class RequestDiagnostics
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("targetKernelName")]
        public string? TargetKernelName { get; set; }
    }

    public class RequestKernelInfo
    {
    }

    public class RequestValueInfos
    {
        [JsonPropertyName("mimeType")]
        public string? MimeType { get; set; }

        [JsonPropertyName("targetKernelName")]
        public string? TargetKernelName { get; set; }
    }

    public class RequestValue
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("mimeType")]
        public string? MimeType { get; set; }

        [JsonPropertyName("targetKernelName")]
        public string? TargetKernelName { get; set; }
    }

    public class SendValue
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("formattedValue")]
        public FormattedValue FormattedValue { get; set; } = new FormattedValue();

        [JsonPropertyName("targetKernelName")]
        public string? TargetKernelName { get; set; }
    }

    public class CancelCommand
    {
    }

    public class LinePosition
    {
        [JsonPropertyName("line")]
        public int Line { get; set; }

        [JsonPropertyName("character")]
        public int Character { get; set; }
    }

    public class FormattedValue
    {
        [JsonPropertyName("mimeType")]
        public string MimeType { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;

        [JsonPropertyName("suppressDisplay")]
        public bool SuppressDisplay { get; set; }
    }

    /// <summary>
    /// String constants for all dotnet-interactive command type names.
    /// </summary>
    public static class CommandTypes
    {
        public const string SubmitCode = "SubmitCode";
        public const string RequestCompletions = "RequestCompletions";
        public const string RequestHoverText = "RequestHoverText";
        public const string RequestSignatureHelp = "RequestSignatureHelp";
        public const string RequestDiagnostics = "RequestDiagnostics";
        public const string RequestKernelInfo = "RequestKernelInfo";
        public const string RequestValueInfos = "RequestValueInfos";
        public const string RequestValue = "RequestValue";
        public const string SendValue = "SendValue";
        public const string CancelCommand = "Cancel";
    }
}

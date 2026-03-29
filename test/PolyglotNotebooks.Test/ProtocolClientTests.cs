using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyglotNotebooks.Protocol;

// Suppress "Use Assert.X" style warnings in favor of explicit Assert.IsTrue patterns
#pragma warning disable MSTEST0037

namespace PolyglotNotebooks.Test
{
    /// <summary>
    /// Serializer options that match the internal ProtocolSerializerOptions used by KernelClient.
    /// </summary>
    internal static class TestSerializerOptions
    {
        public static readonly JsonSerializerOptions Default = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };
    }

    [TestClass]
    public class KernelCommandEnvelopeTests
    {
        [TestMethod]
        public void Create_WhenCalled_SetsCommandType()
        {
            var cmd = new SubmitCode { Code = "1+1" };
            var envelope = KernelCommandEnvelope.Create(CommandTypes.SubmitCode, cmd);

            Assert.AreEqual(CommandTypes.SubmitCode, envelope.CommandType);
        }

        [TestMethod]
        public void Create_WhenCalled_TokenIsBase64Guid()
        {
            var cmd = new SubmitCode { Code = "x" };
            var envelope = KernelCommandEnvelope.Create(CommandTypes.SubmitCode, cmd);

            // Base64-encoded Guid is exactly 24 characters (with trailing ==)
            Assert.IsFalse(string.IsNullOrEmpty(envelope.Token));
            var bytes = Convert.FromBase64String(envelope.Token);
            Assert.AreEqual(16, bytes.Length, "Decoded bytes should be a 16-byte GUID");
        }

        [TestMethod]
        public void Create_WhenCalledTwice_TokensAreUnique()
        {
            var cmd = new SubmitCode { Code = "x" };
            var e1 = KernelCommandEnvelope.Create(CommandTypes.SubmitCode, cmd);
            var e2 = KernelCommandEnvelope.Create(CommandTypes.SubmitCode, cmd);

            Assert.AreNotEqual(e1.Token, e2.Token);
        }

        [TestMethod]
        public void Create_WhenCalled_RoutingSlipIsEmpty()
        {
            var envelope = KernelCommandEnvelope.Create(CommandTypes.SubmitCode, new SubmitCode());

            Assert.IsNotNull(envelope.RoutingSlip);
            Assert.AreEqual(0, envelope.RoutingSlip.Count);
        }

        [TestMethod]
        public void Create_WhenCalled_CommandElementContainsCode()
        {
            var cmd = new SubmitCode { Code = "Console.WriteLine(42);" };
            var envelope = KernelCommandEnvelope.Create(CommandTypes.SubmitCode, cmd);

            // Deserialize the nested Command element to verify content
            var roundTripped = envelope.Command.Deserialize<SubmitCode>(TestSerializerOptions.Default);
            Assert.IsNotNull(roundTripped);
            Assert.AreEqual("Console.WriteLine(42);", roundTripped!.Code);
        }

        [TestMethod]
        public void JsonRoundTrip_WhenSerializedAndDeserialized_PreservesAllFields()
        {
            var cmd = new SubmitCode { Code = "1+1", TargetKernelName = "csharp" };
            var envelope = KernelCommandEnvelope.Create(CommandTypes.SubmitCode, cmd);

            var json = JsonSerializer.Serialize(envelope, TestSerializerOptions.Default);
            var restored = JsonSerializer.Deserialize<KernelCommandEnvelope>(json, TestSerializerOptions.Default);

            Assert.IsNotNull(restored);
            Assert.AreEqual(envelope.CommandType, restored!.CommandType);
            Assert.AreEqual(envelope.Token, restored.Token);
        }

        [TestMethod]
        public void JsonRoundTrip_WhenSerialized_UsedCamelCaseKeys()
        {
            var envelope = KernelCommandEnvelope.Create(CommandTypes.SubmitCode, new SubmitCode { Code = "x" });
            var json = JsonSerializer.Serialize(envelope, TestSerializerOptions.Default);

            Assert.IsTrue(json.Contains("\"commandType\""), "Should use camelCase 'commandType'");
            Assert.IsTrue(json.Contains("\"token\""), "Should use camelCase 'token'");
            Assert.IsTrue(json.Contains("\"routingSlip\""), "Should use camelCase 'routingSlip'");
        }

        [TestMethod]
        public void Deserialize_WhenMalformedJson_ThrowsJsonExceptionNotCrash()
        {
            // System.Text.Json throws JsonException on malformed input — callers (like KernelClient)
            // wrap this in try/catch so the extension never crashes on a bad line.
            bool threw = false;
            try
            {
                JsonSerializer.Deserialize<KernelCommandEnvelope>(
                    "{ not valid json !!!",
                    TestSerializerOptions.Default);
            }
            catch (JsonException)
            {
                threw = true;
            }

            Assert.IsTrue(threw, "Malformed JSON should throw JsonException (callers must catch it)");
        }
    }

    [TestClass]
    public class KernelEventEnvelopeTests
    {
        [TestMethod]
        public void Deserialize_WhenValidKernelReadyJson_PopulatesEventType()
        {
            var json = @"{
                ""eventType"": ""KernelReady"",
                ""event"": { ""kernelInfos"": [] },
                ""routingSlip"": []
            }";

            var envelope = JsonSerializer.Deserialize<KernelEventEnvelope>(json, TestSerializerOptions.Default);

            Assert.IsNotNull(envelope);
            Assert.AreEqual(KernelEventTypes.KernelReady, envelope!.EventType);
        }

        [TestMethod]
        public void Deserialize_WhenCommandSucceededJson_PopulatesCommandToken()
        {
            var json = @"{
                ""eventType"": ""CommandSucceeded"",
                ""event"": {},
                ""command"": { ""commandType"": ""SubmitCode"", ""token"": ""abc123"", ""command"": {} }
            }";

            var envelope = JsonSerializer.Deserialize<KernelEventEnvelope>(json, TestSerializerOptions.Default);

            Assert.IsNotNull(envelope);
            Assert.AreEqual(KernelEventTypes.CommandSucceeded, envelope!.EventType);
            Assert.IsNotNull(envelope.Command);
            Assert.AreEqual("abc123", envelope.Command!.Token);
        }

        [TestMethod]
        public void Deserialize_WhenCommandFailedJson_PopulatesMessage()
        {
            var json = @"{
                ""eventType"": ""CommandFailed"",
                ""event"": { ""message"": ""Compilation error"" },
                ""command"": { ""commandType"": ""SubmitCode"", ""token"": ""tok1"", ""command"": {} }
            }";

            var envelope = JsonSerializer.Deserialize<KernelEventEnvelope>(json, TestSerializerOptions.Default);
            Assert.IsNotNull(envelope);

            var failure = envelope!.Event.Deserialize<CommandFailed>(TestSerializerOptions.Default);
            Assert.IsNotNull(failure);
            Assert.AreEqual("Compilation error", failure!.Message);
        }

        [TestMethod]
        public void Deserialize_WhenNoCommandField_CommandIsNull()
        {
            var json = @"{
                ""eventType"": ""KernelReady"",
                ""event"": { ""kernelInfos"": [] }
            }";

            var envelope = JsonSerializer.Deserialize<KernelEventEnvelope>(json, TestSerializerOptions.Default);

            Assert.IsNotNull(envelope);
            Assert.IsNull(envelope!.Command);
        }

        [TestMethod]
        public void Deserialize_WhenEmptyString_ThrowsJsonException()
        {
            bool threw = false;
            try
            {
                JsonSerializer.Deserialize<KernelEventEnvelope>("", TestSerializerOptions.Default);
            }
            catch (JsonException)
            {
                threw = true;
            }
            Assert.IsTrue(threw, "Expected JsonException for empty input");
        }

        [TestMethod]
        public void Deserialize_WhenCompletionsProducedJson_DeserializesCompletions()
        {
            var json = @"{
                ""eventType"": ""CompletionsProduced"",
                ""event"": {
                    ""completions"": [
                        { ""displayText"": ""Console"", ""insertText"": ""Console"", ""kind"": ""Class"" }
                    ]
                },
                ""command"": { ""commandType"": ""RequestCompletions"", ""token"": ""t1"", ""command"": {} }
            }";

            var envelope = JsonSerializer.Deserialize<KernelEventEnvelope>(json, TestSerializerOptions.Default);
            Assert.IsNotNull(envelope);

            var completions = envelope!.Event.Deserialize<CompletionsProduced>(TestSerializerOptions.Default);
            Assert.IsNotNull(completions);
            Assert.AreEqual(1, completions!.Completions.Count);
            Assert.AreEqual("Console", completions.Completions[0].DisplayText);
        }
    }

    [TestClass]
    public class CommandSerializationTests
    {
        [TestMethod]
        public void SubmitCode_WhenSerialized_ContainsCodeField()
        {
            var cmd = new SubmitCode { Code = "var x = 42;" };
            var json = JsonSerializer.Serialize(cmd, TestSerializerOptions.Default);

            Assert.IsTrue(json.Contains("\"code\""));
            Assert.IsTrue(json.Contains("var x = 42;"));
        }

        [TestMethod]
        public void SubmitCode_WhenTargetKernelNameIsNull_OmittedFromJson()
        {
            var cmd = new SubmitCode { Code = "x", TargetKernelName = null };
            var json = JsonSerializer.Serialize(cmd, TestSerializerOptions.Default);

            Assert.IsFalse(json.Contains("targetKernelName"),
                "Null TargetKernelName should be omitted by WhenWritingNull policy");
        }

        [TestMethod]
        public void SubmitCode_WhenTargetKernelNameSet_IncludedInJson()
        {
            var cmd = new SubmitCode { Code = "x", TargetKernelName = "fsharp" };
            var json = JsonSerializer.Serialize(cmd, TestSerializerOptions.Default);

            Assert.IsTrue(json.Contains("\"targetKernelName\""));
            Assert.IsTrue(json.Contains("fsharp"));
        }

        [TestMethod]
        public void RequestCompletions_WhenSerialized_ContainsLinePosition()
        {
            var cmd = new RequestCompletions
            {
                Code = "Console.",
                LinePosition = new LinePosition { Line = 0, Character = 8 }
            };

            var json = JsonSerializer.Serialize(cmd, TestSerializerOptions.Default);

            Assert.IsTrue(json.Contains("\"linePosition\""));
            Assert.IsTrue(json.Contains("\"line\""));
            Assert.IsTrue(json.Contains("\"character\""));
        }

        [TestMethod]
        public void RequestCompletions_RoundTrip_PreservesPosition()
        {
            var cmd = new RequestCompletions
            {
                Code = "System.",
                LinePosition = new LinePosition { Line = 3, Character = 7 }
            };

            var json = JsonSerializer.Serialize(cmd, TestSerializerOptions.Default);
            var restored = JsonSerializer.Deserialize<RequestCompletions>(json, TestSerializerOptions.Default);

            Assert.IsNotNull(restored);
            Assert.AreEqual("System.", restored!.Code);
            Assert.AreEqual(3, restored.LinePosition.Line);
            Assert.AreEqual(7, restored.LinePosition.Character);
        }

        [TestMethod]
        public void CommandTypes_Constants_MatchExpectedWireNames()
        {
#pragma warning disable MSTEST0032 // Intentionally verifying const wire-name values haven't changed
            Assert.AreEqual("SubmitCode", (object)CommandTypes.SubmitCode);
            Assert.AreEqual("RequestCompletions", (object)CommandTypes.RequestCompletions);
            Assert.AreEqual("RequestHoverText", (object)CommandTypes.RequestHoverText);
            Assert.AreEqual("RequestDiagnostics", (object)CommandTypes.RequestDiagnostics);
            Assert.AreEqual("RequestKernelInfo", (object)CommandTypes.RequestKernelInfo);
            Assert.AreEqual("Cancel", (object)CommandTypes.CancelCommand);
#pragma warning restore MSTEST0032
        }

        [TestMethod]
        public void SendValue_WhenSerialized_ContainsFormattedValue()
        {
            var cmd = new SendValue
            {
                Name = "myVar",
                FormattedValue = new FormattedValue { MimeType = "text/plain", Value = "42" }
            };

            var json = JsonSerializer.Serialize(cmd, TestSerializerOptions.Default);

            Assert.IsTrue(json.Contains("\"name\""));
            Assert.IsTrue(json.Contains("myVar"));
            Assert.IsTrue(json.Contains("\"formattedValue\""));
            Assert.IsTrue(json.Contains("text/plain"));
        }
    }

    [TestClass]
    public class EventTypeSerializationTests
    {
        [TestMethod]
        public void KernelReady_WhenDeserialized_PopulatesKernelInfos()
        {
            var json = @"{
                ""kernelInfos"": [
                    {
                        ""localName"": ""csharp"",
                        ""displayName"": ""C#"",
                        ""languageName"": ""C#"",
                        ""supportedKernelCommands"": [{ ""name"": ""SubmitCode"" }]
                    }
                ]
            }";

            var ready = JsonSerializer.Deserialize<KernelReady>(json, TestSerializerOptions.Default);

            Assert.IsNotNull(ready);
            Assert.AreEqual(1, ready!.KernelInfos.Count);
            Assert.AreEqual("csharp", ready.KernelInfos[0].LocalName);
            Assert.AreEqual("C#", ready.KernelInfos[0].DisplayName);
            Assert.AreEqual("C#", ready.KernelInfos[0].LanguageName);
            Assert.AreEqual(1, ready.KernelInfos[0].SupportedKernelCommands.Count);
            Assert.AreEqual("SubmitCode", ready.KernelInfos[0].SupportedKernelCommands[0].Name);
        }

        [TestMethod]
        public void CommandSucceeded_WhenDeserialized_ExecutionOrderOptional()
        {
            var jsonWithOrder = @"{ ""executionOrder"": 5 }";
            var jsonWithoutOrder = @"{}";

            var withOrder = JsonSerializer.Deserialize<CommandSucceeded>(jsonWithOrder, TestSerializerOptions.Default);
            var withoutOrder = JsonSerializer.Deserialize<CommandSucceeded>(jsonWithoutOrder, TestSerializerOptions.Default);

            Assert.IsNotNull(withOrder);
            Assert.AreEqual(5, withOrder!.ExecutionOrder);
            Assert.IsNotNull(withoutOrder);
            Assert.IsNull(withoutOrder!.ExecutionOrder);
        }

        [TestMethod]
        public void CommandFailed_WhenDeserialized_PopulatesMessage()
        {
            var json = @"{ ""message"": ""(1,1): error CS0103: The name 'x' does not exist"" }";
            var failure = JsonSerializer.Deserialize<CommandFailed>(json, TestSerializerOptions.Default);

            Assert.IsNotNull(failure);
            Assert.IsTrue(failure!.Message.Contains("CS0103"));
        }

        [TestMethod]
        public void CompletionsProduced_WhenDeserialized_MultipleItems()
        {
            var json = @"{
                ""completions"": [
                    { ""displayText"": ""Console"", ""insertText"": ""Console"" },
                    { ""displayText"": ""Convert"", ""insertText"": ""Convert"" }
                ]
            }";

            var result = JsonSerializer.Deserialize<CompletionsProduced>(json, TestSerializerOptions.Default);

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result!.Completions.Count);
            Assert.AreEqual("Console", result.Completions[0].DisplayText);
            Assert.AreEqual("Convert", result.Completions[1].DisplayText);
        }

        [TestMethod]
        public void DiagnosticsProduced_WhenDeserialized_PopulatesDiagnostics()
        {
            var json = @"{
                ""diagnostics"": [
                    {
                        ""severity"": ""error"",
                        ""message"": ""missing semicolon"",
                        ""code"": ""CS1002"",
                        ""linePositionSpan"": {
                            ""start"": { ""line"": 0, ""character"": 5 },
                            ""end"": { ""line"": 0, ""character"": 6 }
                        }
                    }
                ],
                ""formattedDiagnostics"": []
            }";

            var result = JsonSerializer.Deserialize<DiagnosticsProduced>(json, TestSerializerOptions.Default);

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result!.Diagnostics.Count);
            Assert.AreEqual("error", result.Diagnostics[0].Severity);
            Assert.AreEqual("CS1002", result.Diagnostics[0].Code);
            Assert.AreEqual(0, result.Diagnostics[0].LinePositionSpan.Start.Line);
            Assert.AreEqual(5, result.Diagnostics[0].LinePositionSpan.Start.Character);
        }

        [TestMethod]
        public void KernelEventTypes_Constants_MatchExpectedWireNames()
        {
#pragma warning disable MSTEST0032 // Intentionally verifying const wire-name values haven't changed
            Assert.AreEqual("KernelReady", (object)KernelEventTypes.KernelReady);
            Assert.AreEqual("CommandSucceeded", (object)KernelEventTypes.CommandSucceeded);
            Assert.AreEqual("CommandFailed", (object)KernelEventTypes.CommandFailed);
            Assert.AreEqual("CompletionsProduced", (object)KernelEventTypes.CompletionsProduced);
            Assert.AreEqual("StandardOutputValueProduced", (object)KernelEventTypes.StandardOutputValueProduced);
            Assert.AreEqual("StandardErrorValueProduced", (object)KernelEventTypes.StandardErrorValueProduced);
#pragma warning restore MSTEST0032
        }

        [TestMethod]
        public void StandardOutputValueProduced_WhenDeserialized_PopulatesFormattedValues()
        {
            var json = @"{
                ""formattedValues"": [
                    { ""mimeType"": ""text/plain"", ""value"": ""Hello, World!"" }
                ]
            }";

            var result = JsonSerializer.Deserialize<StandardOutputValueProduced>(json, TestSerializerOptions.Default);

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result!.FormattedValues.Count);
            Assert.AreEqual("text/plain", result.FormattedValues[0].MimeType);
            Assert.AreEqual("Hello, World!", result.FormattedValues[0].Value);
        }

        [TestMethod]
        public void ReturnValueProduced_WhenDeserialized_PopulatesValueId()
        {
            var json = @"{
                ""formattedValues"": [{ ""mimeType"": ""text/html"", ""value"": ""<b>42</b>"" }],
                ""valueId"": ""result-1""
            }";

            var result = JsonSerializer.Deserialize<ReturnValueProduced>(json, TestSerializerOptions.Default);

            Assert.IsNotNull(result);
            Assert.AreEqual("result-1", result!.ValueId);
            Assert.AreEqual("text/html", result.FormattedValues[0].MimeType);
        }
    }

    [TestClass]
    public class MalformedJsonHandlingTests
    {
        [TestMethod]
        public void Deserialize_KernelEventEnvelope_WhenNullJson_ReturnsNull()
        {
            var result = JsonSerializer.Deserialize<KernelEventEnvelope>("null", TestSerializerOptions.Default);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void Deserialize_KernelEventEnvelope_WhenEmptyObject_ReturnsDefaultObject()
        {
            var result = JsonSerializer.Deserialize<KernelEventEnvelope>("{}", TestSerializerOptions.Default);
            Assert.IsNotNull(result);
            Assert.AreEqual(string.Empty, result!.EventType);
        }

        [TestMethod]
        public void Deserialize_KernelEventEnvelope_WhenUnknownFields_IgnoresGracefully()
        {
            var json = @"{
                ""eventType"": ""KernelReady"",
                ""event"": {},
                ""unknownField"": ""some value"",
                ""anotherField"": 42
            }";

            // Should not throw
            var result = JsonSerializer.Deserialize<KernelEventEnvelope>(json, TestSerializerOptions.Default);
            Assert.IsNotNull(result);
            Assert.AreEqual("KernelReady", result!.EventType);
        }

        [TestMethod]
        public void Deserialize_KernelEventEnvelope_WhenMalformedJson_ThrowsJsonException()
        {
            try
            {
                JsonSerializer.Deserialize<KernelEventEnvelope>("{bad json", TestSerializerOptions.Default);
                Assert.Fail("Expected JsonException was not thrown");
            }
            catch (JsonException) { }
        }
    }
}

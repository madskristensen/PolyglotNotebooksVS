using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PolyglotNotebooks.Protocol
{
    public class KernelCommandEnvelope
    {
        [JsonPropertyName("commandType")]
        public string CommandType { get; set; } = string.Empty;

        [JsonPropertyName("command")]
        public JsonElement Command { get; set; }

        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;

        [JsonPropertyName("routingSlip")]
        public List<string> RoutingSlip { get; set; } = new List<string>();

        /// <summary>
        /// Creates a new envelope with an auto-generated Base64-encoded GUID token,
        /// matching the dotnet-interactive wire format for root commands.
        /// </summary>
        public static KernelCommandEnvelope Create<T>(string commandType, T command)
        {
            return new KernelCommandEnvelope
            {
                CommandType = commandType,
                Command = JsonSerializer.SerializeToElement(command, ProtocolSerializerOptions.Default),
                Token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
                RoutingSlip = new List<string>()
            };
        }
    }
}

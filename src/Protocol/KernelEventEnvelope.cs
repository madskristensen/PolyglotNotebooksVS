using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PolyglotNotebooks.Protocol
{
    public class KernelEventEnvelope
    {
        [JsonPropertyName("eventType")]
        public string EventType { get; set; } = string.Empty;

        [JsonPropertyName("event")]
        public JsonElement Event { get; set; }

        [JsonPropertyName("command")]
        public KernelCommandEnvelope? Command { get; set; }

        [JsonPropertyName("routingSlip")]
        public List<string> RoutingSlip { get; set; } = new List<string>();
    }
}

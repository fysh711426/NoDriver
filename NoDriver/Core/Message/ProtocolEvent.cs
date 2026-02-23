using System.Text.Json.Nodes;

namespace NoDriver.Core.Message
{
    public class ProtocolEvent
    {
        public string Method { get; set; } = "";
        public JsonObject Params { get; set; } = new();
        public string? SessionId { get; set; } = null;
    }
}

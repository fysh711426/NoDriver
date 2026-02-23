using System.Text.Json.Nodes;

namespace NoDriver.Core.Message
{
    public class ProtocolResponse
    {
        public int Id { get; set; } = 0;
        public JsonObject? Result { get; set; } = null;
        public ProtocolErrorInfo? Error { get; set; } = null;
        public string? SessionId { get; set; } = null;
    }
}

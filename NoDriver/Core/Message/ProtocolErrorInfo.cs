using System.Text.Json.Nodes;

namespace NoDriver.Core.Message
{
    public class ProtocolErrorInfo
    {
        public int Code { get; set; } = 0;
        public JsonObject? Result { get; set; } = null;
        public string? Message { get; set; } = null;
        public string? Data { get; set; } = null;
    }
}

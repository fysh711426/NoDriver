using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace NoDriver.Core.Message
{
    public class ProtocolEvent
    {
        public string Method { get; set; } = "";
        public JsonObject Params { get; set; } = new();
        public string? SessionId { get; set; } = null;
    }
}

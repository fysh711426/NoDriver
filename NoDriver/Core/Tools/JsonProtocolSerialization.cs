using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoDriver.Core.Tools
{
    internal static class JsonProtocolSerialization
    {
        public static readonly JsonSerializerOptions Settings = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            MaxDepth = 1024
        };

        public static readonly JsonDocumentOptions DocSettings = new JsonDocumentOptions
        {
            MaxDepth = 1024
        };
    }
}

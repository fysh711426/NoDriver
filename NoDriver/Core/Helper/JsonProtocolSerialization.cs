using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoDriver.Core.Helper
{
    public static class JsonProtocolSerialization
    {
        public static readonly JsonSerializerOptions Settings = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }
}

using System.Text.Json.Serialization;

namespace Generator.Models
{
    public class Property
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; } = null;
        public TypeKind? Type { get; set; } = null;

        [JsonPropertyName("$ref")]
        public string? Ref { get; set; } = null;
        public bool? Optional { get; set; } = null;
        public bool? Deprecated { get; set; } = null;
        public bool? Experimental { get; set; } = null;
        public Items? Items { get; set; } = null;
        public List<string> Enum { get; set; } = new();
    }
}

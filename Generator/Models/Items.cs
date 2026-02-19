using System.Text.Json.Serialization;

namespace Generator.Models
{
    public class Items
    {
        public TypeKind Type { get; set; }

        [JsonPropertyName("$ref")]
        public string? Ref { get; set; } = null;
    }
}

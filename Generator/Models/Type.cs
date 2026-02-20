using System.Text.Json.Serialization;

namespace Generator.Models
{
    public class Type
    {
        public string Id { get; set; } = "";
        public string? Description { get; set; } = null;

        [JsonPropertyName("type")]
        public TypeKind? Kind { get; set; } = null;
        public Items? Items { get; set; } = null;
        public List<string> Enum { get; set; } = new();
        public List<Property> Properties { get; set; } = new();
    }
}

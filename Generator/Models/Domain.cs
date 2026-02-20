using System.Text.Json.Serialization;

namespace Generator.Models
{
    public class Domain
    {
        [JsonPropertyName("domain")]
        public string Name { get; set; } = "";
        public string? Description { get; set; } = null;
        public bool? Deprecated { get; set; } = null;
        public bool? Experimental { get; set; } = null;
        public List<string> Dependencies { get; set; } = new();
        public List<Type> Types { get; set; } = new();
        public List<Command> Commands { get; set; } = new();
        public List<Event> Events { get; set; } = new();
    }
}

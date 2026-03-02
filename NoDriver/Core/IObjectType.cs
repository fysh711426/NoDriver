using System.Text.Json.Nodes;

namespace NoDriver.Core
{
    public interface IObjectType
    {
        public IReadOnlyDictionary<string, JsonNode?> Properties { get; }
    }
}

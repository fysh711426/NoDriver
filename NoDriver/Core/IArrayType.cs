using System.Text.Json.Nodes;

namespace NoDriver.Core
{
    public interface IArrayType
    {
        public IReadOnlyCollection<JsonNode?> Items { get; }
    }
}

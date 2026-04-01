using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace NoDriver.Core
{
    public interface IObjectType
    {
        IReadOnlyDictionary<string, JsonNode?> Properties { get; }
    }
}

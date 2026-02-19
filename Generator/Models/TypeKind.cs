using System.Text.Json.Serialization;

namespace Generator.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TypeKind
    {
        Any,
        Array,
        Boolean,
        Integer,
        Number,
        Object,
        String
    }
}

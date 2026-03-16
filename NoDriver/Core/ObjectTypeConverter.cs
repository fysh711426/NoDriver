using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NoDriver.Core
{
    public class ObjectTypeConverter : JsonConverter<IObjectType?>
    {
        public override bool HandleNull => true;

        public override bool CanConvert(Type objectType)
        {
            return typeof(IObjectType).IsAssignableFrom(objectType);
        }

        public override IObjectType? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return Activator.CreateInstance(typeToConvert, new Dictionary<string, JsonNode?>()) as IObjectType;

            var properties = JsonSerializer.Deserialize<IReadOnlyDictionary<string, JsonNode?>>(ref reader, options);
            return Activator.CreateInstance(typeToConvert, properties) as IObjectType;
        }

        public override void Write(Utf8JsonWriter writer, IObjectType? value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value?.Properties ?? Enumerable.Empty<KeyValuePair<string, JsonNode?>>(), options);
        }
    }
}

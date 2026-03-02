using NoDriver.Core.Tools;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoDriver.Core
{
    public class PrimitiveTypeConverter : JsonConverter<object>
    {
        private static Type? GetValueType(Type objectType) =>
            objectType
                .BaseTypesAndSelf()
                .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(PrimitiveType<>))
                .Select(t => t.GetGenericArguments()[0])
                .FirstOrDefault();

        public override bool CanConvert(Type objectType)
        {
            return GetValueType(objectType) != null;
        }

        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var valueType = GetValueType(typeToConvert);

            if (reader.TokenType == JsonTokenType.Null || valueType is null)
                return Activator.CreateInstance(typeToConvert, null);

            var value = JsonSerializer.Deserialize(ref reader, valueType, options);
            return Activator.CreateInstance(typeToConvert, value);
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, ((IPrimitiveType?)value)?.RawValue);
        }
    }
}

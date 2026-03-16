using NoDriver.Core.Tools;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoDriver.Core
{
    public class PrimitiveTypeConverter : JsonConverter<IPrimitiveType?>
    {
        private static readonly ConcurrentDictionary<Type, Lazy<Type?>> _valueTypeCache = new();

        private static Type? GetValueType(Type objectType)
        {
            return _valueTypeCache.GetOrAdd(objectType, type => new Lazy<Type?>(() =>
            {
                return type
                    .BaseTypesAndSelf()
                    .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(PrimitiveType<>))
                    .Select(t => t.GetGenericArguments()[0])
                    .FirstOrDefault();
            })).Value;
        }

        public override bool HandleNull => true;

        public override bool CanConvert(Type objectType)
        {
            return typeof(IPrimitiveType).IsAssignableFrom(objectType);
        }

        public override IPrimitiveType? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var valueType = GetValueType(typeToConvert);

            if (reader.TokenType == JsonTokenType.Null || valueType is null)
                return Activator.CreateInstance(typeToConvert, null) as IPrimitiveType;

            var value = JsonSerializer.Deserialize(ref reader, valueType, options);
            return Activator.CreateInstance(typeToConvert, value) as IPrimitiveType;
        }

        public override void Write(Utf8JsonWriter writer, IPrimitiveType? value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value?.RawValue, options);
        }
    }
}

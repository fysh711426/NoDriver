using NoDriver.Core.Tools;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoDriver.Core
{
    public class ArrayTypeConverter : JsonConverter<IArrayType?>
    {
        private static readonly ConcurrentDictionary<Type, Lazy<Type?>> _valueTypeCache = new();

        private static Type? GetValueType(Type objectType)
        {
            return _valueTypeCache.GetOrAdd(objectType, type => new Lazy<Type?>(() =>
            {
                return type
                    .BaseTypesAndSelf()
                    .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ArrayType<>))
                    .Select(t => t.GetGenericArguments()[0])
                    .FirstOrDefault();
            })).Value;
        }

        public override bool HandleNull => true;

        public override bool CanConvert(Type objectType)
        {
            return typeof(IArrayType).IsAssignableFrom(objectType);
        }

        public override IArrayType? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var valueType = GetValueType(typeToConvert);

            if (valueType == null)
                return Activator.CreateInstance(typeToConvert, null) as IArrayType;

            if (reader.TokenType == JsonTokenType.Null)
            {
                var emptyList = Activator.CreateInstance(
                    typeof(List<>).MakeGenericType(valueType));
                return Activator.CreateInstance(typeToConvert, emptyList) as IArrayType;
            }

            var items = JsonSerializer.Deserialize(ref reader,
                typeof(IReadOnlyList<>).MakeGenericType(valueType), options);
            return Activator.CreateInstance(typeToConvert, items) as IArrayType;
        }

        public override void Write(Utf8JsonWriter writer, IArrayType? value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value?.RawItems ?? Enumerable.Empty<object>(), options);
        }
    }
}

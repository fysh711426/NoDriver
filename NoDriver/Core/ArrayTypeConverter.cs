using NoDriver.Core.Tools;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoDriver.Core
{
    public class ArrayTypeConverter : JsonConverter<IArrayType?>
    {
        private static Type? GetValueType(Type objectType) =>
            objectType
                .BaseTypesAndSelf()
                .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ArrayType<>))
                .Select(t => t.GetGenericArguments()[0])
                .FirstOrDefault();

        public override bool CanConvert(Type objectType)
        {
            return typeof(IArrayType).IsAssignableFrom(objectType);
        }

        public override IArrayType? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var valueType = GetValueType(typeToConvert);

            if (reader.TokenType == JsonTokenType.Null || valueType is null)
                return null;

            var listType = typeof(IReadOnlyList<>).MakeGenericType(valueType);
            var items = JsonSerializer.Deserialize(ref reader, listType, options);

            return Activator.CreateInstance(typeToConvert, items) as IArrayType;
        }

        public override void Write(Utf8JsonWriter writer, IArrayType? value, JsonSerializerOptions options)
        {
            if (value?.RawItems == null)
            {
                //writer.WriteNullValue();
                JsonSerializer.Serialize(writer, Enumerable.Empty<object>());
                return;
            }
            JsonSerializer.Serialize(writer, value.RawItems, value.RawItems.GetType());
        }
    }
}

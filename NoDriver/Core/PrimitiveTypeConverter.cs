using NoDriver.Core.Tools;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
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

        private static readonly ConcurrentDictionary<Type, Lazy<Func<object?, object>>> _constructorCache = new();

        private static Func<object?, object> GetConstructor(Type objectType)
        {
            return _constructorCache.GetOrAdd(objectType, type => new Lazy<Func<object?, object>>(() =>
            {
                var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).First();

                var paramType = ctor.GetParameters().First().ParameterType;
                var inputParam = Expression.Parameter(typeof(object), "Value");
                //var castParam = Expression.Convert(inputParam, paramType);

                // inputParam == null ? default(TParam) : (TParam)Value
                var conditionParam = Expression.Condition(
                    Expression.Equal(inputParam, Expression.Constant(null)),
                    paramType == typeof(string) ?
                        Expression.Constant("") : Expression.Default(paramType),
                    Expression.Convert(inputParam, paramType)
                );

                var newExpr = Expression.New(ctor, conditionParam);
                var boxExpr = Expression.Convert(newExpr, typeof(object));

                // lambda: (object Value) => (object)new T((TParam)Value)
                var lambda = Expression.Lambda<Func<object?, object>>(boxExpr, inputParam);
                return lambda.Compile();
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
            if (valueType == null)
                throw new JsonException($"The type '{typeToConvert.Name}' is not a valid PrimitiveType implementation.");

            var constructor = GetConstructor(typeToConvert);

            if (reader.TokenType == JsonTokenType.Null)
                return constructor(null) as IPrimitiveType;

            var value = JsonSerializer.Deserialize(ref reader, valueType, options);
            return constructor(value) as IPrimitiveType;
        }

        public override void Write(Utf8JsonWriter writer, IPrimitiveType? value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value?.RawValue, options);
        }
    }
}

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NoDriver.Core
{
    public class ObjectTypeConverter : JsonConverter<IObjectType?>
    {
        private static readonly ConcurrentDictionary<Type, Lazy<Func<IReadOnlyDictionary<string, JsonNode?>?, object>>> _constructorCache = new();

        private static Func<IReadOnlyDictionary<string, JsonNode?>?, object> GetConstructor(Type objectType)
        {
            return _constructorCache.GetOrAdd(objectType, type => new Lazy<Func<IReadOnlyDictionary<string, JsonNode?>?, object>>(() =>
            {
                var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).First();

                var paramType = ctor.GetParameters().First().ParameterType;
                var inputParam = Expression.Parameter(paramType, "Properties");

                // inputParam == null ? new Dictionary<string, JsonNode>() : Properties
                var conditionParam = Expression.Condition(
                    Expression.Equal(inputParam, Expression.Constant(null)),
                    Expression.Convert(
                        Expression.New(typeof(Dictionary<string, JsonNode>)), paramType),
                    inputParam
                );

                var newExpr = Expression.New(ctor, conditionParam);
                var boxExpr = Expression.Convert(newExpr, typeof(object));

                // TParam: IReadOnlyDictionary<string, JsonNode?>
                // lambda: (TParam Properties) => (object)new T(Properties)
                var lambda = Expression.Lambda<Func<IReadOnlyDictionary<string, JsonNode?>?, object>>(boxExpr, inputParam);
                return lambda.Compile();
            })).Value;
        }

        public override bool HandleNull => true;

        public override bool CanConvert(Type objectType)
        {
            return typeof(IObjectType).IsAssignableFrom(objectType);
        }

        public override IObjectType? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var constructor = GetConstructor(typeToConvert);

            if (reader.TokenType == JsonTokenType.Null)
                return constructor(null) as IObjectType;

            var properties = JsonSerializer.Deserialize<IReadOnlyDictionary<string, JsonNode?>>(ref reader, options);
            return constructor(properties) as IObjectType;
        }

        public override void Write(Utf8JsonWriter writer, IObjectType? value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value?.Properties ?? Enumerable.Empty<KeyValuePair<string, JsonNode?>>(), options);
        }
    }
}

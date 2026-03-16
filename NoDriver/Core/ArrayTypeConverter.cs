using NoDriver.Core.Tools;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
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

        private static readonly ConcurrentDictionary<Type, Lazy<Type>> _listTypeCache = new();

        private static Type GetListType(Type valueType)
        {
            return _listTypeCache.GetOrAdd(valueType, type => new Lazy<Type>(() =>
                typeof(IReadOnlyList<>).MakeGenericType(type))).Value;
        }

        private static readonly ConcurrentDictionary<Type, Lazy<Func<object?, object>>> _constructorCache = new();

        private static Func<object?, object> GetConstructor(Type objectType)
        {
            return _constructorCache.GetOrAdd(objectType, type => new Lazy<Func<object?, object>>(() =>
            {
                var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).First();

                var paramType = ctor.GetParameters().First().ParameterType;
                var inputParam = Expression.Parameter(typeof(object), "Items");

                // inputParam == null ? new List<TValue>() : (TParam)Items
                var conditionParam = Expression.Condition(
                    Expression.Equal(inputParam, Expression.Constant(null)),
                    Expression.Convert(
                        Expression.New(typeof(List<>).MakeGenericType(
                            paramType.GetGenericArguments().First())), paramType),
                    Expression.Convert(inputParam, paramType)
                );

                var newExpr = Expression.New(ctor, conditionParam);
                var boxExpr = Expression.Convert(newExpr, typeof(object));

                // lambda: (object Items) => (object)new T((TParam)Items)
                var lambda = Expression.Lambda<Func<object?, object>>(boxExpr, inputParam);
                return lambda.Compile();
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
                throw new JsonException($"The type '{typeToConvert.Name}' is not a valid ArrayType implementation.");

            var listType = GetListType(valueType);
            var constructor = GetConstructor(typeToConvert);

            if (reader.TokenType == JsonTokenType.Null)
                return constructor(null) as IArrayType;

            var items = JsonSerializer.Deserialize(ref reader, listType, options);
            return constructor(items) as IArrayType;
        }

        public override void Write(Utf8JsonWriter writer, IArrayType? value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value?.RawItems ?? Enumerable.Empty<object>(), options);
        }
    }
}

using System.Collections.Concurrent;
using System.Reflection;

namespace NoDriver.Core
{
    public interface IPrimitiveType : IType
    {
        object? RawValue { get; }
    }

    public abstract record PrimitiveType<TValue>(TValue Value) : IPrimitiveType
    {
        object? IPrimitiveType.RawValue => Value;

        private static readonly ConcurrentDictionary<Type, Lazy<object>> _enumCache = new();

        public static IReadOnlyList<TSub> GetEnums<TSub>() where TSub : PrimitiveType<TValue>
        {
            return (IReadOnlyList<TSub>)_enumCache.GetOrAdd(typeof(TSub), type => new Lazy<object>(() =>
            {
                return type
                    .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                    .Where(f => type.IsAssignableFrom(f.FieldType))
                    .Select(f => (TSub)f.GetValue(null)!)
                    .ToList().AsReadOnly();
            })).Value;
        }
    }
}

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

        public static List<TSub> GetEnums<TSub>() where TSub : PrimitiveType<TValue>
        {
            return typeof(TSub)
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(f => typeof(TSub).IsAssignableFrom(f.FieldType))
                .Select(f => (TSub)f.GetValue(null)!)
                .ToList();
        }
    }
}

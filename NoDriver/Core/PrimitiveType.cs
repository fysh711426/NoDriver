namespace NoDriver.Core
{
    public interface IPrimitiveType : IType
    {
        object? RawValue { get; }
    }

    public abstract record PrimitiveType<TValue>(TValue Value) : IPrimitiveType
    {
        object? IPrimitiveType.RawValue => Value;

        public static bool operator ==(PrimitiveType<TValue>? left, TValue? right) =>
            left is null ? right is null : EqualityComparer<TValue>.Default.Equals(left.Value, right);
        public static bool operator !=(PrimitiveType<TValue>? left, TValue? right) => !(left == right);
        public static bool operator ==(TValue? left, PrimitiveType<TValue>? right) => right == left;
        public static bool operator !=(TValue? left, PrimitiveType<TValue>? right) => !(left == right);
    }
}

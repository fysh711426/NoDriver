namespace NoDriver.Core
{
    public interface IArrayType
    {
        object? RawItems { get; }
    }

    public abstract record ArrayType<TValue>(IReadOnlyList<TValue> Items) : IArrayType
    {
        object? IArrayType.RawItems => Items;
    }
}

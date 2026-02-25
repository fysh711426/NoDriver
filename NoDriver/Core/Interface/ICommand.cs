namespace NoDriver.Core.Interface
{
    public interface ICommand;

    public interface ICommand<TResponse> : ICommand where TResponse : IType;
}

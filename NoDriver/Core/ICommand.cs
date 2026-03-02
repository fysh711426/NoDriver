namespace NoDriver.Core
{
    public interface ICommand;

    public interface ICommand<TResponse> : ICommand where TResponse : IType;
}

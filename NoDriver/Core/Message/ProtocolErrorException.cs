namespace NoDriver.Core.Message
{
    public class ProtocolErrorException : Exception
    {
        public ProtocolErrorInfo Info { get; }

        public ProtocolErrorException(ProtocolErrorInfo info)
            : base($"{info.Message ?? ""} [code: {info.Code}]")
        {
            Info = info;
        }
    }
}

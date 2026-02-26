namespace NoDriver.Core.Message
{
    public class ProtocolRequest<TRawParams>
    {
        public int Id { get; set; } = 0;
        public string Method { get; set; } = "";
        public TRawParams Params { get; set; } = default!;
        public string? SessionId { get; set; } = null;
    }
}

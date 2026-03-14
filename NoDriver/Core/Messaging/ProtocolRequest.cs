namespace NoDriver.Core.Messaging
{
    public class ProtocolRequest
    {
        public int Id { get; set; } = 0;
        public string Method { get; set; } = "";
        public object Params { get; set; } = new();
        public string? SessionId { get; set; } = null;
    }
}

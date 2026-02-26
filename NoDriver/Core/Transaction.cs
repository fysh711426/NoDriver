using NoDriver.Core.Message;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NoDriver.Core
{
    public class Transaction<TRawParams>
    {
        protected readonly TaskCompletionSource<JsonObject> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Id { get; }
        public string Method { get; }
        public TRawParams Params { get; }

        public Task<JsonObject> Task => _tcs.Task;

        public Transaction(int id, string method, TRawParams @params)
        {
            Id = id;
            Method = method;
            Params = @params;
        }

        public string Message => JsonSerializer.Serialize(new
        {
            Method,
            Params,
            Id
        });

        public bool HasException => _tcs.Task.IsFaulted;

        public virtual void ProcessResponse(ProtocolResponse response)
        {
            if (response.Error != null)
                _tcs.TrySetException(new ProtocolErrorException(response.Error));
            else if (response.Result != null)
                _tcs.TrySetResult(response.Result);
        }

        public virtual void Cancel(Exception ex)
        {
            _tcs.TrySetException(ex);
        }

        public override string ToString()
        {
            var isDone = _tcs.Task.IsCompleted;
            var success = isDone && HasException ? false : true;

            var status = "";
            if (isDone)
                status = "finished";
            else
                status = "pending";

            return $"<{typeof(TRawParams).Name}\n\t" +
                   $"Method: {Method}\n\t" +
                   $"Status: {status}\n\t" +
                   $"Success: {success}>";
        }
    }
}

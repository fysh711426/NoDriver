namespace NoDriver.Core.Helper
{
    internal static class TaskExtension
    {
        public static async Task WhenWaitAsync(this Task task, CancellationToken token)
        {
            using (var delayCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                try
                {
                    var completedTask = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, delayCts.Token));
                    if (completedTask != task)
                        await completedTask;
                }
                finally
                {
                    delayCts.Cancel();
                }
            }
        }
    }
}

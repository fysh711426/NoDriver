using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NoDriver.Core.Tools
{
    internal static class CommandDispatcher
    {
        private static readonly ConcurrentDictionary<Type, Lazy<Func<object, object, bool, CancellationToken, Task>>> _cache = new();

        public static Task DispatchAsync(object client, object command, bool isUpdate, CancellationToken token)
        {
            var commandType = command.GetType();

            var lazyInvoker = _cache.GetOrAdd(commandType, _ => new Lazy<Func<object, object, bool, CancellationToken, Task>>(() =>
            {
                // 1. 找出 command 實作的 ICommand<TResponse> 以取得 TResponse 型別
                var commandInterface = commandType.GetInterfaces()
                    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));

                if (commandInterface == null)
                    throw new ArgumentException($"Type '{commandType.FullName}' does not implement ICommand<TResponse>.");

                var responseType = commandInterface.GetGenericArguments()[0];
                var clientType = client.GetType();

                // 2. 找到目標物件上的 SendAsync<TResponse> 泛型方法
                var methodInfo = clientType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(it => it.Name == "SendAsync" && it.IsGenericMethodDefinition)?
                    .MakeGenericMethod(responseType);

                if (methodInfo == null)
                    throw new MissingMethodException($"Generic method 'SendAsync' not found on '{clientType.FullName}'.");

                // 3. 建立 Expression Tree 參數
                var clientParam = Expression.Parameter(typeof(object), "client");
                var commandParam = Expression.Parameter(typeof(object), "command");
                var isUpdateParam = Expression.Parameter(typeof(bool), "isUpdate");
                var tokenParam = Expression.Parameter(typeof(CancellationToken), "token");

                // 4. 將傳入的 object 轉型為真實型別
                var castClient = Expression.Convert(clientParam, clientType);
                var castCommand = Expression.Convert(commandParam, commandInterface);

                // 5. 呼叫 SendAsync 方法
                var callExpr = Expression.Call(castClient, methodInfo, castCommand, isUpdateParam, tokenParam);

                // 6. 將回傳的 Task<TResponse> 轉型為基礎 Task (因為在你的情境中沒有去接 TResponse 的回傳值)
                var castResult = Expression.Convert(callExpr, typeof(Task));

                // lambda: (client, command, isUpdate, token) => (Task)client.SendAsync<TResponse>((ICommand<TResponse>)command, isUpdate, token)
                var lambda = Expression.Lambda<Func<object, object, bool, CancellationToken, Task>>(
                    castResult, clientParam, commandParam, isUpdateParam, tokenParam);
                return lambda.Compile();
            }));

            return lazyInvoker.Value(client, command, isUpdate, token);
        }
    }
}

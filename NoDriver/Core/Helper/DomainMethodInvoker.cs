using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace NoDriver.Core.Helper
{
    internal static class DomainMethodInvoker
    {
        private static readonly ConcurrentDictionary<string, Func<object>> _invokerCache = new();

        public static Func<object> GetInvoker(Type staticClass, string methodName)
        {
            var key = $"{staticClass.Name}.{methodName}";
            if (_invokerCache.TryGetValue(key, out var invoker)) 
                return invoker;

            var methodInfo = staticClass.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);

            if (methodInfo == null)
                throw new MissingMethodException($"Static method '{methodName}' not found in class '{staticClass.Name}'.");

            var methodCall = Expression.Call(null, methodInfo);

            var convertedCall = Expression.Convert(methodCall, typeof(object));

            // lambda: () => (object)ClassName.Method()
            var lambda = Expression.Lambda<Func<object>>(convertedCall);

            invoker = lambda.Compile();
            _invokerCache[key] = invoker;
            return invoker;
        }
    }
}

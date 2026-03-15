using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace NoDriver.Core.Tools
{
    internal static class DomainMethodInvoker
    {
        private static readonly ConcurrentDictionary<string, Lazy<Func<object>>> _invokerCache = new();

        public static Func<object> GetInvoker(Type staticClass, string methodName)
        {
            var key = $"{staticClass.FullName}.{methodName}";

            var lazyInvoker = _invokerCache.GetOrAdd(key, (_) => new Lazy<Func<object>>(() =>
            {
                var methodInfo = staticClass.GetMethod(methodName,
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);

                if (methodInfo == null)
                    throw new MissingMethodException($"Static method '{methodName}' not found in class '{staticClass.FullName}'.");

                var parameters = methodInfo.GetParameters();
                var argumentExpressions = parameters.Select<ParameterInfo, Expression>(it =>
                {
                    if (it.HasDefaultValue)
                        return Expression.Constant(it.DefaultValue, it.ParameterType);
                    return Expression.Default(it.ParameterType);
                }).ToArray();

                var methodCall = Expression.Call(null, methodInfo, argumentExpressions);
                var convertedCall = Expression.Convert(methodCall, typeof(object));

                // lambda: () => (object)ClassName.Method()
                var lambda = Expression.Lambda<Func<object>>(convertedCall);
                return lambda.Compile();
            }));

            return lazyInvoker.Value;
        }
    }
}

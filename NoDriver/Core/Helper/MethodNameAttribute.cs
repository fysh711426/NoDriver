namespace NoDriver.Core.Helper
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class MethodNameAttribute : Attribute
    {
        public string MethodName { get; }

        public MethodNameAttribute(string methodName)
        {
            MethodName = methodName;
        }
    }
}

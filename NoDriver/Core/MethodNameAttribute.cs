using System;

namespace NoDriver.Core
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

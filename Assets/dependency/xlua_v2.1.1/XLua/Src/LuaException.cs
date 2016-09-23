using System;

namespace LuaInterface
{
    [Serializable]
    public class LuaException : Exception
    {
        public LuaException(string message) : base(message)
        {}
    }
}

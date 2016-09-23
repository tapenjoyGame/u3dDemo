#if USE_UNI_LUA
using LuaAPI = UniLua.Lua;
using RealStatePtr = UniLua.ILuaState;
using LuaCSFunction = UniLua.CSharpFunctionDelegate;
#else
using LuaAPI = LuaDLL.Lua;
using RealStatePtr = System.IntPtr;
using LuaCSFunction = LuaDLL.lua_CSFunction;
#endif

using System;
using System.Collections.Generic;
using System.Text;

namespace LuaInterface
{
    public class LuaFunction : LuaBase
    {
        public LuaFunction(int reference, LuaEnv interpreter) : base(reference, interpreter)
        {
        }
        public object[] Call(object[] args, Type[] returnTypes)
        {
            //return _Interpreter.callFunction(this, args, returnTypes);
            int nArgs = 0;
            var L = _Interpreter.L;
            var translator = _Interpreter.translator;
            int oldTop = LuaAPI.lua_gettop(L);

            int errFunc = LuaAPI.load_error_func(L);

            if (!LuaAPI.lua_checkstack(L, args.Length + 6))
                throw new LuaException("Lua stack overflow");
            LuaAPI.lua_getref(L, _Reference);
            if (args != null)
            {
                nArgs = args.Length;
                for (int i = 0; i < args.Length; i++)
                {
                    translator.PushAny(L, args[i]);
                }
            }
            int error = LuaAPI.lua_pcall(L, nArgs, -1, errFunc);
            if (error != 0)
                _Interpreter.ThrowExceptionFromError(oldTop);

            LuaAPI.lua_remove(L, errFunc);
            if (returnTypes != null)
                return translator.popValues(L, oldTop, returnTypes);
            else
                return translator.popValues(L, oldTop);
        }

        public object[] Call(params object[] args)
        {
            return Call(args, null);
        }
        public void SetEnv(LuaTable env)
        {
            var L = _Interpreter.L;
            int oldTop = LuaAPI.lua_gettop(L);
            push(L);
            env.push(L);
            LuaAPI.lua_setfenv(L, -2);
            LuaAPI.lua_settop(L, oldTop);
        }

        internal override void push(RealStatePtr L)
        {
            LuaAPI.lua_getref(L, _Reference);
        }
        public override string ToString()
        {
            return "function";
        }
    }

}

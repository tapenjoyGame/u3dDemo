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

namespace LuaInterface
{
    public partial class DelegateBridge : LuaBase
    {
        public Dictionary<Type, Delegate> BindTo = new Dictionary<Type, Delegate>();

        public DelegateBridge(int reference, LuaEnv interpreter) : base(reference, interpreter)
        {
        }

        protected override Action getGCAction()
        {
            var L = _Interpreter.L;
            var translator = _Interpreter.translator;
            return () => {
#if USE_UNI_LUA
                if (L != null)
#else
                if (L != RealStatePtr.Zero)
#endif
                {
                    LuaAPI.lua_rawgeti(L, LuaIndexes.LUA_REGISTRYINDEX, _Reference);
                    if (LuaAPI.lua_isnil(L, -1))
                    {
                        LuaAPI.lua_pop(L, 1);
                    }
                    else
                    {
                        LuaAPI.lua_pushvalue(L, -1);
                        LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
                        if (LuaAPI.lua_type(L, -1) == LuaTypes.LUA_TNUMBER && LuaAPI.lua_tointeger(L, -1) == _Reference) //
                        {
                            //UnityEngine.Debug.LogWarning("release delegate ref = " + _Reference);
                            LuaAPI.lua_pop(L, 1);// pop LUA_REGISTRYINDEX[func]
                            LuaAPI.lua_pushnil(L);
                            LuaAPI.lua_rawset(L, LuaIndexes.LUA_REGISTRYINDEX); // LUA_REGISTRYINDEX[func] = nil
                        }
                        else //another Delegate ref the function before the GC tick
                        {
                            LuaAPI.lua_pop(L, 2); // pop LUA_REGISTRYINDEX[func] & func
                        }
                    }
                    
                    LuaAPI.lua_unref(L, _Reference);
                    translator.RemoveDelegateBridge(_Reference);
                }
            };
        }
    }
}

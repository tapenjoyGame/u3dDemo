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
    public abstract class LuaBase : IDisposable
    {
        protected bool _Disposed;
        public int _Reference;
        public LuaEnv _Interpreter;

        public LuaBase(int reference, LuaEnv interpreter)
        {
            _Reference = reference;
            _Interpreter = interpreter;
        }

        ~LuaBase()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual Action getGCAction()
        {
            var L = _Interpreter.L;
            return () => {
#if USE_UNI_LUA
                if (L != null)
#else
                if (L != RealStatePtr.Zero)
#endif
                {
                    LuaAPI.lua_unref(L, _Reference);
                }
            };
        }

        public virtual void Dispose(bool disposeManagedResources)
        {
            if (!_Disposed)
            {
                if (_Reference != 0)
                {
                    Action acton = getGCAction();
                    if (disposeManagedResources)
                    {
                        acton();
                    }
                    else //will dispse by LuaEnv.GC
                    {
                        _Interpreter.equeueGCAction(acton);
                    }
                }
                _Interpreter = null;
                _Disposed = true;
            }
        }

        public override bool Equals(object o)
        {
            if (this.GetType() == o.GetType())
            {
                LuaBase rhs = (LuaBase)o;
                var L = _Interpreter.L;
                int top = LuaAPI.lua_gettop(L);
                LuaAPI.lua_getref(L, rhs._Reference);
                LuaAPI.lua_getref(L, _Reference);
                int equal = LuaAPI.lua_rawequal(L, -1, -2);
                LuaAPI.lua_settop(L, top);
                return (equal != 0);
            }
            else return false;
        }

        public override int GetHashCode()
        {
            return _Reference;
        }

        internal virtual void push(RealStatePtr L)
        {
            LuaAPI.lua_getref(L, _Reference);
        }
    }
}

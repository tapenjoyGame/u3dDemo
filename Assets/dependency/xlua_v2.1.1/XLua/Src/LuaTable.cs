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
using System.Collections;

namespace LuaInterface
{
    public class LuaTable : LuaBase
    {
        public LuaTable(int reference, LuaEnv interpreter) : base(reference, interpreter)
        {
        }

        public object this[string field]
        {
            get
            {
                return GetInPath<object>(field);
            }
            set
            {
                string[] path = field.Split(new char[] { '.' });
                var L = _Interpreter.L;
                int oldTop = LuaAPI.lua_gettop(L);
                LuaAPI.lua_getref(L, _Reference);
                Utils.GetInPath(L, -1, path, path.Length - 1);

                _Interpreter.translator.PushAny(L, path[path.Length -1]);
                _Interpreter.translator.PushAny(L, value);
                if (0 != LuaAPI.xlua_psettable(L, -3))
                {
                    _Interpreter.ThrowExceptionFromError(oldTop);
                }

                LuaAPI.lua_settop(L, oldTop);
            }
        }
        /*
         * Indexer for numeric fields of the table
         */
        public object this[object field]
        {
            get
            {
                return Get<object>(field);
            }
            set
            {
                var L = _Interpreter.L;
                int oldTop = LuaAPI.lua_gettop(L);
                LuaAPI.lua_getref(L, _Reference);
                _Interpreter.translator.PushAny(L, field);
                _Interpreter.translator.PushAny(L, value);
                if (0 != LuaAPI.xlua_psettable(L, -3))
                {
                    _Interpreter.ThrowExceptionFromError(oldTop);
                }
                LuaAPI.lua_settop(L, oldTop);
            }
        }

        public IEnumerable GetKeys()
        {
            var L = _Interpreter.L;
            var translator = _Interpreter.translator;
            int oldTop = LuaAPI.lua_gettop(L);
            LuaAPI.lua_getref(L, _Reference);
            LuaAPI.lua_pushnil(L);
            while (LuaAPI.lua_next(L, -2) != 0)
            {
                yield return translator.GetObject(L, -2);
                LuaAPI.lua_pop(L, 1);
            }
            LuaAPI.lua_settop(L, oldTop);
        }

        public IEnumerable<T> GetKeys<T>()
        {
            var L = _Interpreter.L;
            var translator = _Interpreter.translator;
            int oldTop = LuaAPI.lua_gettop(L);
            LuaAPI.lua_getref(L, _Reference);
            LuaAPI.lua_pushnil(L);
            while (LuaAPI.lua_next(L, -2) != 0)
            {
                if (translator.Assignable<T>(L, -2))
                {
                    T v;
                    translator.Get(L, -2, out v);
                    yield return v;
                }
                LuaAPI.lua_pop(L, 1);
            }
            LuaAPI.lua_settop(L, oldTop);
        }

        public T Get<T>(object key)
        {
            var L = _Interpreter.L;
            var translator = _Interpreter.translator;
            int oldTop = LuaAPI.lua_gettop(L);
            LuaAPI.lua_getref(L, _Reference);
            translator.PushAny(L, key);
            if (0 != LuaAPI.xlua_pgettable(L, -2))
            {
                string err = LuaAPI.lua_tostring(L, -1);
                LuaAPI.lua_settop(L, oldTop);
                throw new Exception("get field" + key + " error:" + err);
            }
            object returnValue = translator.GetObject(L, -1, typeof(T));
            LuaTypes lua_type = LuaAPI.lua_type(L, -1);
            LuaAPI.lua_settop(L, oldTop);
            if (lua_type == LuaTypes.LUA_TNIL && typeof(T).IsValueType)
            {
                throw new InvalidCastException("can not assign nil to " + typeof(T));
            }
            if (returnValue == null && lua_type != LuaTypes.LUA_TNIL)
            {
                throw new InvalidCastException("can not assign " + lua_type + " to " + typeof(T));
            }
            return returnValue == null? default(T): (T)returnValue;
        }

        public T GetInPath<T>(string[] path)
        {
            var L = _Interpreter.L;
            var translator = _Interpreter.translator;
            int oldTop = LuaAPI.lua_gettop(L);
            LuaAPI.lua_getref(L, _Reference);
            Utils.GetInPath(L, -1, path, path.Length);
            object returnValue = translator.GetObject(L, -1, typeof(T));
            LuaTypes lua_type = LuaAPI.lua_type(L, -1);
            LuaAPI.lua_settop(L, oldTop);
            if (lua_type == LuaTypes.LUA_TNIL && typeof(T).IsValueType)
            {
                throw new InvalidCastException("can not assign nil to " + typeof(T));
            }
            if (returnValue == null && lua_type != LuaTypes.LUA_TNIL)
            {
                throw new InvalidCastException("can not assign " + lua_type + " to " + typeof(T));
            }
            return returnValue == null ? default(T) : (T)returnValue;
        }

        public T GetInPath<T>(string path)
        {
            return GetInPath<T>(path.Split(new char[] { '.' }));
        }
		
		public void SetMetaTable(LuaTable metaTable)
		{
			push(_Interpreter.L);
			metaTable.push(_Interpreter.L);
			LuaAPI.lua_setmetatable(_Interpreter.L, -2);
			LuaAPI.lua_pop(_Interpreter.L, 1);
		}

        /*
         * Pushes this table into the Lua stack
         */
        internal override void push(RealStatePtr luaState)
        {
            LuaAPI.lua_getref(luaState, _Reference);
        }
        public override string ToString()
        {
            return "table";
        }
    }
}

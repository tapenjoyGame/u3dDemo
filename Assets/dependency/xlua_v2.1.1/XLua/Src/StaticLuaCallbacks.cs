#if USE_UNI_LUA
using LuaAPI = UniLua.Lua;
using RealStatePtr = UniLua.ILuaState;
using LuaCSFunction = UniLua.CSharpFunctionDelegate;
#else
using LuaAPI = LuaDLL.Lua;
using RealStatePtr = System.IntPtr;
using LuaCSFunction = LuaDLL.lua_CSFunction;
#endif

namespace LuaInterface
{
	using System;
	using System.IO;
	using System.Collections;
	using System.Reflection;
	using System.Diagnostics;
	using System.Collections.Generic;
	using System.Runtime.InteropServices;

    public class StaticLuaCallbacks
    {
        internal LuaCSFunction GcMeta, ToStringMeta;

        internal LuaCSFunction StaticCSFunctionWraper;
        
        public StaticLuaCallbacks()
        {
            GcMeta = new LuaCSFunction(StaticLuaCallbacks.LuaGC);
            ToStringMeta = new LuaCSFunction(StaticLuaCallbacks.ToString);
            StaticCSFunctionWraper = new LuaCSFunction(StaticLuaCallbacks.StaticCSFunction);
        }

        [MonoPInvokeCallback(typeof(LuaCSFunction))]
        static int StaticCSFunction(RealStatePtr L)
        {
            ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
            LuaCSFunction func = (LuaCSFunction)translator.FastGetCSObj(L, LuaAPI.xlua_upvalueindex(1));
            return func(L);
        }

        [MonoPInvokeCallback(typeof(LuaCSFunction))]
        public static int DelegateCall(RealStatePtr L)
        {
            ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
            object objDelegate = translator.FastGetCSObj(L, 1);
            if (objDelegate == null || !(objDelegate is Delegate))
            {
                return LuaAPI.luaL_error(L, "trying to invoke a not delegate or callable value");
            }
            return translator.methodWrapsCache.GetDelegateWrap(objDelegate.GetType())(L);
        }

        [MonoPInvokeCallback(typeof(LuaCSFunction))]
        public static int LuaGC(RealStatePtr L)
        {
            int udata = LuaAPI.xlua_tocsobj_safe(L, 1);
            if (udata != -1)
            {
                ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
                translator.collectObject(udata);
            }
            return 0;
        }

        [MonoPInvokeCallback(typeof(LuaCSFunction))]
        public static int ToString(RealStatePtr L)
        {
            ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
            object obj = translator.FastGetCSObj(L, 1);
            translator.PushAny(L, obj != null ? (obj.ToString() + ": " + obj.GetHashCode()) : "<invalid c# object>");
            return 1;
        }

        [MonoPInvokeCallback(typeof(LuaCSFunction))]
        public static int DelegateCombine(RealStatePtr L)
        {
            var translator = ObjectTranslatorPool.Instance.Find(L);
            Type type = translator.FastGetCSObj(L, LuaAPI.lua_type(L, 1) == LuaTypes.LUA_TUSERDATA ? 1 : 2).GetType();
            Delegate d1 = translator.GetObject(L, 1, type) as Delegate;
            Delegate d2 = translator.GetObject(L, 2, type) as Delegate;
            if (d1 == null || d2 == null)
            {
                return LuaAPI.luaL_error(L, "parameters must be has one delegate, other one must be delegate or function");
            }
            translator.PushAny(L, Delegate.Combine(d1, d2));
            return 1;
        }

        [MonoPInvokeCallback(typeof(LuaCSFunction))]
        public static int DelegateRemove(RealStatePtr L)
        {
            var translator = ObjectTranslatorPool.Instance.Find(L);
            Delegate d1 = translator.FastGetCSObj(L, 1) as Delegate;
            if (d1 == null)
            {
                return LuaAPI.luaL_error(L, "#1 parameter must be an delegate");
            }
            Delegate d2 = translator.GetObject(L, 2, d1.GetType()) as Delegate;
            if (d2 == null)
            {
                return LuaAPI.luaL_error(L, "#2 parameter must be an delegate or an function ");
            }
            translator.PushAny(L, Delegate.Remove(d1, d2));
            return 1;
        }

        [MonoPInvokeCallback(typeof(LuaCSFunction))]
        public static int ArrayIndexer(RealStatePtr L)
        {
            ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
            System.Array array = (System.Array)translator.FastGetCSObj(L, 1);

            if (array == null)
            {
                return LuaAPI.luaL_error(L, "#1 parameter is not a array!");
            }

            int i = LuaAPI.lua_tointeger(L, 2);

            if (i >= array.Length)
            {
                return LuaAPI.luaL_error(L, "index out of range! i =" + i + ", array.Length=" + array.Length);
            }

            object ret = array.GetValue(i);
            translator.PushAny(L, ret);

            return 1;
        }

        [MonoPInvokeCallback(typeof(LuaCSFunction))]
        public static int ArrayNewIndexer(RealStatePtr L)
        {
            ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
            System.Array array = (System.Array)translator.FastGetCSObj(L, 1);

            if (array == null)
            {
                return LuaAPI.luaL_error(L, "#1 parameter is not a array!");
            }

            int i = LuaAPI.lua_tointeger(L, 2);

            if (i >= array.Length)
            {
                return LuaAPI.luaL_error(L, "index out of range! i =" + i + ", array.Length=" + array.Length);
            }

            object val = translator.GetObject(L, 3, array.GetType().GetElementType());

            array.SetValue(val, i);

            return 0;
        }

        [MonoPInvokeCallback(typeof(LuaCSFunction))]
        public static int ArrayLength(RealStatePtr L)
        {
            ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
            try
            {
                System.Array array = (System.Array)translator.FastGetCSObj(L, 1);
                LuaAPI.lua_pushnumber(L, array.Length);
            }
            catch (System.Exception e)
            {
                return LuaAPI.luaL_error(L, "c# exception:" + e + ",stack:" + e.StackTrace);
            }
            return 1;
        }

        [MonoPInvokeCallback(typeof(LuaCSFunction))]
        public static int MetaFuncIndex(RealStatePtr L)
        {
            ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
            Type type = (Type)translator.FastGetCSObj(L, 2);
            if (type == null)
            {
                return LuaAPI.luaL_error(L, "#2 param need a System.Type!");
            }
            //UnityEngine.Debug.Log("============================load type by __index:" + type);
            translator.TryDelayWrapLoader(L, type);
            LuaAPI.lua_pushvalue(L, 2);
            LuaAPI.lua_rawget(L, 1);
            return 1;
        }


        [MonoPInvokeCallback(typeof(LuaCSFunction))]
        internal static int Panic(RealStatePtr L)
        {
            string reason = String.Format("unprotected error in call to Lua API ({0})", LuaAPI.lua_tostring(L, -1));
            throw new LuaException(reason);
        }

        [MonoPInvokeCallback(typeof(LuaCSFunction))]
        internal static int Print(RealStatePtr L)
        {
            // For each argument we'll 'tostring' it
            int n = LuaAPI.lua_gettop(L);
            string s = String.Empty;

            LuaAPI.lua_getglobal(L, "tostring");

            for (int i = 1; i <= n; i++)
            {
                LuaAPI.lua_pushvalue(L, -1);  /* function to be called */
                LuaAPI.lua_pushvalue(L, i);   /* value to print */
                if (0 != LuaAPI.lua_pcall(L, 1, 1, 0))
                {
                    return LuaAPI.lua_error(L);
                }
                s += LuaAPI.lua_tostring(L, -1);

                if (i != n) s += "\t";

                LuaAPI.lua_pop(L, 1);  /* pop result */
            }
            UnityEngine.Debug.Log("LUA: " + s);
            return 0;
        }

        [MonoPInvokeCallback(typeof(LuaCSFunction))]
        internal static int LoadSocketCore(RealStatePtr L)
        {
            return LuaAPI.luaopen_socket_core(L);
        }

        [MonoPInvokeCallback(typeof(LuaCSFunction))]
        internal static int LoadBuiltinLib(RealStatePtr L)
        {
            string builtin_lib = LuaAPI.lua_tostring(L, 1);

            LuaEnv self = ObjectTranslatorPool.Instance.Find(L).interpreter;

            LuaCSFunction initer;

            if (self.buildin_initer.TryGetValue(builtin_lib, out initer))
            {
                LuaAPI.lua_pushstdcallcfunction(L, initer);
            }
            else
            {
                LuaAPI.lua_pushstring(L, string.Format(
                    "\n\tno such builtin lib '{0}'", builtin_lib));
            }
            return 1;
        }

        [MonoPInvokeCallback(typeof(LuaCSFunction))]
        internal static int LoadFromResource(RealStatePtr L)
        {
            string filename = LuaAPI.lua_tostring(L, 1).Replace('.', '/') + ".lua";

            // Load with Unity3D resources
            UnityEngine.TextAsset file = (UnityEngine.TextAsset)UnityEngine.Resources.Load(filename);
            if (file == null)
            {
                LuaAPI.lua_pushstring(L, string.Format(
                    "\n\tno such resource '{0}'", filename));
            }
            else
            {
                if (LuaAPI.luaL_loadbuffer(L, file.text, "@" + filename) != 0)
                {
                    return LuaAPI.luaL_error(L, String.Format("error loading module {0} from resource, {1}",
                        LuaAPI.lua_tostring(L, 1), LuaAPI.lua_tostring(L, -1)));
                }
            }

            return 1;
        }

        [MonoPInvokeCallback(typeof(LuaCSFunction))]
        internal static int LoadFromStreamingAssetsPath(RealStatePtr L)
        {
            string filename = LuaAPI.lua_tostring(L, 1).Replace('.', '/') + ".lua";
            var filepath = UnityEngine.Application.streamingAssetsPath + "/" + filename;
#if UNITY_ANDROID && !UNITY_EDITOR
            UnityEngine.WWW www = new UnityEngine.WWW(filepath);
            while (true)
            {
                if (www.isDone || !string.IsNullOrEmpty(www.error))
                {
                    System.Threading.Thread.Sleep(50); //比较hacker的做法
                    if (!string.IsNullOrEmpty(www.error))
                    {
                        LuaAPI.lua_pushstring(L, string.Format(
                           "\n\tno such file '{0}' in streamingAssetsPath!", filename));
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning("load lua file from StreamingAssets is obsolete, filename:" + filename);
                        if (LuaAPI.luaL_loadbuffer(L, www.text, "@" + filename) != 0)
                        {
                            return LuaAPI.luaL_error(L, String.Format("error loading module {0} from streamingAssetsPath, {1}",
                                LuaAPI.lua_tostring(L, 1), LuaAPI.lua_tostring(L, -1)));
                        }
                    }
                    break;
                }
            }
#else
            if (File.Exists(filepath))
            {
                Stream stream = File.Open(filepath, FileMode.Open, FileAccess.Read);
                StreamReader reader = new StreamReader(stream);
                string text = reader.ReadToEnd();
                stream.Close();

                UnityEngine.Debug.LogWarning("load lua file from StreamingAssets is obsolete, filename:" + filename);
                if (LuaAPI.luaL_loadbuffer(L, text, "@" + filename) != 0)
                {
                    return LuaAPI.luaL_error(L, String.Format("error loading module {0} from streamingAssetsPath, {1}",
                        LuaAPI.lua_tostring(L, 1), LuaAPI.lua_tostring(L, -1)));
                }
            }
            else
            {
                LuaAPI.lua_pushstring(L, string.Format(
                    "\n\tno such file '{0}' in streamingAssetsPath!", filename));
            }
#endif
            return 1;
        }

        [MonoPInvokeCallback(typeof(LuaCSFunction))]
        internal static int LoadFromCustomLoaders(RealStatePtr L)
        {
            string filename = LuaAPI.lua_tostring(L, 1);

            LuaEnv self = ObjectTranslatorPool.Instance.Find(L).interpreter;

            foreach (var loader in self.customLoaders)
            {
                string real_file_path = filename;
                byte[] bytes = loader(ref real_file_path);
                if (bytes != null)
                {
                    if (LuaAPI.luaL_loadbuffer(L, bytes, bytes.Length, "@" + real_file_path) != 0)
                    {
                        return LuaAPI.luaL_error(L, String.Format("error loading module {0} from CustomLoader, {1}",
                            LuaAPI.lua_tostring(L, 1), LuaAPI.lua_tostring(L, -1)));
                    }
                    return 1;
                }
            }
            LuaAPI.lua_pushstring(L, string.Format(
                "\n\tno such file '{0}' in CustomLoaders!", filename));
            return 1;
        }

        [MonoPInvokeCallback(typeof(LuaCSFunction))]
        public static int LoadAssembly(RealStatePtr L)
        {
            ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
            try
            {
                string assemblyName = LuaAPI.lua_tostring(L, 1);

                Assembly assembly = null;

                try
                {
                    assembly = Assembly.Load(assemblyName);
                }
                catch (BadImageFormatException)
                {
                    // The assemblyName was invalid.  It is most likely a path.
                }

                if (assembly == null)
                {
                    assembly = Assembly.Load(AssemblyName.GetAssemblyName(assemblyName));
                }

                if (assembly != null && !translator.assemblies.Contains(assembly))
                {
                    translator.assemblies.Add(assembly);
                }
            }
            catch (Exception e)
            {
                return LuaAPI.luaL_error(L, "LoadAssembly catch a exception: " + e + ",stack:" + e.StackTrace);
            }

            return 0;
        }


        [MonoPInvokeCallback(typeof(LuaCSFunction))]
        public static int ImportType(RealStatePtr L)
        {
            ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
            string className = LuaAPI.lua_tostring(L, 1);
            Type type = translator.FindType(className);
            if (type != null)
            {
                if (translator.TryDelayWrapLoader(L, type))
                {
                    LuaAPI.lua_pushboolean(L, true);
                }
                else
                {
                    return LuaAPI.luaL_error(L, "can not load type " + type);
                }
            }
            else
            {
                LuaAPI.lua_pushnil(L);
            }
            return 1;
        }

        [MonoPInvokeCallback(typeof(LuaCSFunction))]
        public static int Cast(RealStatePtr L)
        {
            ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
            Type type;
            translator.Get(L, 2, out type);

            if (type == null)
            {
                return LuaAPI.luaL_error(L, "#2 param[" + LuaAPI.lua_tostring(L, 2) + "]is not valid type indicator");
            }
            LuaAPI.luaL_getmetatable(L, type.FullName);
            if (LuaAPI.lua_isnil(L, -1))
            {
                return LuaAPI.luaL_error(L, "no gen code for " + LuaAPI.lua_tostring(L, 2));
            }
            LuaAPI.lua_setmetatable(L, 1);
            return 0;
        }

    }
}
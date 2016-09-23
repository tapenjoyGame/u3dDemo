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
	using System.Runtime.InteropServices;
	using System.Collections.Generic;
	using System.Diagnostics;
    using System.Linq;

    class ReferenceEqualsComparer : IEqualityComparer<object>
    {
        public new bool Equals(object o1, object o2)
        {
            return object.ReferenceEquals(o1, o2);
        }
        public int GetHashCode(object obj)
        {
            return obj.GetHashCode();
        }
    }

#pragma warning disable 414
    public class MonoPInvokeCallbackAttribute : System.Attribute
    {
        private Type type;
        public MonoPInvokeCallbackAttribute(Type t) { type = t; }
    }
#pragma warning restore 414

    public enum LuaTypes
    {
        LUA_TNONE = -1,
        LUA_TNIL = 0,
        LUA_TNUMBER = 3,
        LUA_TSTRING = 4,
        LUA_TBOOLEAN = 1,
        LUA_TTABLE = 5,
        LUA_TFUNCTION = 6,
        LUA_TUSERDATA = 7,
        LUA_TTHREAD = 8,
        LUA_TLIGHTUSERDATA = 2
    }

    public enum LuaGCOptions
    {
        LUA_GCSTOP = 0,
        LUA_GCRESTART = 1,
        LUA_GCCOLLECT = 2,
        LUA_GCCOUNT = 3,
        LUA_GCCOUNTB = 4,
        LUA_GCSTEP = 5,
        LUA_GCSETPAUSE = 6,
        LUA_GCSETSTEPMUL = 7,
    }

    public enum LuaThreadStatus
    {
        LUA_RESUME_ERROR = -1,
        LUA_OK = 0,
        LUA_YIELD = 1,
        LUA_ERRRUN = 2,
        LUA_ERRSYNTAX = 3,
        LUA_ERRMEM = 4,
        LUA_ERRERR = 5,
    }

    sealed class LuaIndexes
    {
        public static int LUA_REGISTRYINDEX = -10000;
        public static int LUA_ENVIRONINDEX=-10001;
        public static int LUA_GLOBALSINDEX=-10002;
    }

    public class ObjectPool
    {
        struct Slot
        {
            public int next;
            public object obj;

            public Slot(int next, object obj)
            {
                this.next = next;
                this.obj = obj;
            }
        }

        private Slot[] list = new Slot[512];
        private int head = -1;
        private int count = 0;

        public object this[int i]
        {
            get
            {
                if (i >= 0 && i < count)
                {
                    return list[i].obj;
                }

                return null;
            }
        }

        public void Clear()
        {
            head = -1;
            count = 0;
            list = new Slot[512];
        }

        void extend_capacity()
        {
            Slot[] new_list = new Slot[list.Length * 2];
            for (int i = 0; i < list.Length; i++)
            {
                new_list[i] = list[i];
            }
            list = new_list;
        }

        public int Add(object obj)
        {
            int index = -1;

            if (head != -1)
            {
                index = head;
                list[index].obj = obj;
                head = list[index].next;
                list[index].next = -2;
            }
            else
            {
                if (count == list.Length)
                {
                    extend_capacity();
                }
                index = count;
                list[index].next = -2;
                list[index].obj = obj;
                count = index + 1;
            }

            return index;
        }

        public bool TryGetValue(int index, out object obj)
        {
            if (index >= 0 && index < count && list[index].obj != null)
            {
                obj = list[index].obj;
                return true;
            }

            obj = null;
            return false;
        }

        public object Get(int index)
        {
            if (index >= 0 && index < count)
            {
                return list[index].obj;
            }
            return null;
        }

        public object Remove(int index)
        {
            if (index >= 0 && index < count)
            {
                object o = list[index].obj;
                list[index].obj = null;
                list[index].next = head;
                head = index;
                return o;
            }

            return null;
        }

        public object Replace(int index, object o)
        {
            if (index >= 0 && index < count)
            {
                object obj = list[index].obj;
                list[index].obj = o;
                return obj;
            }

            return null;
        }

        public int Check(int check_pos, int max_check, Func<object, bool> checker)
        {
            
            for(int i = 0; i < Math.Min(max_check, count); ++i)
            {
                check_pos %= count;
                if (list[check_pos].next == -2 && !Object.ReferenceEquals(list[check_pos].obj, null))
                {
                    if(!checker(list[check_pos].obj))
                    {
                        Replace(check_pos, null);
                    }
                }
                ++check_pos;
            }

            return check_pos %= count;
        }
    }

    public partial class ObjectTranslator
	{
        internal MethodWrapsCache methodWrapsCache;
        internal ObjectCheckers objectCheckers;
        internal ObjectCasters objectCasters;

        public readonly ObjectPool objects = new ObjectPool();
        //public readonly Dictionary<int, object> objects = new Dictionary<int, object>();
        // object to object #
        //Fix bug by john, struct equals is by value, blow will print
        //local v1=Vector3(1,1,1) 
        //local v2=Vector3(1,1,1) 
        //v1.x = 100 
        //print(v1.x, v2.x) 
        public readonly Dictionary<object, int> objectsBackMap = new Dictionary<object, int>(new ReferenceEqualsComparer());
		internal LuaEnv interpreter;
		public StaticLuaCallbacks metaFunctions;
		public List<Assembly> assemblies;
		private LuaCSFunction importTypeFunction,loadAssemblyFunction, castFunction;

        private readonly Dictionary<Type, MethodInfo> delegateBridgeMethods = new Dictionary<Type, MethodInfo>();
        private readonly Dictionary<Type, Type> interfaceBridges = new Dictionary<Type, Type>();

        //延迟加载
        private readonly Dictionary<Type, Action<RealStatePtr>> delayWrap = new Dictionary<Type, Action<RealStatePtr>>();
        private readonly Dictionary<Type, Action> delayBridge = new Dictionary<Type, Action>();

        //无法访问的类，比如声明成internal，可以用其接口、基类的生成代码来访问
        private readonly Dictionary<Type, Type> aliasCfg = new Dictionary<Type, Type>();

        public void DelayWrapLoader(Type type, Action<RealStatePtr> loader)
        {
            delayWrap[type] = loader;
        }

        public void DelayBridgeLoader(Type type, Action loader)
        {
            delayBridge[type] = loader;
        }

        Dictionary<Type, bool> loaded_types = new Dictionary<Type, bool>();
        public bool TryDelayWrapLoader(RealStatePtr L, Type type)
        {
            if (loaded_types.ContainsKey(type)) return true;
            loaded_types.Add(type, true);

            LuaAPI.luaL_newmetatable(L, type.FullName); //先建一个metatable，因为加载过程可能会需要用到
            LuaAPI.lua_pop(L, 1);

            //if (type.BaseType != null)
            //{
            //    TryDelayWrapLoader(L, type.BaseType);
            //}
            Action<RealStatePtr> loader;
            int top = LuaAPI.lua_gettop(L);
            if (delayWrap.TryGetValue(type, out loader))
            {
                delayWrap.Remove(type);
                loader(L);
            }
            else
            {
                Utils.ReflectionWrap(L, type);
#if NOT_GEN_WARNING
                UnityEngine.Debug.LogWarning(string.Format("{0} not gen, using reflection instead", type));
#endif
            }
            if (top != LuaAPI.lua_gettop(L))
            {
                throw new Exception("top change, before:" + top + ", after:" + LuaAPI.lua_gettop(L));
            }

            foreach (var nested_type in type.GetNestedTypes())
            {
                TryDelayWrapLoader(L, nested_type);
            }
            
            return true;
        }
        
        public void Alias(Type type, string alias)
        {
            Type alias_type = FindType(alias);
            if (alias_type == null)
            {
                throw new ArgumentException("Can not find " + alias);
            }
            aliasCfg[alias_type] = type;
        }

        public int cacheRef;

        //TODO: 优化，去掉interpreter
        public ObjectTranslator(LuaEnv interpreter,RealStatePtr L)
		{
            assemblies = new List<Assembly>();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                assemblies.Add(assembly);
            }

			this.interpreter=interpreter;
            objectCasters = new ObjectCasters(this);
            objectCheckers = new ObjectCheckers(this);
            methodWrapsCache = new MethodWrapsCache(this, objectCheckers, objectCasters);
			metaFunctions=new StaticLuaCallbacks();

            importTypeFunction = new LuaCSFunction(StaticLuaCallbacks.ImportType);
            loadAssemblyFunction = new LuaCSFunction(StaticLuaCallbacks.LoadAssembly);
            castFunction = new LuaCSFunction(StaticLuaCallbacks.Cast);

            LuaAPI.lua_newtable(L);
            LuaAPI.lua_newtable(L);
            LuaAPI.lua_pushstring(L, "__mode");
            LuaAPI.lua_pushstring(L, "v");
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.lua_setmetatable(L, -2);
            cacheRef = LuaAPI.luaL_ref(L, LuaIndexes.LUA_REGISTRYINDEX);

            if (initers != null)
            {
                for (int i = 0; i < initers.Count; i++)
                {
                    initers[i](this);
                }
            }

            initCSharpCallLua();
        }

        private static List<Action<ObjectTranslator>> initers = null;

        public static void AddIniter(Action<ObjectTranslator> initer)
        {
            if (initers == null) initers = new List<Action<ObjectTranslator>>();
            initers.Add(initer);
        }

        enum LOGLEVEL{
            NO,
            INFO,
            WARN,
            ERROR
        }

        Type delegate_birdge_type;

#if UNITY_EDITOR
        class CompareByArgRet : IEqualityComparer<MethodInfo>
        {
            public bool Equals(MethodInfo x, MethodInfo y)
            {
                return Utils.IsParamsMatch(x, y);
            }
            public int GetHashCode(MethodInfo method)
            {
                int hc = 0;
                hc += method.ReturnType.GetHashCode();
                foreach (var pi in method.GetParameters())
                {
                    hc += pi.ParameterType.GetHashCode();
                }
                return hc;
            }
        }
#endif

        void initCSharpCallLua()
        {
            delegate_birdge_type = typeof(DelegateBridge);
#if UNITY_EDITOR
            if (delegate_birdge_type.GetProperty("__Gen_Flag") == null)
            {
                List<Type> gen_delegates = new List<Type>();
                List<Type> gen_interfaces = new List<Type>();
                foreach (var type in (from assembly in AppDomain.CurrentDomain.GetAssemblies()
                                   where !(assembly.ManifestModule is System.Reflection.Emit.ModuleBuilder)
                                   from type in assembly.GetExportedTypes()
                                   select type))
                {
                    if (!type.IsInterface && typeof(LuaInterface.GenConfig).IsAssignableFrom(type))
                    {
                        var cfg = Activator.CreateInstance(type) as LuaInterface.GenConfig;
                        var cs_call_lua = cfg.CSharpCallLua;
                        if (cs_call_lua != null)
                        {
                            gen_delegates.AddRange(cs_call_lua.Where(t => !t.IsGenericTypeDefinition && typeof(Delegate).IsAssignableFrom(t)));
                            gen_interfaces.AddRange(cs_call_lua.Where(t => !t.IsGenericTypeDefinition && t.IsInterface));
                        }
                    }
                    else if(type.IsDefined(typeof(CSharpCallLuaAttribute), false))
                    {
                        if (typeof(Delegate).IsAssignableFrom(type))
                        {
                            gen_delegates.Add(type);
                        }
                        else if(type.IsInterface)
                        {
                            gen_interfaces.Add(type);
                        }
                    }
                }
                IEnumerable<MethodInfo> method_to_be_impl = (from type in gen_delegates select type.GetMethod("Invoke")).Distinct(new CompareByArgRet());

                ce.SetGenInterfaces(gen_interfaces);
                delegate_birdge_type = ce.EmitDelegateImpl(method_to_be_impl);
            }
#endif
        }

        public MethodInfo findDelegateBridge(Type delegateType)
        {
            MethodInfo delegateMethod = delegateType.GetMethod("Invoke");
            var methods = delegate_birdge_type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly);
            foreach (MethodInfo mi in methods)
            {
                if (!mi.IsConstructor && Utils.IsParamsMatch(delegateMethod, mi))
                {
                    return mi;
                }
            }
            return null;
        }

#if UNITY_EDITOR
        CodeEmit ce = new CodeEmit();
#endif

        MethodInfo getBridgeMethodInfo(Type delegateType)
        {
            MethodInfo bridgeMethod = null;
            if (!delegateBridgeMethods.TryGetValue(delegateType, out bridgeMethod))
            {
                bridgeMethod = findDelegateBridge(delegateType);
                if (bridgeMethod == null)
                {
                    throw new InvalidCastException("No gen code for delegate: " + delegateType);
                }
                delegateBridgeMethods[delegateType] = bridgeMethod;
            }
            return bridgeMethod;
        }

        Dictionary<int, WeakReference> delegate_bridges = new Dictionary<int, WeakReference>();
        public Delegate CreateDelegateBridge(RealStatePtr L, Type delegateType, int idx)
        {
            LuaAPI.lua_pushvalue(L, idx);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
            if (!LuaAPI.lua_isnil(L, -1))
            {
                int referenced = LuaAPI.lua_tointeger(L, -1);
                LuaAPI.lua_pop(L, 1);

                if (delegate_bridges[referenced].IsAlive)
                {
                    DelegateBridge exist_bridge = delegate_bridges[referenced].Target as DelegateBridge;
                    Delegate exist_delegate;
                    if (exist_bridge.BindTo.TryGetValue(delegateType, out exist_delegate))
                    {
                        return exist_delegate;
                    }
                    else
                    {
                        exist_delegate = Delegate.CreateDelegate(delegateType, exist_bridge, getBridgeMethodInfo(delegateType));
                        exist_bridge.BindTo.Add(delegateType, exist_delegate);
                        return exist_delegate;
                    }
                }
            }
            else
            {
                LuaAPI.lua_pop(L, 1);
            }

            MethodInfo bridgeMethod = getBridgeMethodInfo(delegateType);

            LuaAPI.lua_pushvalue(L, idx);
            int reference = LuaAPI.luaL_ref(L);
            LuaAPI.lua_pushvalue(L, idx);
            LuaAPI.lua_pushnumber(L, reference);
            LuaAPI.lua_rawset(L, LuaIndexes.LUA_REGISTRYINDEX);
            DelegateBridge bridge;
#if UNITY_EDITOR
            if (bridgeMethod.DeclaringType != typeof(DelegateBridge))
            {
                bridge = Activator.CreateInstance(bridgeMethod.DeclaringType, new object[] { reference, interpreter }) as DelegateBridge;
            }
            else
#endif
            {
                bridge = new DelegateBridge(reference, interpreter);
            }
            var ret = Delegate.CreateDelegate(delegateType, bridge, bridgeMethod);
            bridge.BindTo.Add(delegateType, ret);
            delegate_bridges[reference] = new WeakReference(bridge);
            return ret;
        }

        public void RemoveDelegateBridge(int reference)
        {
            if (delegate_bridges.ContainsKey(reference))
            {
                delegate_bridges.Remove(reference);
            }
        }

        public bool DelegateBridgeExisted(Type delegateType)
        {
            if (delegateBridgeMethods.ContainsKey(delegateType))
            {
                return true;
            }
            MethodInfo bridgeMethod = findDelegateBridge(delegateType);
            
            if (bridgeMethod != null)
            {
                delegateBridgeMethods[delegateType] = bridgeMethod;
                return true;
            }
            else
            {
                return false;
            }
        }

        public void RegisterInterfaceBridge(Type interfaceType, Type bridgeType)
        {
            Debug.Assert(interfaceType.IsInterface);
			Debug.Assert(bridgeType.IsClass && typeof(LuaBase).IsAssignableFrom(bridgeType));
			//UnityEngine.Debug.Log ("register " + bridgeType + " as " + interfaceType +" bridge!");
            interfaceBridges[interfaceType] = bridgeType;
        }

		public object CreateInterfaceBridge(RealStatePtr L, Type interfaceType, int idx)
        {
            if (!interfaceBridges.ContainsKey(interfaceType))
            {
                Action loader;
                if (!delayBridge.TryGetValue(interfaceType, out loader))
                {
#if UNITY_EDITOR
                    RegisterInterfaceBridge(interfaceType, ce.EmitInterfaceImpl(interfaceType));
#else
                    throw new InvalidCastException("No gen code for interface: " + interfaceType);
#endif
                }
                else
                {
                    delayBridge.Remove(interfaceType);
                    loader();
                }
            }
            Type bridgeType = interfaceBridges[interfaceType];
            LuaAPI.lua_pushvalue(L, idx);
            object bridge = Activator.CreateInstance(bridgeType, new object[] { LuaAPI.luaL_ref(L), interpreter });
            return bridge;
        }

        public bool InterfaceBridgeExisted(Type interfaceType)
        {
            return interfaceBridges.ContainsKey(interfaceType) || delayBridge.ContainsKey(interfaceType);
        }

        int common_array_meta = -1;
        public void CreateArrayMetatable(RealStatePtr L)
        {
            Dictionary<string, LuaCSFunction> getters = new Dictionary<string, LuaCSFunction>(){
                {"Length", StaticLuaCallbacks.ArrayLength},
            };

            Utils.RegisterWrap(L, null, null, null, null, null, getters,
                null, null, null, null, null, typeof(System.Array), StaticLuaCallbacks.ArrayIndexer, StaticLuaCallbacks.ArrayNewIndexer);
            common_array_meta = LuaAPI.luaL_ref(L, LuaIndexes.LUA_REGISTRYINDEX);
        }

        int common_delegate_meta = -1;
        public void CreateDelegateMetatable(RealStatePtr L)
        {
            Dictionary<string, LuaCSFunction> metas = new Dictionary<string, LuaCSFunction>(){
                {"__call", StaticLuaCallbacks.DelegateCall},
                {"__add", StaticLuaCallbacks.DelegateCombine},
                {"__sub", StaticLuaCallbacks.DelegateRemove},
            };

            Utils.RegisterWrap(L, null, metas, null, null, null, null,
                null, null, null, null, null, typeof(System.MulticastDelegate));
            common_delegate_meta = LuaAPI.luaL_ref(L, LuaIndexes.LUA_REGISTRYINDEX);
        }

		public void OpenLib(RealStatePtr L)
		{
            createFunctionMetatable(L);

            LuaAPI.lua_pushstring(L, "xlua");
            LuaAPI.lua_newtable(L);
            LuaAPI.lua_pushstring(L, "import_type");
			LuaAPI.lua_pushstdcallcfunction(L,importTypeFunction);
			LuaAPI.lua_rawset(L, -3);
            LuaAPI.lua_pushstring(L, "cast");
            LuaAPI.lua_pushstdcallcfunction(L, castFunction);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.lua_pushstring(L, "load_assembly");
			LuaAPI.lua_pushstdcallcfunction(L,loadAssemblyFunction);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.lua_rawset(L, LuaIndexes.LUA_GLOBALSINDEX);
		}
		
		private void createFunctionMetatable(RealStatePtr L)
		{
			LuaAPI.lua_newtable(L);
			LuaAPI.lua_pushstring(L,"__gc");
			LuaAPI.lua_pushstdcallcfunction(L,metaFunctions.GcMeta);
			LuaAPI.lua_rawset(L,-3);
            LuaAPI.lua_pushlightuserdata(L, LuaAPI.xlua_tag());
            LuaAPI.lua_pushnumber(L, 1);
            LuaAPI.lua_rawset(L, -3);

            LuaAPI.lua_pushvalue(L, -1);
            int type_id = LuaAPI.luaL_ref(L, LuaIndexes.LUA_REGISTRYINDEX);
            LuaAPI.lua_pushnumber(L, type_id);
            LuaAPI.lua_rawseti(L, -2, 1);
            LuaAPI.lua_pop(L, 1);

            typeIdMap.Add(typeof(LuaCSFunction), type_id);
        }
		
		internal Type FindType(string className)
		{
			foreach(Assembly assembly in assemblies)
			{
                Type klass = assembly.GetType(className);
				if(klass!=null)
				{
					return klass;
				}
			}
			return null;
		}

        bool hasMethod(Type type, string methodName)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.Name == methodName)
                {
                    return true;
                }
            }
            return false;
        }
		
		internal void collectObject(int obj_index_to_collect)
		{
			object o;
			
			if (objects.TryGetValue(obj_index_to_collect, out o))
			{
				objects.Remove(obj_index_to_collect);
                int obj_index;
                //lua gc是先把weak table移除后再调用__gc，这期间同一个对象可能再次push到lua，关联到新的index
                if (objectsBackMap.TryGetValue(o, out obj_index) && obj_index == obj_index_to_collect)
                {
                    objectsBackMap.Remove(o);
                }
			}
		}
		
		int addObject(object obj, bool need_cache)
		{
            int index = objects.Add(obj);
            if (need_cache)
            {
                objectsBackMap[obj] = index;
            }
			
			return index;
		}
		
		internal object GetObject(RealStatePtr L,int index)
		{
            return (objectCasters.GetCaster(typeof(object))(L, index, null));
        }

        public Type GetTypeOf(RealStatePtr L, int idx)
        {
            Type type = null;
            int type_id = LuaAPI.xlua_gettypeid(L, idx);
            if (type_id != -1)
            {
                typeMap.TryGetValue(type_id, out type);
            }
            return type;
        }

        public bool Assignable<T>(RealStatePtr L, int index)
		{
            return Assignable(L, index, typeof(T));
        }

        public bool Assignable(RealStatePtr L, int index, Type type)
        {
            if (LuaAPI.lua_type(L, index) == LuaTypes.LUA_TUSERDATA) // 快路径
            {
                int udata = LuaAPI.xlua_tocsobj_safe(L, index);
                object obj;
                if (udata != -1 && objects.TryGetValue(udata, out obj))
                {
                    return type.IsAssignableFrom(obj.GetType());
                }

                int type_id = LuaAPI.xlua_gettypeid(L, index);
                Type type_of_struct;
                if (type_id != -1 && typeMap.TryGetValue(type_id, out type_of_struct)) // is struct
                {
                    return type.IsAssignableFrom(type_of_struct);
                }
            }

            return objectCheckers.GetChecker(type)(L, index);
        }

        public object GetObject(RealStatePtr L, int index, Type type)
        {
            int udata = LuaAPI.xlua_tocsobj_safe(L, index);

            if (udata != -1)
            {
                return objects.Get(udata);
            }
            else
            {
                if (LuaAPI.lua_type(L, index) == LuaTypes.LUA_TUSERDATA)
                {
                    GetCSObject get;
                    int type_id = LuaAPI.xlua_gettypeid(L, index);
                    Type type_of_struct;
                    if (type_id != -1 && typeMap.TryGetValue(type_id, out type_of_struct) && type.IsAssignableFrom(type_of_struct) && custom_get_funcs.TryGetValue(type, out get))
                    {
                        return get(L, index);
                    }
                }
                return (objectCasters.GetCaster(type)(L, index, null));
            }
        }

        public void Get<T>(RealStatePtr L, int index, out T v)
        {
            v = (T)GetObject(L, index, typeof(T));
        }

        public T[] GetParams<T>(RealStatePtr L, int index)
        {
            T[] ret = new T[Math.Max(LuaAPI.lua_gettop(L) - index + 1, 0)];
            for(int i = 0; i < ret.Length; i++)
            {
                Get(L, index + i, out ret[i]);
            }
            return ret;
        }

        public Array GetParams(RealStatePtr L, int index, Type type) //反射版本
        {
            Array ret = Array.CreateInstance(type, Math.Max(LuaAPI.lua_gettop(L) - index + 1, 0));
            for (int i = 0; i < ret.Length; i++)
            {
                ret.SetValue(GetObject(L, index + i, type), i); 
            }
            return ret;
        }

        public T GetDelegate<T>(RealStatePtr L, int index) where T :class
        {
            
            if (LuaAPI.lua_isfunction(L, index))
            {
                return CreateDelegateBridge(L, typeof(T), index) as T;
            }
            else if (LuaAPI.lua_type(L, index) == LuaTypes.LUA_TUSERDATA)
            {
                return (T)SafeGetCSObj(L, index);
            }
            else
            {
                return null;
            }
        }

        Dictionary<Type, int> typeIdMap = new Dictionary<Type, int>();

        //only store the type id to type map for struct
        Dictionary<int, Type> typeMap = new Dictionary<int, Type>();

        int getTypeId(RealStatePtr L, Type type, LOGLEVEL log_level = LOGLEVEL.WARN)
        {
            int type_id;
            if (!typeIdMap.TryGetValue(type, out type_id)) // no reference
            {
                if (type.IsArray) return common_array_meta;
                if (typeof(MulticastDelegate).IsAssignableFrom(type)) return common_delegate_meta;

                Type alias_type = null;
                aliasCfg.TryGetValue(type, out alias_type);
                LuaAPI.luaL_getmetatable(L, alias_type == null ? type.FullName : alias_type.FullName);

                if (LuaAPI.lua_isnil(L, -1)) //no meta yet, try to use reflection meta
                {
                    LuaAPI.lua_pop(L, 1);

                    if (TryDelayWrapLoader(L, alias_type == null ? type : alias_type))
                    {
                        LuaAPI.luaL_getmetatable(L, alias_type == null ? type.FullName : alias_type.FullName);
                    }
                    else
                    {
                        throw new Exception("Fatal: can not load metatable of type:" + type);
                    }
                }

                //循环依赖，自身依赖自己的class，比如有个自身类型的静态readonly对象。
                if (typeIdMap.TryGetValue(type, out type_id))
                {
                    typeIdMap.Remove(type);
                    LuaAPI.lua_unref(L, type_id);
                    if (CopyByValue.IsStruct(type) && typeMap.ContainsKey(type_id))
                    {
                        typeMap.Remove(type_id);
                    }
                }
                LuaAPI.lua_pushvalue(L, -1);
                type_id = LuaAPI.luaL_ref(L, LuaIndexes.LUA_REGISTRYINDEX);
                LuaAPI.lua_pushnumber(L, type_id);
                LuaAPI.lua_rawseti(L, -2, 1);
                LuaAPI.lua_pop(L, 1);

                if (CopyByValue.IsStruct(type))
                {
                    typeMap.Add(type_id, type);
                }

                typeIdMap.Add(type, type_id);
            }
            return type_id;
        }

        void pushPrimitive(RealStatePtr L, object o)
        {
            if (o is sbyte || o is byte || o is short || o is ushort ||
                    o is int || o is uint || o is float ||
                    o is decimal || o is double)
            {
                double d = Convert.ToDouble(o);
                LuaAPI.lua_pushnumber(L, d);
            }
            else if (o is IntPtr)
            {
                LuaAPI.lua_pushlightuserdata(L, (IntPtr)o);
            }
            else if (o is char)
            {
                LuaAPI.lua_pushnumber(L, (char)o);
            }
            else if (o is long)
            {
                LuaAPI.lua_pushint64(L, Convert.ToInt64(o));
            }
            else if (o is ulong)
            {
                LuaAPI.lua_pushuint64(L, Convert.ToUInt64(o));
            }
            else if (o is bool)
            {
                bool b = (bool)o;
                LuaAPI.lua_pushboolean(L, b);
            }
            else
            {
                throw new Exception("No support type " + o.GetType());
            }
        }

        public void PushAny(RealStatePtr L, object o)
        {
            if (o == null)
            {
                LuaAPI.lua_pushnil(L);
                return;
            }

            Type type = o.GetType();
            if (type.IsPrimitive)
            {
                pushPrimitive(L, o);
            }
            else if (o is string)
            {
                LuaAPI.lua_pushstring(L, o as string);
            }
            else if (o is byte[])
            {
                LuaAPI.lua_pushstring(L, o as byte[]);
            }
            else if (o is LuaBase)
            {
                ((LuaBase)o).push(L);
            }
            else if (o is Enum)
            {
                Push(L, o as Enum);
            }
            else if (o is LuaCSFunction)
            {
                Push(L, o as LuaCSFunction);
            }
            else if (o is ValueType)
            {
                PushCSObject push;
                if (custom_push_funcs.TryGetValue(o.GetType(), out push))
                {
                    push(L, o);
                }
                else
                {
                    Push(L, o);
                }
            }
            else
            {
                Push(L, o);
            }
        }

        Dictionary<Enum, object> enumMap = new Dictionary<Enum, object>();

        public void Push(RealStatePtr L, Enum e)
        {
            object obj = null;
            if (!enumMap.TryGetValue(e, out obj))
            {
                obj = e;
                enumMap.Add(e, obj);
            }
            Push(L, obj);
        }

        public int TranslateToEnumToTop(RealStatePtr L, Type type, int idx)
        {
            object res = null;
            LuaTypes lt = (LuaTypes)LuaAPI.lua_type(L, idx);
            if (lt == LuaTypes.LUA_TNUMBER)
            {
                int ival = (int)LuaAPI.lua_tonumber(L, idx);
                res = Enum.ToObject(type, ival);
            }
            else
            if (lt == LuaTypes.LUA_TSTRING)
            {
                string sflags = LuaAPI.lua_tostring(L, idx);
                string err = null;
                try
                {
                    res = Enum.Parse(type, sflags);
                }
                catch (ArgumentException e)
                {
                    err = e.Message;
                }
                if (err != null)
                {
                    return LuaAPI.luaL_error(L, err);
                }
            }
            else {
                return LuaAPI.luaL_error(L, "#1 argument must be a integer or a string");
            }
            Push(L, res as Enum);
            return 1;
        }

        public void Push(RealStatePtr L, LuaCSFunction o)
        {
            Push(L, (object)o);
            LuaAPI.lua_pushstdcallcfunction(L, metaFunctions.StaticCSFunctionWraper, 1);
        }

        public void Push(RealStatePtr L, object o)
        {
            if (o == null)
            {
                LuaAPI.lua_pushnil(L);
                return;
            }

            int index = -1;
            Type type = o.GetType();
            bool needcache = !(type.IsValueType) || o is Enum;
            if (needcache && objectsBackMap.TryGetValue(o, out index))
            {
                if (LuaAPI.xlua_tryget_cachedud(L, index, cacheRef) == 1)
                {
                    return;
                }
                //这里实在太经典了，weaktable先删除，然后GC会延迟调用，当index会循环利用的时候，不注释这行将会导致重复释放
                //collectObject(index);
            }

            index = addObject(o, needcache);
            LuaAPI.xlua_pushcsobj(L, index, getTypeId(L, type), needcache, cacheRef);
        }

        public void PushObject(RealStatePtr L, object o, int type_id)
        {
            if (o == null)
            {
                LuaAPI.lua_pushnil(L);
                return;
            }

            int index = -1;
            if (objectsBackMap.TryGetValue(o, out index))
            {
                if (LuaAPI.xlua_tryget_cachedud(L, index, cacheRef) == 1)
                {
                    return;
                }
            }

            index = addObject(o, true);

            LuaAPI.xlua_pushcsobj(L, index, type_id, true, cacheRef);
        }

        public void Update(RealStatePtr L, int index, object obj)
        {
            int udata = LuaAPI.xlua_tocsobj_fast(L, index);

            if (udata != -1)
            {
                objects.Replace(udata, obj);
            }
            else
            {
                UpdateCSObject update;
                if (custom_update_funcs.TryGetValue(obj.GetType(), out update))
                {
                    update(L, index, obj);
                }
                else
                {
                    throw new Exception("can not update [" + obj + "]");
                }
            }
        }

        private object getCsObj(RealStatePtr L, int index, int udata)
        {
            object obj;
            if (udata == -1)
            {
                if (LuaAPI.lua_type(L, index) != LuaTypes.LUA_TUSERDATA) return null;

                Type type = GetTypeOf(L, index);
                GetCSObject get;
                if (type != null && custom_get_funcs.TryGetValue(type, out get))
                {
                    return get(L, index);
                }
                else
                {
                    return null;
                }
            }
            else if (objects.TryGetValue(udata, out obj))
            {
                return obj;
            }
            return null;
        }

        internal object SafeGetCSObj(RealStatePtr L, int index)
        {
            return getCsObj(L, index, LuaAPI.xlua_tocsobj_safe(L, index));
        }

		internal object FastGetCSObj(RealStatePtr L,int index)
		{
            return getCsObj(L, index, LuaAPI.xlua_tocsobj_fast(L,index));
		}

		internal object[] popValues(RealStatePtr L,int oldTop)
		{
			int newTop=LuaAPI.lua_gettop(L);
			if(oldTop==newTop)
			{
				return null;
			}
			else
			{
				ArrayList returnValues=new ArrayList();
				for(int i=oldTop+1;i<=newTop;i++)
				{
					returnValues.Add(GetObject(L,i));
				}
				LuaAPI.lua_settop(L,oldTop);
				return returnValues.ToArray();
			}
		}

		internal object[] popValues(RealStatePtr L,int oldTop,Type[] popTypes)
		{
			int newTop=LuaAPI.lua_gettop(L);
			if(oldTop==newTop)
			{
				return null;
			}
			else
			{
				int iTypes;
				ArrayList returnValues=new ArrayList();
				if(popTypes[0] == typeof(void))
					iTypes=1;
				else
					iTypes=0;
				for(int i=oldTop+1;i<=newTop;i++)
				{
					returnValues.Add(GetObject(L,i,popTypes[iTypes]));
					iTypes++;
				}
				LuaAPI.lua_settop(L,oldTop);
				return returnValues.ToArray();
			}
		}

        public delegate void PushCSObject(RealStatePtr L, object obj);
        public delegate object GetCSObject(RealStatePtr L, int idx);
        public delegate void UpdateCSObject(RealStatePtr L, int idx, object obj);

        private Dictionary<Type, PushCSObject> custom_push_funcs = new Dictionary<Type, PushCSObject>();
        private Dictionary<Type, GetCSObject> custom_get_funcs = new Dictionary<Type, GetCSObject>();
        private Dictionary<Type, UpdateCSObject> custom_update_funcs = new Dictionary<Type, UpdateCSObject>();

        public void RegisterCustomOp(Type type, PushCSObject push, GetCSObject get, UpdateCSObject update)
        {
            if (push != null) custom_push_funcs.Add(type, push);
            if (get != null) custom_get_funcs.Add(type, get);
            if (update != null) custom_update_funcs.Add(type, update);
        }

        public bool HasCustomOp(Type type)
        {
            return custom_push_funcs.ContainsKey(type);
        }
    }
}
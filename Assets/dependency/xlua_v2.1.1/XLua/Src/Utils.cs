using System.Collections.Generic;
using System;
using System.Reflection;
using System.Linq;

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
    public static class Utils
    {
        public static RealStatePtr GetMainState(RealStatePtr L)
        {
            RealStatePtr ret = default(RealStatePtr);
            LuaAPI.lua_pushstring(L, "xlua_main_thread");
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
            if (LuaAPI.lua_isthread(L, -1))
            {
                ret = LuaAPI.lua_tothread(L, -1);
            }
            LuaAPI.lua_pop(L, 1);
            return ret;
        }

        public static void GetInPath(RealStatePtr L, int idx, string[] path, int length)
        {
            LuaAPI.lua_pushvalue(L, idx);
            for (int i = 0; i < length; ++i)
            {
                LuaAPI.lua_pushstring(L, path[i]);
                if(0 != LuaAPI.xlua_pgettable(L, -2))
                {
                    LuaAPI.lua_pop(L, 1);
                    LuaAPI.lua_pushnil(L);
                    break;
                }
                if (!LuaAPI.lua_istable(L, -1) && i < length - 1)
                {
                    LuaAPI.lua_pop(L, 2);
                    LuaAPI.lua_pushnil(L);
                    break;
                }
                LuaAPI.lua_remove(L, -2);
            }
        }
        public static void NewTableInPath(RealStatePtr L, string[] path)
        {
            int oldTop = LuaAPI.lua_gettop(L);
            LuaAPI.lua_getglobal(L, "_G");
            for (int i = 0; i < path.Length; ++i)
            {
                LuaAPI.lua_pushstring(L, path[i]);
                if (0 != LuaAPI.xlua_pgettable(L, -2))
                {
                    throw new Exception("create table in [" + String.Join(".", path) + "] error:" + LuaAPI.lua_tostring(L, -1));
                }
                if (LuaAPI.lua_isnil(L, -1))
                {
                    LuaAPI.lua_pop(L, 1);
                    LuaAPI.lua_createtable(L, 0, 0);
                    LuaAPI.lua_pushstring(L, path[i]);
                    LuaAPI.lua_pushvalue(L, -2);
                    LuaAPI.lua_rawset(L, -4);
                }
                else if (!LuaAPI.lua_istable(L, -1))
                {
                    LuaAPI.lua_settop(L, oldTop);
                    throw new Exception("can not create table in [" + String.Join(".", path) + "]");
                }
                LuaAPI.lua_remove(L, -2);
            }
        }

        public static void NewTableInPath(RealStatePtr L, string path)
        {
            NewTableInPath(L, path.Split(new char[] { '.' }));
        }

        static LuaCSFunction genFieldGetter(Type type, FieldInfo field)
        {
            if (field.IsStatic)
            {
                return (RealStatePtr L) =>
                {
                    ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
                    translator.PushAny(L, field.GetValue(null));
                    return 1;
                };
            }
            else
            {
                return (RealStatePtr L) =>
                {
                    ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
                    object obj = translator.FastGetCSObj(L, 1);
                    if (obj == null || !type.IsInstanceOfType(obj))
                    {
                        return LuaAPI.luaL_error(L, "Expected type " + type + ", but got " + (obj == null ? "null" : obj.GetType().ToString()) + ", while get field " + field);
                    }

                    translator.PushAny(L, field.GetValue(obj));
                    return 1;
                };
            }
        }

        static LuaCSFunction genFieldSetter(Type type, FieldInfo field)
        {
            if (field.IsStatic)
            {
                return (RealStatePtr L) =>
                {
                    ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
                    object val = translator.GetObject(L, 1, field.FieldType);
                    if (field.FieldType.IsValueType && val == null)
                    {
                        return LuaAPI.luaL_error(L, type.Name + "." + field.Name + " Expected type " + field.FieldType);
                    }
                    field.SetValue(null, val);
                    return 0;
                };
            }
            else
            {
                return (RealStatePtr L) =>
                {
                    ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);

                    object obj = translator.FastGetCSObj(L, 1);
                    if (obj == null || !type.IsInstanceOfType(obj))
                    {
                        return LuaAPI.luaL_error(L, "Expected type " + type + ", but got " + (obj == null ? "null" : obj.GetType().ToString()) + ", while set field " + field);
                    }

                    object val = translator.GetObject(L, 2, field.FieldType);
                    if (field.FieldType.IsValueType && val == null)
                    {
                        return LuaAPI.luaL_error(L, type.Name + "." + field.Name + " Expected type " + field.FieldType);
                    }
                    field.SetValue(obj, val);
                    return 0;
                };
            }
        }

        static LuaCSFunction genPropGetter(Type type, PropertyInfo prop, bool is_static)
        {
            if (is_static)
            {
                return (RealStatePtr L) =>
                {
                    ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
                    try
                    {
                        translator.PushAny(L, prop.GetValue(null, null));
                    }
                    catch(Exception e)
                    {
                        return LuaAPI.luaL_error(L, "try to get " + type + "." + prop.Name + " throw a exception:" + e + ",stack:" + e.StackTrace);
                    }
                    return 1;
                };
            }
            else
            {
                return (RealStatePtr L) =>
                {
                    ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
                    object obj = translator.FastGetCSObj(L, 1);
                    if (obj == null || !type.IsInstanceOfType(obj))
                    {
                        return LuaAPI.luaL_error(L, "Expected type " + type + ", but got " + (obj == null ? "null" : obj.GetType().ToString()) + ", while get prop " + prop);
                    }

                    try
                    {
                        translator.PushAny(L, prop.GetValue(obj, null));
                    }
                    catch (Exception e)
                    {
                        return LuaAPI.luaL_error(L, "try to get " + type + "." + prop.Name + " throw a exception:" + e + ",stack:" + e.StackTrace);
                    }

                    return 1;
                };
            }
        }

        static LuaCSFunction genPropSetter(Type type, PropertyInfo prop, bool is_static)
        {
            if (is_static)
            {
                return (RealStatePtr L) =>
                {
                    ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
                    object val = translator.GetObject(L, 1, prop.PropertyType);
                    if (prop.PropertyType.IsValueType && val == null)
                    {
                        return LuaAPI.luaL_error(L, type.Name + "." + prop.Name + " Expected type " + prop.PropertyType);
                    }
                    try
                    { 
                        prop.SetValue(null, val, null);
                    }
                    catch (Exception e)
                    {
                        return LuaAPI.luaL_error(L, "try to set " + type + "." + prop.Name + " throw a exception:" + e + ",stack:" + e.StackTrace);
                    }
                    return 0;
                };
            }
            else
            {
                return (RealStatePtr L) =>
                {
                    ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);

                    object obj = translator.FastGetCSObj(L, 1);
                    if (obj == null || !type.IsInstanceOfType(obj))
                    {
                        return LuaAPI.luaL_error(L, "Expected type " + type + ", but got " + (obj == null ? "null" : obj.GetType().ToString()) + ", while set prop " + prop);
                    }

                    object val = translator.GetObject(L, 2, prop.PropertyType);
                    if (prop.PropertyType.IsValueType && val == null)
                    {
                        return LuaAPI.luaL_error(L, type.Name + "." + prop.Name + " Expected type " + prop.PropertyType);
                    }
                    try
                    {
                        prop.SetValue(obj, val, null);
                    }
                    catch (Exception e)
                    {
                        return LuaAPI.luaL_error(L, "try to set " + type + "." + prop.Name + " throw a exception:" + e + ",stack:" + e.StackTrace);
                    }
                    return 0;
                };
            }
        }

        static Dictionary<string, string> support_op = new Dictionary<string, string>()
        {
            { "op_Addition", "__add" },
            { "op_Subtraction", "__sub" },
            { "op_Multiply", "__mul" },
            { "op_Division", "__div" },
            { "op_Equality", "__eq" },
            { "op_UnaryNegation", "__unm" },
            { "op_LessThan", "__lt" },
            { "op_LessThanOrEqual", "__le" },
            { "op_Modulus", "__mod" }
        };

        static LuaCSFunction genItemGetter(Type type, PropertyInfo[] props)
        {
            Type[] params_type = new Type[props.Length];
            for(int i = 0; i < props.Length; i++)
            {
                params_type[i] = props[i].GetIndexParameters()[0].ParameterType;
            }
            object[] arg = new object[1] { null };
            return (RealStatePtr L) =>
            {
                ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
                object obj = translator.FastGetCSObj(L, 1);
                if (obj == null || !type.IsInstanceOfType(obj))
                {
                    return LuaAPI.luaL_error(L, "Expected type " + type + ", but got " + (obj == null ? "null" : obj.GetType().ToString()) + ", while get prop " + props[0].Name);
                }

                for (int i = 0; i < props.Length; i++)
                {
                    if (!translator.Assignable(L, 2, params_type[i]))
                    {
                        continue;
                    }
                    else
                    {
                        PropertyInfo prop = props[i];
                        try
                        {
                            object index = translator.GetObject(L, 2, params_type[i]);
                            arg[0] = index;
                            object ret = prop.GetValue(obj, arg);
                            LuaAPI.lua_pushboolean(L, true);
                            translator.PushAny(L, ret);
                            return 2;
                        }
                        catch (Exception e)
                        {
                            return LuaAPI.luaL_error(L, "try to get " + type + "." + prop.Name + " throw a exception:" + e + ",stack:" + e.StackTrace);
                        }
                    }
                }

                LuaAPI.lua_pushboolean(L, false);
                return 1;
            };
        }

        static LuaCSFunction genItemSetter(Type type, PropertyInfo[] props)
        {
            Type[] params_type = new Type[props.Length];
            for (int i = 0; i < props.Length; i++)
            {
                params_type[i] = props[i].GetIndexParameters()[0].ParameterType;
            }
            object[] arg = new object[1] { null };
            return (RealStatePtr L) =>
            {
                ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
                object obj = translator.FastGetCSObj(L, 1);
                if (obj == null || !type.IsInstanceOfType(obj))
                {
                    return LuaAPI.luaL_error(L, "Expected type " + type + ", but got " + (obj == null ? "null" : obj.GetType().ToString()) + ", while set prop " + props[0].Name);
                }

                for (int i = 0; i < props.Length; i++)
                {
                    if (!translator.Assignable(L, 2, params_type[i]))
                    {
                        continue;
                    }
                    else
                    {
                        PropertyInfo prop = props[i];
                        try
                        {
                            arg[0] = translator.GetObject(L, 2, params_type[i]);
                            object val = translator.GetObject(L, 3, prop.PropertyType);
                            if (val == null)
                            {
                                return LuaAPI.luaL_error(L, type.Name + "." + prop.Name + " Expected type " + prop.PropertyType);
                            }
                            prop.SetValue(obj, val, arg);
                            LuaAPI.lua_pushboolean(L, true);
                            
                            return 1;
                        }
                        catch (Exception e)
                        {
                            return LuaAPI.luaL_error(L, "try to set " + type + "." + prop.Name + " throw a exception:" + e + ",stack:" + e.StackTrace);
                        }
                    }
                }

                LuaAPI.lua_pushboolean(L, false);
                return 1;
            };
        }

        static LuaCSFunction genEnumCastFrom(Type type)
        {
            return (RealStatePtr L) =>
            {
                ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
                return translator.TranslateToEnumToTop(L, type, 1);
            };
        }

        static Dictionary<Type, Dictionary<string, List<MethodInfo>>> extension_method_map = null;
        static Dictionary<string, List<MethodInfo>> GetExtensionMethodsOf(Type type_to_be_extend)
        {
            if (extension_method_map == null)
            {
                extension_method_map = new Dictionary<Type, Dictionary<string, List<MethodInfo>>>();
                var grouped_methods = from assembly in AppDomain.CurrentDomain.GetAssemblies()
#if UNITY_EDITOR
                                      where !(assembly.ManifestModule is System.Reflection.Emit.ModuleBuilder)
#endif
                                      from type in assembly.GetExportedTypes()
                                      from method in type.GetMethods(BindingFlags.Static | BindingFlags.Public)
                                      where !method.ContainsGenericParameters && method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false)
                                      group method by method.GetParameters()[0].ParameterType;
                foreach (var group in grouped_methods)
                {
                    Dictionary<string, List<MethodInfo>> name_to_methods = new Dictionary<string, List<MethodInfo>>();
                    foreach(var method in group)
                    {
                        List<MethodInfo> methods;
                        if (!name_to_methods.TryGetValue(method.Name, out methods))
                        {
                            methods = new List<MethodInfo>();
                            name_to_methods.Add(method.Name, methods);
                        }
                        methods.Add(method);
                    }
                    extension_method_map.Add(group.Key, name_to_methods);
                }
            }
            Dictionary<string, List<MethodInfo>> ret = null;
            extension_method_map.TryGetValue(type_to_be_extend, out ret);
            return ret;
        }

        struct MethodKey
        {
            public string Name;
            public bool IsStatic;
        }

        public static void ReflectionWrap(RealStatePtr L, Type type)
        {
            int top_enter = LuaAPI.lua_gettop(L);
            ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
            //create obj meta table
            LuaAPI.luaL_getmetatable(L, type.FullName);
            if (LuaAPI.lua_isnil(L, -1))
            {
                LuaAPI.lua_pop(L, 1);
                LuaAPI.luaL_newmetatable(L, type.FullName);
            }
            LuaAPI.lua_pushlightuserdata(L, LuaAPI.xlua_tag());
            LuaAPI.lua_pushnumber(L, 1);
            LuaAPI.lua_rawset(L, -3);
            int obj_meta = LuaAPI.lua_gettop(L);

            LuaAPI.lua_newtable(L);
            int cls_meta = LuaAPI.lua_gettop(L);

            LuaAPI.lua_newtable(L);
            int obj_field = LuaAPI.lua_gettop(L);
            LuaAPI.lua_newtable(L);
            int obj_getter = LuaAPI.lua_gettop(L);
            LuaAPI.lua_newtable(L);
            int obj_setter = LuaAPI.lua_gettop(L);
            LuaAPI.lua_newtable(L);
            int cls_field = LuaAPI.lua_gettop(L);
            LuaAPI.lua_newtable(L);
            int cls_getter = LuaAPI.lua_gettop(L);
            LuaAPI.lua_newtable(L);
            int cls_setter = LuaAPI.lua_gettop(L);

            BindingFlags flag = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
            FieldInfo[] fields = type.GetFields(flag);

            for(int i = 0; i < fields.Length; ++i)
            {
                FieldInfo field = fields[i];
                if (field.IsStatic && (field.IsInitOnly || field.IsLiteral))
                {
                    LuaAPI.lua_pushstring(L, field.Name);
                    translator.PushAny(L, field.GetValue(null));
                    LuaAPI.lua_rawset(L, cls_field);
                }
                else
                {
                    LuaAPI.lua_pushstring(L, field.Name);
                    translator.PushAny(L, genFieldGetter(type, field));
                    LuaAPI.lua_rawset(L, field.IsStatic ? cls_getter : obj_getter);

                    LuaAPI.lua_pushstring(L, field.Name);
                    translator.PushAny(L, genFieldSetter(type, field));
                    LuaAPI.lua_rawset(L, field.IsStatic ? cls_setter : obj_setter);
                }
            }

            EventInfo[] events = type.GetEvents(flag);
            for(int i = 0; i < events.Length; ++i)
            {
                EventInfo eventInfo = events[i];
                LuaAPI.lua_pushstring(L, eventInfo.Name);
                translator.PushAny(L, translator.methodWrapsCache.GetEventWrap(type, eventInfo.Name));
                bool is_static = (eventInfo.GetAddMethod() != null) ? eventInfo.GetAddMethod().IsStatic : eventInfo.GetRemoveMethod().IsStatic;
                LuaAPI.lua_rawset(L, is_static ? cls_field : obj_field);
            }

            Dictionary<string, PropertyInfo> prop_map = new Dictionary<string, PropertyInfo>();
            List<PropertyInfo> items = new List<PropertyInfo>();
            PropertyInfo[] props = type.GetProperties(flag);
            for(int i = 0; i < props.Length; ++i)
            {
                PropertyInfo prop = props[i];
                if (prop.Name == "Item")
                {
                    items.Add(prop);
                }
                else
                {
                    prop_map.Add(prop.Name, prop);
                }
            }

            var item_array = items.ToArray();
            LuaCSFunction item_getter = item_array.Length > 0 ? genItemGetter(type, item_array) : null;
            LuaCSFunction item_setter = item_array.Length > 0 ? genItemSetter(type, item_array) : null; ;
            MethodInfo[] methods = type.GetMethods(flag);
            Dictionary<MethodKey, List<MemberInfo>> pending_methods = new Dictionary<MethodKey, List<MemberInfo>>();
            for (int i = 0; i < methods.Length; ++i)
            {
                MethodInfo method = methods[i];
                string method_name = method.Name;

                MethodKey method_key = new MethodKey { Name = method_name, IsStatic = method.IsStatic };
                List<MemberInfo> overloads;
                if (pending_methods.TryGetValue(method_key, out overloads))
                {
                    overloads.Add(method);
                    continue;
                }

                PropertyInfo prop = null;
                if (method_name.StartsWith("add_") || method_name.StartsWith("remove_") 
                    || method_name == "get_Item" || method_name == "set_Item")
                {
                    continue;
                }

                if (method_name.StartsWith("op_")) // 操作符
                {
                    if (support_op.ContainsKey(method_name))
                    {
                        if (overloads == null)
                        {
                            overloads = new List<MemberInfo>();
                            pending_methods.Add(method_key, overloads);
                        }
                        overloads.Add(method);
                    }
                    continue;
                }
                else if (method_name.StartsWith("get_") && prop_map.TryGetValue(method.Name.Substring(4), out prop)) // getter of property
                {
                    LuaAPI.lua_pushstring(L, prop.Name);
                    translator.PushAny(L, genPropGetter(type, prop, method.IsStatic));
                    LuaAPI.lua_rawset(L, method.IsStatic ? cls_getter : obj_getter);
                }
                else if (method_name.StartsWith("set_") && prop_map.TryGetValue(method.Name.Substring(4), out prop)) // setter of property
                {
                    LuaAPI.lua_pushstring(L, prop.Name);
                    translator.PushAny(L, genPropSetter(type, prop, method.IsStatic));
                    LuaAPI.lua_rawset(L, method.IsStatic ? cls_setter : obj_setter);
                }
                else if (method_name == ".ctor" && method.IsConstructor)
                {
                    continue;
                }
                else
                {
                    if (overloads == null)
                    {
                        overloads = new List<MemberInfo>();
                        pending_methods.Add(method_key, overloads);
                    }
                    overloads.Add(method);
                }
            }

            foreach (var kv in pending_methods)
            {
                if (kv.Key.Name.StartsWith("op_")) // 操作符
                {
                    LuaAPI.lua_pushstring(L, support_op[kv.Key.Name]);
                    translator.PushAny(L,
                        new LuaCSFunction(translator.methodWrapsCache._GenMethodWrap(type, kv.Key.Name, kv.Value.ToArray()).Call));
                    LuaAPI.lua_rawset(L, obj_meta);
                }
                else
                {
                    LuaAPI.lua_pushstring(L, kv.Key.Name);
                    translator.PushAny(L,
                        new LuaCSFunction(translator.methodWrapsCache._GenMethodWrap(type, kv.Key.Name, kv.Value.ToArray()).Call));
                    LuaAPI.lua_rawset(L, kv.Key.IsStatic ? cls_field : obj_field);
                }
            }

            Dictionary<string, List<MethodInfo>> extend_methods = GetExtensionMethodsOf(type);
            if (extend_methods != null)
            {
                foreach(var kv in extend_methods)
                {
                    LuaAPI.lua_pushstring(L, kv.Key);
                    translator.PushAny(L, new LuaCSFunction(translator.methodWrapsCache._GenMethodWrap(type, kv.Key, kv.Value.ToArray()).Call));
                    LuaAPI.lua_rawset(L, obj_field);
                }
            }

            // init obj metatable
            LuaAPI.lua_pushstring(L, "__gc");
            LuaAPI.lua_pushstdcallcfunction(L, translator.metaFunctions.GcMeta);
            LuaAPI.lua_rawset(L, obj_meta);

            LuaAPI.lua_pushstring(L, "__tostring");
            LuaAPI.lua_pushstdcallcfunction(L, translator.metaFunctions.ToStringMeta);
            LuaAPI.lua_rawset(L, obj_meta);

            LuaAPI.lua_pushstring(L, "__index");
            LuaAPI.lua_pushvalue(L, obj_field);
            LuaAPI.lua_pushvalue(L, obj_getter);
            translator.PushAny(L, item_getter);
            translator.PushAny(L, type.BaseType);
            LuaAPI.lua_pushstring(L, Utils.LuaIndexsFieldName);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
            LuaAPI.lua_pushnil(L);
            LuaAPI.gen_obj_indexer(L);
            //store in lua indexs function tables
            LuaAPI.lua_pushstring(L, Utils.LuaIndexsFieldName);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
            translator.Push(L, type);
            LuaAPI.lua_pushvalue(L, -3);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.lua_pop(L, 1);
            LuaAPI.lua_rawset(L, obj_meta); // set __index

            LuaAPI.lua_pushstring(L, "__newindex");
            LuaAPI.lua_pushvalue(L, obj_setter);
            translator.PushAny(L, item_setter);
            translator.Push(L, type.BaseType);
            LuaAPI.lua_pushstring(L, Utils.LuaNewIndexsFieldName);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
            LuaAPI.lua_pushnil(L);
            LuaAPI.gen_obj_newindexer(L);
            //store in lua newindexs function tables
            LuaAPI.lua_pushstring(L, Utils.LuaNewIndexsFieldName);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
            translator.Push(L, type);
            LuaAPI.lua_pushvalue(L, -3);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.lua_pop(L, 1);
            LuaAPI.lua_rawset(L, obj_meta); // set __newindex
            //finish init obj metatable

            LuaAPI.lua_pushstring(L, "UnderlyingSystemType");
            translator.PushAny(L, type);
            LuaAPI.lua_rawset(L, cls_field);

            if (type != null && type.IsEnum)
            {
                LuaAPI.lua_pushstring(L, "__CastFrom");
                translator.PushAny(L, genEnumCastFrom(type));
                LuaAPI.lua_rawset(L, cls_field);
            }

            //set cls_field to namespace
            string class_path = "CS" + ((type.Namespace == null) ? "" : ("." + type.Namespace));
            string class_name = type.ToString().Substring(type.Namespace == null ? 0 : type.Namespace.Length + 1);
            if (type.IsNested)
            {
                class_path += "." + class_name.Substring(0, class_name.IndexOf("+"));
                class_name = type.ToString().Substring(type.ToString().IndexOf('+') + 1);
            }

            NewTableInPath(L, class_path);
            LuaAPI.lua_pushstring(L, class_name);
            LuaAPI.lua_pushvalue(L, cls_field);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.lua_pop(L, 1);
            //finish set cls_field to namespace

            //init class meta
            LuaAPI.lua_pushstring(L, "__index");
            LuaAPI.lua_pushvalue(L, cls_getter);
            LuaAPI.lua_pushvalue(L, cls_field);
            translator.Push(L, type.BaseType);
            LuaAPI.lua_pushstring(L, Utils.LuaClassIndexsFieldName);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
            LuaAPI.gen_cls_indexer(L);
            //store in lua indexs function tables
            LuaAPI.lua_pushstring(L, Utils.LuaClassIndexsFieldName);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
            translator.Push(L, type);
            LuaAPI.lua_pushvalue(L, -3);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.lua_pop(L, 1);
            LuaAPI.lua_rawset(L, cls_meta); // set __index 

            LuaAPI.lua_pushstring(L, "__newindex");
            LuaAPI.lua_pushvalue(L, cls_setter);
            translator.Push(L, type.BaseType);
            LuaAPI.lua_pushstring(L, Utils.LuaClassNewIndexsFieldName);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
            LuaAPI.gen_cls_newindexer(L);
            //store in lua newindexs function tables
            LuaAPI.lua_pushstring(L, Utils.LuaClassNewIndexsFieldName);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
            translator.Push(L, type);
            LuaAPI.lua_pushvalue(L, -3);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.lua_pop(L, 1);
            LuaAPI.lua_rawset(L, cls_meta); // set __newindex

            LuaCSFunction constructor = translator.methodWrapsCache.GetConstructorWrap(type);
            if (constructor == null)
            {
                constructor = (RealStatePtr LL) =>
                {
                    return LuaAPI.luaL_error(LL, "No constructor for " + type);
                };
            }

            LuaAPI.lua_pushstring(L, "__call");
            translator.PushAny(L, constructor);
            LuaAPI.lua_rawset(L, cls_meta);

            LuaAPI.lua_pushvalue(L, cls_meta);
            LuaAPI.lua_setmetatable(L, cls_field);

            LuaAPI.lua_pop(L, 8);

            System.Diagnostics.Debug.Assert(top_enter == LuaAPI.lua_gettop(L));
        }

        public static void RegisterWrap(RealStatePtr L, Type type, Dictionary<string, LuaCSFunction> objMeta, LuaCSFunction creator, Dictionary<string, object> classFields,
            Dictionary<string, LuaCSFunction> methods = null, Dictionary<string, LuaCSFunction> getters = null, LuaCSFunction csIndexer = null,
            Dictionary<string, LuaCSFunction> setters = null, LuaCSFunction csNewIndexer = null, Dictionary<string, LuaCSFunction> static_getters = null,
            Dictionary<string, LuaCSFunction> static_setters = null, Type base_type = null, LuaCSFunction arrayIndexer = null, LuaCSFunction arrayNewIndexer = null)
        {
            ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
            //create obj meta table
            if (type == null)
            {
                LuaAPI.lua_newtable(L);
            }
            else
            {
                LuaAPI.luaL_getmetatable(L, type.FullName);
                if (LuaAPI.lua_isnil(L, -1))
                {
                    LuaAPI.lua_pop(L, 1);
                    LuaAPI.luaL_newmetatable(L, type.FullName);
                }
            }
            LuaAPI.lua_pushlightuserdata(L, LuaAPI.xlua_tag());
            LuaAPI.lua_pushnumber(L, 1);
            LuaAPI.lua_rawset(L, -3);

            if (objMeta != null)
            {
                foreach (KeyValuePair<string, LuaCSFunction> kv in objMeta)
                {
                    LuaAPI.lua_pushstring(L, kv.Key);
                    LuaAPI.lua_pushstdcallcfunction(L, kv.Value);
                    LuaAPI.lua_rawset(L, -3);
                }
            }

            if ((objMeta == null || !objMeta.ContainsKey("__gc")) && (type == null || !translator.HasCustomOp(type))) // no custom __gc and need gc
            {
                LuaAPI.lua_pushstring(L, "__gc");
                LuaAPI.lua_pushstdcallcfunction(L, translator.metaFunctions.GcMeta);
                LuaAPI.lua_rawset(L, -3);
            }

            if (objMeta == null || !objMeta.ContainsKey("__tostring"))
            {
                LuaAPI.lua_pushstring(L, "__tostring");
                LuaAPI.lua_pushstdcallcfunction(L, translator.metaFunctions.ToStringMeta);
                LuaAPI.lua_rawset(L, -3);
            }

            //if (methods != null || getters != null || csIndexer != null)
            {
                //LuaAPI.lua_getfield(L, LuaIndexes.LUA_REGISTRYINDEX, Utils.LuaIndexGeneratorName);
                LuaAPI.lua_pushstring(L, "__index");
                if (methods == null || methods.Count == 0)
                {
                    LuaAPI.lua_pushnil(L);
                }
                else
                {
                    LuaAPI.lua_newtable(L);
                    foreach (KeyValuePair<string, LuaCSFunction> kv in methods)
                    {
                        LuaAPI.lua_pushstring(L, kv.Key);
                        LuaAPI.lua_pushstdcallcfunction(L, kv.Value);
                        LuaAPI.lua_rawset(L, -3);
                    }
                }

                if (getters == null || getters.Count == 0)
                {
                    LuaAPI.lua_pushnil(L);
                }
                else
                {
                    LuaAPI.lua_newtable(L);
                    foreach (KeyValuePair<string, LuaCSFunction> kv in getters)
                    {
                        LuaAPI.lua_pushstring(L, kv.Key);
                        LuaAPI.lua_pushstdcallcfunction(L, kv.Value);
                        LuaAPI.lua_rawset(L, -3);
                    }
                }

                if (csIndexer == null)
                {
                    LuaAPI.lua_pushnil(L);
                }
                else
                {
                    LuaAPI.lua_pushstdcallcfunction(L, csIndexer);
                }

                translator.Push(L, type == null ? base_type : type.BaseType);

                LuaAPI.lua_pushstring(L, Utils.LuaIndexsFieldName);
                LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
                if (arrayIndexer == null)
                {
                    LuaAPI.lua_pushnil(L);
                }
                else
                {
                    LuaAPI.lua_pushstdcallcfunction(L, arrayIndexer);
                }

                //LuaAPI.lua_call(L, 5, 1);
                LuaAPI.gen_obj_indexer(L);

                if (type != null)
                {
                    LuaAPI.lua_pushstring(L, Utils.LuaIndexsFieldName);
                    LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);//store in lua indexs function tables
                    translator.Push(L, type);
                    LuaAPI.lua_pushvalue(L, -3);
                    LuaAPI.lua_rawset(L, -3);
                    LuaAPI.lua_pop(L, 1);
                }

                LuaAPI.lua_rawset(L, -3);
            }

            //if (setters != null || csNewIndexer != null)
            {
                //LuaAPI.lua_getfield(L, LuaIndexes.LUA_REGISTRYINDEX, Utils.LuaNewIndexGeneratorName);
                LuaAPI.lua_pushstring(L, "__newindex");
                if (setters == null || setters.Count == 0)
                {
                    LuaAPI.lua_pushnil(L);
                }
                else
                {
                    LuaAPI.lua_newtable(L);
                    foreach (KeyValuePair<string, LuaCSFunction> kv in setters)
                    {
                        LuaAPI.lua_pushstring(L, kv.Key);
                        LuaAPI.lua_pushstdcallcfunction(L, kv.Value);
                        LuaAPI.lua_rawset(L, -3);
                    }
                }

                if (csNewIndexer == null)
                {
                    LuaAPI.lua_pushnil(L);
                }
                else
                {
                    LuaAPI.lua_pushstdcallcfunction(L, csNewIndexer);
                }

                translator.Push(L, type == null ? base_type : type.BaseType);

                LuaAPI.lua_pushstring(L, Utils.LuaNewIndexsFieldName);
                LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);

                if (arrayNewIndexer == null)
                {
                    LuaAPI.lua_pushnil(L);
                }
                else
                {
                    LuaAPI.lua_pushstdcallcfunction(L, arrayNewIndexer);
                }

                //LuaAPI.lua_call(L, 4, 1);
                LuaAPI.gen_obj_newindexer(L);

                if (type != null)
                {
                    LuaAPI.lua_pushstring(L, Utils.LuaNewIndexsFieldName);
                    LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);//store in lua newindexs function tables
                    translator.Push(L, type);
                    LuaAPI.lua_pushvalue(L, -3);
                    LuaAPI.lua_rawset(L, -3);
                    LuaAPI.lua_pop(L, 1);
                }

                LuaAPI.lua_rawset(L, -3);
            }

            if (type != null)
            {
                LuaAPI.lua_pop(L, 1);

                LuaAPI.lua_createtable(L, 0, 0);

                int cls_table = LuaAPI.lua_gettop(L);

                foreach (KeyValuePair<string, object> kv in classFields)
                {
                    LuaAPI.lua_pushstring(L, kv.Key);
                    if (kv.Value is LuaCSFunction)
                    {
                        LuaAPI.lua_pushstdcallcfunction(L, kv.Value as LuaCSFunction);
                    }
                    else
                    {
                        translator.PushAny(L, kv.Value);
                    }
                    LuaAPI.lua_rawset(L, cls_table);
                }

                string class_path = "CS" + ((type.Namespace == null) ? "" : ("." + type.Namespace));
                string class_name = type.ToString().Substring(type.Namespace == null ? 0 : type.Namespace.Length + 1);
                if (type.IsNested)
                {
                    class_path += "." + class_name.Substring(0, class_name.IndexOf("+"));
                    class_name = type.ToString().Substring(type.ToString().IndexOf('+') + 1);
                }

                NewTableInPath(L, class_path);
                LuaAPI.lua_pushstring(L, class_name);
                LuaAPI.lua_pushvalue(L, cls_table);
                LuaAPI.lua_rawset(L, -3);
                LuaAPI.lua_pop(L, 1);

                LuaAPI.lua_newtable(L);
                if (creator != null)
                {
                    LuaAPI.lua_pushstring(L, "__call");
                    LuaAPI.lua_pushstdcallcfunction(L, creator);
                    LuaAPI.lua_rawset(L, -3);
                }

                //if (static_getters != null)
                {
                    //LuaAPI.lua_getfield(L, LuaIndexes.LUA_REGISTRYINDEX, Utils.LuaStaticIndexGeneratorName);
                    LuaAPI.lua_pushstring(L, "__index");

                    if (static_getters == null || static_getters.Count == 0)
                    {
                        LuaAPI.lua_pushnil(L);
                    }
                    else
                    {
                        LuaAPI.lua_newtable(L);
                        foreach (var kv in static_getters)
                        {
                            LuaAPI.lua_pushstring(L, kv.Key);
                            LuaAPI.lua_pushstdcallcfunction(L, kv.Value);
                            LuaAPI.lua_rawset(L, -3);
                        }
                    }

                    //LuaAPI.lua_call(L, 1, 1);
                    LuaAPI.lua_pushvalue(L, cls_table);
                    translator.Push(L, type.BaseType);
                    LuaAPI.lua_pushstring(L, Utils.LuaClassIndexsFieldName);
                    LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
                    LuaAPI.gen_cls_indexer(L);

                    LuaAPI.lua_pushstring(L, Utils.LuaClassIndexsFieldName);
                    LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);//store in lua indexs function tables
                    translator.Push(L, type);
                    LuaAPI.lua_pushvalue(L, -3);
                    LuaAPI.lua_rawset(L, -3);
                    LuaAPI.lua_pop(L, 1);

                    LuaAPI.lua_rawset(L, -3);
                }
                //if (static_setters != null)
                {
                    //LuaAPI.lua_getfield(L, LuaIndexes.LUA_REGISTRYINDEX, Utils.LuaStaticNewIndexGeneratorName);
                    LuaAPI.lua_pushstring(L, "__newindex");
                    if (static_setters == null || static_setters.Count == 0)
                    {
                        LuaAPI.lua_pushnil(L);
                    }
                    else
                    {
                        LuaAPI.lua_newtable(L);
                        foreach (var kv in static_setters)
                        {
                            LuaAPI.lua_pushstring(L, kv.Key);
                            LuaAPI.lua_pushstdcallcfunction(L, kv.Value);
                            LuaAPI.lua_rawset(L, -3);
                        }
                    }
                    //LuaAPI.lua_call(L, 1, 1);
                    translator.Push(L, type.BaseType);
                    LuaAPI.lua_pushstring(L, Utils.LuaClassNewIndexsFieldName);
                    LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
                    LuaAPI.gen_cls_newindexer(L);

                    LuaAPI.lua_pushstring(L, Utils.LuaClassNewIndexsFieldName);
                    LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);//store in lua newindexs function tables
                    translator.Push(L, type);
                    LuaAPI.lua_pushvalue(L, -3);
                    LuaAPI.lua_rawset(L, -3);
                    LuaAPI.lua_pop(L, 1);

                    LuaAPI.lua_rawset(L, -3);
                }
                LuaAPI.lua_setmetatable(L, -2);

                LuaAPI.lua_pop(L, 1);
            }
        }

        public static void RegisterEnumWrap(RealStatePtr L, Type type, LuaCSFunction cast_from)
        {
            Dictionary<string, object> classFields = new Dictionary<string, object>();
            foreach(var field_info in type.GetFields(BindingFlags.GetField | BindingFlags.Public | BindingFlags.Static))
            {
                classFields[field_info.Name] = field_info.GetValue(null);
            }
            classFields["UnderlyingSystemType"] = type;
            classFields["__CastFrom"] = cast_from;
            Utils.RegisterWrap(L, type, null, null, classFields);
        }

        public static void RegisterInterfaceBridge(RealStatePtr L, Type delegateType, Type bridgeType)
		{
			ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
            translator.DelayBridgeLoader(delegateType, () =>
            {
                translator.RegisterInterfaceBridge(delegateType, bridgeType);
            });
        }
        
        public static string LuaIndexsFieldName = "LuaIndexs";

        public static string LuaNewIndexsFieldName = "LuaNewIndexs";

        public static string LuaClassIndexsFieldName = "LuaClassIndexs";

        public static string LuaClassNewIndexsFieldName = "LuaClassNewIndexs";

        public static bool IsParamsMatch(MethodInfo delegateMethod, MethodInfo bridgeMethod)
        {
            if (delegateMethod == null || bridgeMethod == null)
            {
                return false;
            }
            if (delegateMethod.ReturnType != bridgeMethod.ReturnType)
            {
                return false;
            }
            ParameterInfo[] delegateParams = delegateMethod.GetParameters();
            ParameterInfo[] bridgeParams = bridgeMethod.GetParameters();
            if (delegateParams.Length != bridgeParams.Length)
            {
                return false;
            }

            for (int i = 0; i < delegateParams.Length; i++)
            {
                if (delegateParams[i].ParameterType != bridgeParams[i].ParameterType || delegateParams[i].IsOut != bridgeParams[i].IsOut)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

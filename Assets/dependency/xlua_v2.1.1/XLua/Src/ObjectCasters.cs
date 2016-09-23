#if USE_UNI_LUA
using LuaAPI = UniLua.Lua;
using RealStatePtr = UniLua.ILuaState;
using LuaCSFunction = UniLua.CSharpFunctionDelegate;
#else
using LuaAPI = LuaDLL.Lua;
using RealStatePtr = System.IntPtr;
using LuaCSFunction = LuaDLL.lua_CSFunction;
#endif

using System.Collections;
using System.Collections.Generic;
using System;
using System.Reflection;

namespace LuaInterface
{
    public delegate bool ObjectCheck(RealStatePtr L, int idx);

    public delegate object ObjectCast(RealStatePtr L, int idx, object target); // if target is null, will new one

    public class ObjectCheckers
    {
        Dictionary<Type, ObjectCheck> checkersMap = new Dictionary<Type, ObjectCheck>();
        ObjectTranslator translator;

        public ObjectCheckers(ObjectTranslator translator)
        {
            this.translator = translator;
            checkersMap[typeof(sbyte)] = numberCheck;
            checkersMap[typeof(byte)] = numberCheck;
            checkersMap[typeof(short)] = numberCheck;
            checkersMap[typeof(ushort)] = numberCheck;
            checkersMap[typeof(int)] = numberCheck;
            checkersMap[typeof(uint)] = numberCheck;
            checkersMap[typeof(long)] = int64Check;
            checkersMap[typeof(ulong)] = uint64Check;
            checkersMap[typeof(double)] = numberCheck;
            checkersMap[typeof(char)] = numberCheck;
            checkersMap[typeof(float)] = numberCheck;
            checkersMap[typeof(decimal)] = numberCheck;
            checkersMap[typeof(bool)] = boolCheck;
            checkersMap[typeof(string)] = strCheck;
            checkersMap[typeof(object)] = objectCheck;
            checkersMap[typeof(byte[])] = bytesCheck;
            checkersMap[typeof(IntPtr)] = intptrCheck;

            checkersMap[typeof(LuaTable)] = luaTableCheck;
            checkersMap[typeof(LuaFunction)] = luaFunctionCheck;
        }

        private static bool objectCheck(RealStatePtr L, int idx)
        {
            return true;
        }

        private bool luaTableCheck(RealStatePtr L, int idx)
        {
            return LuaAPI.lua_isnil(L, idx) || LuaAPI.lua_istable(L, idx) || (LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TUSERDATA && translator.SafeGetCSObj(L, idx) is LuaTable);
        }

        private bool numberCheck(RealStatePtr L, int idx)
        {
            return LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TNUMBER;
        }

        private bool strCheck(RealStatePtr L, int idx)
        {
            return LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TSTRING || LuaAPI.lua_isnil(L, idx);
        }

        private bool bytesCheck(RealStatePtr L, int idx)
        {
            return LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TSTRING || LuaAPI.lua_isnil(L, idx) || (LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TUSERDATA && translator.SafeGetCSObj(L, idx) is byte[]);
        }

        private bool boolCheck(RealStatePtr L, int idx)
        {
            return LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TBOOLEAN;
        }

        private bool int64Check(RealStatePtr L, int idx)
        {
            return LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TNUMBER || LuaAPI.lua_isint64(L, idx);
        }

        private bool uint64Check(RealStatePtr L, int idx)
        {
            return LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TNUMBER || LuaAPI.lua_isuint64(L, idx);
        }

        private bool luaFunctionCheck(RealStatePtr L, int idx)
        {
            return LuaAPI.lua_isnil(L, idx) || LuaAPI.lua_isfunction(L, idx) || (LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TUSERDATA && translator.SafeGetCSObj(L, idx) is LuaFunction);
        }

        private bool intptrCheck(RealStatePtr L, int idx)
        {
            return LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TLIGHTUSERDATA;
        }

        private ObjectCheck GenFixTypeCheck(Type type)
        {
            return (RealStatePtr L, int idx) =>
            {
                if (LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TUSERDATA)
                {
                    object obj = translator.SafeGetCSObj(L, idx);
                    if (obj != null)
                    {
                        return type.IsAssignableFrom(obj.GetType());
                    }
                    else
                    {
                        Type type_of_obj = translator.GetTypeOf(L, idx);
                        if (type_of_obj != null) return type.IsAssignableFrom(type_of_obj);
                    }
                }
                return false;
            };
        }

        public ObjectCheck GetChecker(Type type)
        {
            if (type.IsByRef) type = type.GetElementType();

            Type underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType != null)
            {
                type = underlyingType;     // Silently convert nullable types to their non null requics
            }
            ObjectCheck oc;
            if (!checkersMap.TryGetValue(type, out oc))
            {
                ObjectCheck fixTypeCheck = GenFixTypeCheck(type);
                if (!type.IsAbstract && typeof(Delegate).IsAssignableFrom(type) && translator.DelegateBridgeExisted(type))
                {
                    checkersMap[type] = (RealStatePtr L, int idx) =>
                    {
                        return LuaAPI.lua_isnil(L, idx) || LuaAPI.lua_isfunction(L, idx) || fixTypeCheck(L, idx);
                    };
                }
                else if (type.IsEnum)
                {
                    /*checkersMap[type] = (RealStatePtr L, int idx) =>
                    {
                        return LuaAPI.lua_isnumber(L, idx) || LuaAPI.lua_isstring(L, idx) || fixTypeCheck(L, idx);
                    };*/
                    //不支持字符串，数组到枚举的静默转换，这会影响到重载判断
                    checkersMap[type] = fixTypeCheck;
                }
				else if(type.IsInterface && translator.InterfaceBridgeExisted(type))
				{
					checkersMap[type] = (RealStatePtr L, int idx) =>
					{
						return LuaAPI.lua_isnil(L, idx) || LuaAPI.lua_istable(L, idx) || fixTypeCheck(L, idx);
					};
				}
                else
                {
                    if ((type.IsClass && type.GetConstructor(System.Type.EmptyTypes) != null) || type.IsValueType) //class has default construtor
                    {
                        checkersMap[type] = (RealStatePtr L, int idx) =>
                        {
                            return LuaAPI.lua_isnil(L, idx) || LuaAPI.lua_istable(L, idx) || fixTypeCheck(L, idx);
                        };
                    }
                    else if(type.IsArray)
                    {
                        checkersMap[type] = (RealStatePtr L, int idx) =>
                        {
                            return LuaAPI.lua_isnil(L, idx) || LuaAPI.lua_istable(L, idx) || fixTypeCheck(L, idx);
                        };
                    }
                    else
                    {
                        checkersMap[type] = (RealStatePtr L, int idx) =>
                        {
                            return LuaAPI.lua_isnil(L, idx) || fixTypeCheck(L, idx);
                        };
                    }
                }
                oc = checkersMap[type];
            }
            return oc;
        }
    }

    public class ObjectCasters
    {
        Dictionary<Type, ObjectCast> castersMap = new Dictionary<Type, ObjectCast>();
        ObjectTranslator translator;

        public ObjectCasters(ObjectTranslator translator)
        {
            this.translator = translator;
            castersMap[typeof(char)] = charCaster;
            castersMap[typeof(sbyte)] = sbyteCaster;
            castersMap[typeof(byte)] = byteCaster;
            castersMap[typeof(short)] = shortCaster;
            castersMap[typeof(ushort)] = ushortCaster;
            castersMap[typeof(int)] = intCaster;
            castersMap[typeof(uint)] = uintCaster;
            castersMap[typeof(long)] = longCaster;
            castersMap[typeof(ulong)] = ulongCaster;
            castersMap[typeof(double)] = getDouble;
            castersMap[typeof(float)] = floatCaster;
            castersMap[typeof(decimal)] = decimalCaster;
            castersMap[typeof(bool)] = getBoolean;
            castersMap[typeof(string)] =  getString;
            castersMap[typeof(object)] = getObject;
            castersMap[typeof(byte[])] = getBytes;
            castersMap[typeof(IntPtr)] = getIntptr;
            //special type
            castersMap[typeof(LuaTable)] = getLuaTable;
            castersMap[typeof(LuaFunction)] = getLuaFunction;
        }

        private static object charCaster(RealStatePtr L, int idx, object target)
        {
            return LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TNUMBER ? (object)(char)LuaAPI.lua_tonumber(L, idx) : null;
        }

        private static object sbyteCaster(RealStatePtr L, int idx, object target)
        {
            return LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TNUMBER ? (object)(sbyte)LuaAPI.lua_tonumber(L, idx) : null;
        }

        private static object byteCaster(RealStatePtr L, int idx, object target)
        {
            return LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TNUMBER ? (object)(byte)LuaAPI.lua_tonumber(L, idx) : null;
        }

        private static object shortCaster(RealStatePtr L, int idx, object target)
        {
            return LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TNUMBER ? (object)(short)LuaAPI.lua_tonumber(L, idx) : null;
        }

        private static object ushortCaster(RealStatePtr L, int idx, object target)
        {
            return LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TNUMBER ? (object)(ushort)LuaAPI.lua_tonumber(L, idx) : null;
        }

        private static object intCaster(RealStatePtr L, int idx, object target)
        {
            return LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TNUMBER ? (object)(int)LuaAPI.lua_tonumber(L, idx) : null;
        }

        private static object uintCaster(RealStatePtr L, int idx, object target)
        {
            return LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TNUMBER ? (object)(uint)LuaAPI.lua_tonumber(L, idx) : null;
        }

        private static object longCaster(RealStatePtr L, int idx, object target)
        {
            return (LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TNUMBER || LuaAPI.lua_isint64(L, idx)) ? (object)LuaAPI.lua_toint64(L, idx) : null;
        }

        private static object ulongCaster(RealStatePtr L, int idx, object target)
        {
            return (LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TNUMBER || LuaAPI.lua_isuint64(L, idx)) ? (object)LuaAPI.lua_touint64(L, idx) : null;
        }

        private static object getDouble(RealStatePtr L, int idx, object target)
        {
            return LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TNUMBER ? (object)LuaAPI.lua_tonumber(L, idx) : null;
        }

        private static object floatCaster(RealStatePtr L, int idx, object target)
        {
            return LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TNUMBER ? (object)(float)LuaAPI.lua_tonumber(L, idx) : null;
        }

        private static object decimalCaster(RealStatePtr L, int idx, object target)
        {
            return LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TNUMBER ? (object)(decimal)LuaAPI.lua_tonumber(L, idx) : null;
        }

        private static object getBoolean(RealStatePtr L, int idx, object target)
        {
            return LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TBOOLEAN ? (object)LuaAPI.lua_toboolean(L, idx) : null;
        }

        private static object getString(RealStatePtr L, int idx, object target)
        {
            return LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TSTRING ? LuaAPI.lua_tostring(L, idx) : null;
        }

        private object getBytes(RealStatePtr L, int idx, object target)
        {
            return LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TSTRING ? LuaAPI.lua_tobytes(L, idx) : translator.SafeGetCSObj(L, idx) as byte[];
        }

        private object getIntptr(RealStatePtr L, int idx, object target)
        {
            return LuaAPI.lua_touserdata(L, idx);
        }

        private object getObject(RealStatePtr L, int idx, object target)
        {
            //return translator.getObject(L, idx); //TODO: translator.getObject move to here??
            LuaTypes type = (LuaTypes)LuaAPI.lua_type(L, idx);
            switch (type)
            {
                case LuaTypes.LUA_TNUMBER:
                    {
                        return LuaAPI.lua_tonumber(L, idx);
                    }
                case LuaTypes.LUA_TSTRING:
                    {
                        return LuaAPI.lua_tostring(L, idx);
                    }
                case LuaTypes.LUA_TBOOLEAN:
                    {
                        return LuaAPI.lua_toboolean(L, idx);
                    }
                case LuaTypes.LUA_TTABLE:
                    {
                        return getLuaTable(L, idx, null);
                    }
                case LuaTypes.LUA_TFUNCTION:
                    {
                        return getLuaFunction(L, idx, null);
                    }
                case LuaTypes.LUA_TUSERDATA:
                    {
                        if (LuaAPI.lua_isint64(L, idx))
                        {
                            return LuaAPI.lua_toint64(L, idx);
                        }
                        else if(LuaAPI.lua_isuint64(L, idx))
                        {
                            return LuaAPI.lua_touint64(L, idx);
                        }
                        else
                        {
                            return translator.SafeGetCSObj(L, idx);
                        }
                    }
                default:
                    return null;
            }
        }

        private object getLuaTable(RealStatePtr L, int idx, object target)
        {
            if (LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TUSERDATA)
            {
                object obj = translator.SafeGetCSObj(L, idx);
                return (obj != null && obj is LuaTable) ? obj : null;
            }
            if (!LuaAPI.lua_istable(L, idx))
            {
                return null;
            }
            LuaAPI.lua_pushvalue(L, idx);
            return new LuaTable(LuaAPI.luaL_ref(L), translator.interpreter);
        }

        private object getLuaFunction(RealStatePtr L, int idx, object target)
        {
            if (LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TUSERDATA)
            {
                object obj = translator.SafeGetCSObj(L, idx);
                return (obj != null && obj is LuaFunction) ? obj : null;
            }
            if (!LuaAPI.lua_isfunction(L, idx))
            {
                return null;
            }
            LuaAPI.lua_pushvalue(L, idx);
            return new LuaFunction(LuaAPI.luaL_ref(L), translator.interpreter);
        }

        private ObjectCast GenFixTypeGetter(Type type)
        {
            return (RealStatePtr L, int idx, object target) =>
            {
                if (LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TUSERDATA)
                {
                    object obj = translator.SafeGetCSObj(L, idx);
                    //UnityEngine.Debug.Log("call fix caster for " + obj);
                    return (obj != null && type.IsAssignableFrom(obj.GetType())) ? obj : null;
                    //return translator.FastGetCSObj(L, idx);
                }
                return null;
            };
        }

        public ObjectCast GetCaster(Type type)
        {
            if (type.IsByRef) type = type.GetElementType();

            Type underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType != null)
            {
                type = underlyingType;     // Silently convert nullable types to their non null requics
            }
            ObjectCast oc;
            if (!castersMap.TryGetValue(type, out oc))
            {
                //UnityEngine.Debug.LogWarning("gen caster for " + type);
                ObjectCast fixTypeGetter = GenFixTypeGetter(type);

                if (typeof(Delegate).IsAssignableFrom(type))
                {
                    castersMap[type] = (RealStatePtr L, int idx, object target) =>
                    {
                        object obj = fixTypeGetter(L, idx, target);
                        if (obj != null) return obj;

                        if (!LuaAPI.lua_isfunction(L, idx))
                        {
                            return null;
                        }
                        
                        return translator.CreateDelegateBridge(L, type, idx);
                    };
                }
				else if (type.IsInterface)
                {
                    castersMap[type] = (RealStatePtr L, int idx, object target) =>
                    {
                        object obj = fixTypeGetter(L, idx, target);
                        if (obj != null) return obj;

                        if (!LuaAPI.lua_istable(L, idx))
                        {
                            return null;
                        }
						return translator.CreateInterfaceBridge(L, type, idx);
                    };
                }
                else if (type.IsEnum)
                {
                    castersMap[type] = (RealStatePtr L, int idx, object target) =>
                    {
                        object obj = fixTypeGetter(L, idx, target);
                        if (obj != null) return obj;

                        if (LuaAPI.lua_isstring(L, idx))
                        {
                            return Enum.Parse(type, LuaAPI.lua_tostring(L, idx));
                        }
                        else if (LuaAPI.lua_isnumber(L, idx))
                        {
                            return Enum.ToObject(type, LuaAPI.lua_tointeger(L, idx));
                        }
                        
                        return null;
                    };
                }
                else if (type.IsArray)
                {
                    castersMap[type] = (RealStatePtr L, int idx, object target) =>
                    {
                        object obj = fixTypeGetter(L, idx, target);
                        if (obj != null) return obj;

                        if (!LuaAPI.lua_istable(L, idx))
                        {
                            return null;
                        }

                        uint len = LuaAPI.lua_objlen(L, idx);
                        int n = LuaAPI.lua_gettop(L);
                        Type et = type.GetElementType();
                        ObjectCast elementCaster = GetCaster(et);
                        Array ary = target == null ? Array.CreateInstance(et, len) : target as Array;
                        if (!LuaAPI.lua_checkstack(L, 1))
                        {
                            throw new Exception("stack overflow while cast to Array");
                        }
                        for (int i = 0; i < len; ++i)
                        {
                            LuaAPI.lua_pushnumber(L, i + 1);
                            LuaAPI.lua_rawget(L, idx);

                            ary.SetValue(elementCaster(L, n + 1, target == null || et.IsPrimitive || et == typeof(string) || ary.Length <= i ? null : ary.GetValue(i)), i);
                            LuaAPI.lua_pop(L, 1);
                        }
                        return ary;
                    };
                }
                else if (typeof(IList).IsAssignableFrom(type) && type.IsGenericType)
                {
                    ObjectCast elementCaster = GetCaster(type.GetGenericArguments()[0]);

                    castersMap[type] = (RealStatePtr L, int idx, object target) =>
                    {
                        object obj = fixTypeGetter(L, idx, target);
                        if (obj != null) return obj;

                        if (!LuaAPI.lua_istable(L, idx))
                        {
                            return null;
                        }

                        obj = target == null ? Activator.CreateInstance(type) : target;
                        int n = LuaAPI.lua_gettop(L);
                        IList list = obj as IList;
                        

                        uint len = LuaAPI.lua_objlen(L, n);
                        if (!LuaAPI.lua_checkstack(L, 1))
                        {
                            throw new Exception("stack overflow while cast to IList");
                        }
                        for (int i = 0; i < len; ++i)
                        {
                            LuaAPI.lua_pushnumber(L, i + 1);
                            LuaAPI.lua_rawget(L, n);
                            if (i < list.Count && target != null)
                            {
                                var item = elementCaster(L, n + 1, list[i]);
                                if (item != null)
                                {
                                    list[i] = item;
                                }
                            }
                            else
                            {
                                var item = elementCaster(L, n + 1, null);
                                if (item != null)
                                {
                                    list.Add(item);
                                }
                            }
                            LuaAPI.lua_pop(L, 1);
                        }
                        return obj;
                    };
                }
                else if (typeof(IDictionary).IsAssignableFrom(type) && type.IsGenericType)
                {
                    ObjectCast keyCaster = GetCaster(type.GetGenericArguments()[0]);
                    ObjectCast valueCaster = GetCaster(type.GetGenericArguments()[1]);

                    castersMap[type] = (RealStatePtr L, int idx, object target) =>
                    {
                        object obj = fixTypeGetter(L, idx, target);
                        if (obj != null) return obj;

                        if (!LuaAPI.lua_istable(L, idx))
                        {
                            return null;
                        }

                        IDictionary dic = (target == null ? Activator.CreateInstance(type) : target) as IDictionary;
                        int n = LuaAPI.lua_gettop(L);

                        LuaAPI.lua_pushnil(L);
                        if (!LuaAPI.lua_checkstack(L, 1))
                        {
                            throw new Exception("stack overflow while cast to IDictionary");
                        }
                        while (LuaAPI.lua_next(L, n) != 0)
                        {
                            object k = keyCaster(L, n + 1, null); // -2:key
                            if (k != null)
                            {
                                object v = valueCaster(L, n + 2, !dic.Contains(k) ? null : dic[k]);
                                if (v != null)
                                {
                                    dic[k] = v; // -1:value
                                }
                            }
                            LuaAPI.lua_pop(L, 1); // removes value, keeps key for next iteration
                        }
                        return dic;
                    };
                }
                else if ((type.IsClass && type.GetConstructor(System.Type.EmptyTypes) != null ) || type.IsValueType) //class has default construtor
                {
                    castersMap[type] = (RealStatePtr L, int idx, object target) =>
                    {
                        object obj = fixTypeGetter(L, idx, target);
                        if (obj != null) return obj;

                        if (!LuaAPI.lua_istable(L, idx))
                        {
                            return null;
                        }

                        obj = target == null ? Activator.CreateInstance(type) : target;

                        int n = LuaAPI.lua_gettop(L);
                        idx = idx > 0 ? idx : LuaAPI.lua_gettop(L) + idx + 1;// abs of index
                        if (!LuaAPI.lua_checkstack(L, 1))
                        {
                            throw new Exception("stack overflow while cast to " + type);
                        }
                        foreach (PropertyInfo prop in type.GetProperties())
                        {
                            LuaAPI.lua_pushstring(L, prop.Name);
                            LuaAPI.lua_rawget(L, idx);
                            if (!LuaAPI.lua_isnil(L, -1))
                            {
                                try
                                {
                                    prop.SetValue(obj, GetCaster(prop.PropertyType)(L, n + 1,
                                        target == null || prop.PropertyType.IsPrimitive || prop.PropertyType == typeof(string) ? null : prop.GetValue(obj, null)), null);
                                }
                                catch (Exception e)
                                {
                                    throw new Exception("exception in tran " + prop.Name + ", msg=" + e.Message);
                                }
                            }
                            LuaAPI.lua_pop(L, 1);
                        }
                        foreach (FieldInfo field in type.GetFields())
                        {
                            LuaAPI.lua_pushstring(L, field.Name);
                            LuaAPI.lua_rawget(L, idx);
                            if (!LuaAPI.lua_isnil(L, -1))
                            {
                                try
                                {
                                    field.SetValue(obj, GetCaster(field.FieldType)(L, n + 1,
                                            target == null || field.FieldType.IsPrimitive || field.FieldType == typeof(string) ? null : field.GetValue(obj)));
                                }
                                catch (Exception e)
                                {
                                    throw new Exception("exception in tran " + field.Name + ", msg=" + e.Message);
                                }
                            }
                            LuaAPI.lua_pop(L, 1);
                        }

                        return obj;
                    };
                }
                else
                {
                    castersMap[type] = fixTypeGetter;
                }
                oc = castersMap[type];
            }
            return oc;
        }
    }
}

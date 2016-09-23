﻿#if USE_UNI_LUA
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
    public class OverloadMethodWrap
    {
        ObjectTranslator translator;
        Type targetType;
        MethodBase method;

        ObjectCheck[] checkArray;
        ObjectCast[] castArray;

        int[] inPosArray;
        int[] outPosArray;

        bool[] isOptionalArray;

        object[] defaultValueArray;

        bool isVoid = true;

        int luaStackPosStart = 1;

        bool targetNeeded = false;

        object[] args;

        int[] refPos;

        Type paramsType = null;

        public bool HasDefalutValue{ get; private set; }

        public OverloadMethodWrap(ObjectTranslator translator, Type targetType, MethodBase method)
        {
            this.translator = translator;
            this.targetType = targetType;
            this.method = method;
            HasDefalutValue = false;
        }

        public void Init(ObjectCheckers objCheckers, ObjectCasters objCasters)
        {
            if (typeof(Delegate).IsAssignableFrom(targetType) || !method.IsStatic || method.IsConstructor)
            {
                luaStackPosStart = 2;
                if (!method.IsConstructor)
                {
                    targetNeeded = true;
                }
            }

            var paramInfos = method.GetParameters();
            refPos = new int[paramInfos.Length];

            List<int> inPosList = new List<int>();
            List<int> outPosList = new List<int>();

            List<ObjectCheck> paramsChecks = new List<ObjectCheck>();
            List<ObjectCast> paramsCasts = new List<ObjectCast>();
            List<bool> isOptionalList = new List<bool>();
            List<object> defaultValueList = new List<object>();

            for(int i = 0; i < paramInfos.Length; i++)
            {
                refPos[i] = -1;
                if (!paramInfos[i].IsIn && paramInfos[i].IsOut)  // out parameter
				{
					outPosList.Add(i);
				}
                else
                {
                    if(paramInfos[i].ParameterType.IsByRef)
                    {
                        var ttype = paramInfos[i].ParameterType.GetElementType();
                        if(CopyByValue.IsStruct(ttype) && ttype != typeof(Decimal))
                        {
                            refPos[i] = inPosList.Count;
                        }
                        outPosList.Add(i);
                    }

                    inPosList.Add(i);
                    var paramType = paramInfos[i].IsDefined(typeof(ParamArrayAttribute), false) || (!paramInfos[i].ParameterType.IsArray && paramInfos[i].ParameterType.IsByRef ) ? 
                        paramInfos[i].ParameterType.GetElementType() : paramInfos[i].ParameterType;
                    paramsChecks.Add (objCheckers.GetChecker(paramType));
                    paramsCasts.Add (objCasters.GetCaster(paramType));
                    isOptionalList.Add(paramInfos[i].IsOptional);
                    var defalutValue = paramInfos[i].DefaultValue;
                    if (paramInfos[i].IsOptional)
                    {
                        if (defalutValue != null && defalutValue.GetType() != paramInfos[i].ParameterType)
                        {
                            defalutValue = Convert.ChangeType(defalutValue, paramInfos[i].ParameterType);
                        }
                        HasDefalutValue = true;
                    }
                    defaultValueList.Add(paramInfos[i].IsOptional ? defalutValue : null);
                }
            }
            checkArray = paramsChecks.ToArray();
            castArray = paramsCasts.ToArray();
            inPosArray = inPosList.ToArray();
            outPosArray = outPosList.ToArray();
            isOptionalArray = isOptionalList.ToArray();
            defaultValueArray = defaultValueList.ToArray();

            if (paramInfos.Length > 0 && paramInfos[paramInfos.Length - 1].IsDefined(typeof(ParamArrayAttribute), false))
            {
                paramsType = paramInfos[paramInfos.Length - 1].ParameterType.GetElementType();
            }

            args = new object[paramInfos.Length];

            if (method is MethodInfo) //constructor is not MethodInfo?
            {
                isVoid = (method as MethodInfo).ReturnType == typeof(void);
            }
            else if(method is ConstructorInfo)
            {
                isVoid = false;
            }
        }

        public bool Check(RealStatePtr L)
        {
            int luaTop = LuaAPI.lua_gettop(L);
            int luaStackPos = luaStackPosStart;
            int default_count = 0;

            for(int i = 0; i < checkArray.Length; i++)
            {
                if ((i == (checkArray.Length - 1)) && (paramsType != null))
                {
                    break;
                }
                if(luaStackPos > luaTop && !isOptionalArray[i])
                {
                    return false;
                }
                else if(luaStackPos <= luaTop && !checkArray[i](L, luaStackPos))
                {
                    return false;
                }
                else
                {
                    if (luaStackPos > luaTop && isOptionalArray[i]) default_count++;
                    luaStackPos++;
                }
            }
            return paramsType != null ? luaStackPos <= luaTop + 1 + default_count : luaStackPos == luaTop + 1 + default_count;
        }

        public int Call(RealStatePtr L)
        {
            object target = null;
            MethodBase toInvoke = method;

            if (luaStackPosStart > 1)
            {
                target = translator.FastGetCSObj(L, 1);
                if(target is Delegate)
                {
                    Delegate delegateInvoke = (Delegate)target;
                    toInvoke = delegateInvoke.Method;
                }
            }


            int luaTop = LuaAPI.lua_gettop(L);
            int luaStackPos = luaStackPosStart;

            for(int i = 0; i < castArray.Length; i++)
            {
                //UnityEngine.Debug.Log("inPos:" + inPosArray[i]);
                if (luaStackPos > luaTop) //after check
                {
                    args[inPosArray[i]] = defaultValueArray[i];
                }
                else
                {
                    if (paramsType != null && i == castArray.Length - 1)
                    {
                        args[inPosArray[i]] = translator.GetParams(L, luaStackPos, paramsType);
                    }
                    else
                    {
                        args[inPosArray[i]] = castArray[i](L, luaStackPos, null);
                    }
                    luaStackPos++;
                }
                //UnityEngine.Debug.Log("value:" + args[inPosArray[i]]);
            }
            
            object ret = null;

            try
            {
                ret = toInvoke.IsConstructor ? ((ConstructorInfo)method).Invoke(args) : method.Invoke(targetNeeded ? target : null, args);
            }
            catch (System.Exception e)
            {
                return LuaAPI.luaL_error(L, "c# exception:" + e.Message + ",stack:" + e.StackTrace);
            }

            int nRet = 0;

            if(!isVoid)
            {
                //UnityEngine.Debug.Log(toInvoke.ToString() + " ret:" + ret);
                translator.PushAny(L, ret);
                nRet++;
            }

            for (int i = 0; i < outPosArray.Length; i++)
            {
                if (refPos[outPosArray[i]] != -1)
                {
                    translator.Update(L, luaStackPosStart + refPos[outPosArray[i]], args[outPosArray[i]]);
                }
                translator.PushAny(L, args[outPosArray[i]]);
                nRet++;
            }
            
            return nRet;
        }
    }

    public class MethodWrap
    {
        private string methodName;
        private List<OverloadMethodWrap> overloads = new List<OverloadMethodWrap>();

        public MethodWrap(string methodName, List<OverloadMethodWrap> overloads)
        {
            this.methodName = methodName;
            this.overloads = overloads;
        }

        public int Call(RealStatePtr L)
        {
            if (overloads.Count == 1 && !overloads[0].HasDefalutValue) return overloads[0].Call(L);

            for (int i = 0; i < overloads.Count; ++i)
            {
                var overload = overloads[i];
                if (overload.Check(L))
                {
                    return overload.Call(L);
                }
            }
            return LuaAPI.luaL_error(L, "invalid arguments to " + methodName);
        }
    }

    public class MethodWrapsCache
    {
        ObjectTranslator translator;
        ObjectCheckers objCheckers;
        ObjectCasters objCasters;

        Dictionary<Type, LuaCSFunction> constructorCache = new Dictionary<Type, LuaCSFunction>();
        Dictionary<Type, Dictionary<string, LuaCSFunction>> methodsCache = new Dictionary<Type, Dictionary<string, LuaCSFunction>>();
        Dictionary<Type, LuaCSFunction> delegateCache = new Dictionary<Type, LuaCSFunction>();

        public MethodWrapsCache(ObjectTranslator translator, ObjectCheckers objCheckers, ObjectCasters objCasters)
        {
            this.translator = translator;
            this.objCheckers = objCheckers;
            this.objCasters = objCasters;
        }

        public LuaCSFunction GetConstructorWrap(Type type)
        {
            //UnityEngine.Debug.LogWarning("GetConstructor:" + type);
            if (!constructorCache.ContainsKey(type))
            {
                var constructors = type.GetConstructors();
                if (type.IsAbstract || constructors == null || constructors.Length == 0)
                {
                    if (type.IsValueType)
                    {
                        constructorCache[type] = (L) =>
                        {
                            translator.PushAny(L, Activator.CreateInstance(type));
                            return 1;
                        };
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    constructorCache[type] = _GenMethodWrap(type, ".ctor", constructors).Call;
                }
            }
            return constructorCache[type];
        }

        public LuaCSFunction GetMethodWrap(Type type, string methodName)
        {
            //UnityEngine.Debug.LogWarning("GetMethodWrap:" + type + " " + methodName);
            if (!methodsCache.ContainsKey(type))
            {
                methodsCache[type] = new Dictionary<string, LuaCSFunction>();
            }
            var methodsOfType = methodsCache[type];
            if (!methodsOfType.ContainsKey(methodName))
            {
                MemberInfo[] methods = type.GetMember(methodName, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (methods == null || methods.Length == 0 || methods[0].MemberType != MemberTypes.Method)
                {
                    return null;
                }
                methodsOfType[methodName] = _GenMethodWrap(type, methodName, methods).Call;
            }
            return methodsOfType[methodName];
        }

        public LuaCSFunction GetMethodWrapInCache(Type type, string methodName)
        {
            //string retriKey = type.ToString() + "." + methodName;
            //return methodsCache.ContainsKey(retriKey) ? methodsCache[retriKey] : null;
            if (!methodsCache.ContainsKey(type))
            {
                methodsCache[type] = new Dictionary<string, LuaCSFunction>();
            }
            var methodsOfType = methodsCache[type];
            return methodsOfType.ContainsKey(methodName) ? methodsOfType[methodName] : null;
        }

        public LuaCSFunction GetDelegateWrap(Type type)
        {
            //UnityEngine.Debug.LogWarning("GetDelegateWrap:" + type );
            if (!typeof(Delegate).IsAssignableFrom(type))
            {
                return null;
            }
            if (!delegateCache.ContainsKey(type))
            {
                delegateCache[type] = _GenMethodWrap(type, type.ToString(), new MethodBase[] { type.GetMethod("Invoke") }).Call;
            }
            return delegateCache[type];
        }

        public LuaCSFunction GetEventWrap(Type type, string eventName)
        {
            if (!methodsCache.ContainsKey(type))
            {
                methodsCache[type] = new Dictionary<string, LuaCSFunction>();
            }

            var methodsOfType = methodsCache[type];
            if (!methodsOfType.ContainsKey(eventName))
            {
                /*FieldInfo fi = type.GetField(eventName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Static);
                if (fi != null)
                {
                    methodsOfType[eventName] = (L) =>
                    {
                        object obj = null;
                        int start_idx = 0;

                        if (!fi.IsStatic)
                        {
                            obj = translator.getAsType(L, 1, type);
                            if (obj == null)
                            {
                                return LuaAPI.luaL_error(L, "invalid #1, needed:" + type);
                            }
                            start_idx = 1;
                        }

                        try
                        {
                            Delegate handlerDelegate = translator.CreateDelegateBridge(L, fi.FieldType, start_idx + 2);
                            if (handlerDelegate == null)
                            {
                                return LuaAPI.luaL_error(L, "invalid #" + (start_idx + 2) + ", needed:" + fi.FieldType);
                            }
                            Delegate orgDelegate = fi.GetValue(obj) as Delegate;
                            switch (LuaAPI.lua_tostring(L, start_idx + 1))
                            {
                                case "+":
                                    fi.SetValue(obj, orgDelegate == null ? handlerDelegate : Delegate.Combine(orgDelegate, handlerDelegate));
                                    break;
                                case "-":
                                    fi.SetValue(obj, orgDelegate == null ? null : Delegate.Remove(orgDelegate, handlerDelegate));
                                    break;
                                default:
                                    return LuaAPI.luaL_error(L, "invalid #" + (start_idx + 1) + ", needed: '+' or '-'" + fi.FieldType);
                            }
                        }
                        catch (System.Exception e)
                        {
                            return LuaAPI.luaL_error(L, "c# exception:" + e + ",stack:" + e.StackTrace);
                        }
                        return 0;
                    };
                }
                else*/
                {
                    EventInfo eventInfo = type.GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Static);
                    if (eventInfo == null)
                    {
                        throw new Exception(type.Name + " has no event named: " + eventName);
                    }
                    int start_idx = 0;

                    MethodInfo add = eventInfo.GetAddMethod();
                    MethodInfo remove = eventInfo.GetRemoveMethod();

                    bool is_static = add != null ? add.IsStatic : remove.IsStatic;
                    if (!is_static) start_idx = 1;

                    methodsOfType[eventName] = (L) =>
                    {
                        object obj = null;

                        if (!is_static)
                        {
                            obj = translator.GetObject(L, 1, type);
                            if (obj == null)
                            {
                                return LuaAPI.luaL_error(L, "invalid #1, needed:" + type);
                            }
                        }

                        try
                        {
                            Delegate handlerDelegate = translator.CreateDelegateBridge(L, eventInfo.EventHandlerType, start_idx + 2);
                            if (handlerDelegate == null)
                            {
                                return LuaAPI.luaL_error(L, "invalid #" + (start_idx + 2) + ", needed:" + eventInfo.EventHandlerType);
                            }
                            switch (LuaAPI.lua_tostring(L, start_idx + 1))
                            {
                                case "+":
                                    if (add == null)
                                    {
                                        return LuaAPI.luaL_error(L, "no add for event " + eventName);
                                    }
                                    add.Invoke(obj, new object[] { handlerDelegate });
                                    break;
                                case "-":
                                    if (remove == null)
                                    {
                                        return LuaAPI.luaL_error(L, "no remove for event " + eventName);
                                    }
                                    remove.Invoke(obj, new object[] { handlerDelegate });
                                    break;
                                default:
                                    return LuaAPI.luaL_error(L, "invalid #" + (start_idx + 1) + ", needed: '+' or '-'" + eventInfo.EventHandlerType);
                            }
                        }
                        catch (System.Exception e)
                        {
                            return LuaAPI.luaL_error(L, "c# exception:" + e + ",stack:" + e.StackTrace);
                        }

                        return 0;
                    };
                }
            }
            return methodsOfType[eventName];
        }

        public MethodWrap _GenMethodWrap(Type type, string methodName, MemberInfo[] methodBases)
        { 
            List<OverloadMethodWrap> overloads = new List<OverloadMethodWrap>();
            foreach(var methodBase in methodBases)
            {
                if (methodBase is MethodBase && !(methodBase as MethodBase).ContainsGenericParameters)
                {
                    var overload = new OverloadMethodWrap(translator, type, methodBase as MethodBase);
                    overload.Init(objCheckers, objCasters);
                    overloads.Add(overload);
                }
            }
            return new MethodWrap(methodName, overloads);
        }
    }
}
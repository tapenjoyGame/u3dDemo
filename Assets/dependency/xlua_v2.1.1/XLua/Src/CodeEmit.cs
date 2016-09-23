#if UNITY_EDITOR
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using LuaInterface;
using System.Linq;
using System.Reflection;
using System;

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
    public class CodeEmit
    {
        private ModuleBuilder codeEmitModule = null;
        private ulong genID = 0;

        private MethodInfo LuaEnv_ThrowExceptionFromError = typeof(LuaEnv).GetMethod("ThrowExceptionFromError");
        private FieldInfo LuaBase_Interpreter = typeof(LuaBase).GetField("_Interpreter");
        private MethodInfo LuaAPI_load_error_func = typeof(LuaAPI).GetMethod("load_error_func");
        private FieldInfo LuaEnv_translator  = typeof(LuaEnv).GetField("translator");
        private FieldInfo LuaBase_Reference = typeof(LuaBase).GetField("_Reference");
        private MethodInfo LuaAPI_lua_getref = typeof(LuaAPI).GetMethod("lua_getref");
        private MethodInfo Type_GetTypeFromHandle = typeof(Type).GetMethod("GetTypeFromHandle", new Type[] { typeof(RuntimeTypeHandle) });
        private MethodInfo ObjectTranslator_PushAny = typeof(ObjectTranslator).GetMethod("PushAny");
        private FieldInfo LuaEnv_L = typeof(LuaEnv).GetField("L");
        private MethodInfo LuaAPI_lua_pcall = typeof(LuaAPI).GetMethod("lua_pcall");
        private MethodInfo ObjectTranslator_GetObject = typeof(ObjectTranslator).GetMethod("GetObject", new Type[] { typeof(RealStatePtr),
               typeof(int), typeof(Type)});
        private MethodInfo LuaAPI_lua_pushvalue = typeof(LuaAPI).GetMethod("lua_pushvalue");
        private MethodInfo LuaAPI_lua_remove = typeof(LuaAPI).GetMethod("lua_remove");
        private MethodInfo LuaAPI_lua_pushstring = typeof(LuaAPI).GetMethod("lua_pushstring", new Type[] { typeof(RealStatePtr), typeof(string)});
        private MethodInfo LuaAPI_lua_gettop = typeof(LuaAPI).GetMethod("lua_gettop");
        private MethodInfo LuaAPI_xlua_pgettable = typeof(LuaAPI).GetMethod("xlua_pgettable");
        private MethodInfo LuaAPI_xlua_psettable = typeof(LuaAPI).GetMethod("xlua_psettable");
        private MethodInfo LuaAPI_lua_pop = typeof(LuaAPI).GetMethod("lua_pop");
        private MethodInfo LuaAPI_lua_settop = typeof(LuaAPI).GetMethod("lua_settop");

        public CodeEmit()
        {
            
        }

        private ModuleBuilder CodeEmitModule
        {
            get
            {
                if (codeEmitModule == null)
                {
                    var assemblyName = new AssemblyName();
                    assemblyName.Name = "XLuaCodeEmit";
                    codeEmitModule = AppDomain.CurrentDomain.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run)
                        .DefineDynamicModule("XLuaCodeEmit");
                }
                return codeEmitModule;
            }
        }

        public Type EmitDelegateImpl(IEnumerable<MethodInfo> to_be_impls)
        {
            TypeBuilder impl_type_builder = CodeEmitModule.DefineType("XLuaGenDelegateImpl" + (genID++), TypeAttributes.Public, typeof(DelegateBridge));

            foreach (var to_be_impl in to_be_impls)
            {
                var parameters = to_be_impl.GetParameters();

                Type[] param_types = new Type[parameters.Length];
                for (int i = 0; i < parameters.Length; ++i)
                {
                    param_types[i] = parameters[i].ParameterType;
                }

                var method_builder = impl_type_builder.DefineMethod("Invoke" + (genID++), to_be_impl.Attributes, to_be_impl.ReturnType, param_types);
                for (int i = 0; i < parameters.Length; ++i)
                {
                    method_builder.DefineParameter(i + 1, parameters[i].Attributes, parameters[i].Name);
                }

                ILGenerator g = method_builder.GetILGenerator();
                g.DeclareLocal(typeof(RealStatePtr));//RealStatePtr L;  0
                g.DeclareLocal(typeof(int));//int err_func; 1
                g.DeclareLocal(typeof(ObjectTranslator));//ObjectTranslator translator; 2
                bool has_return = to_be_impl.ReturnType != typeof(void);
                if (has_return)
                {
                    g.DeclareLocal(to_be_impl.ReturnType); //ReturnType ret; 3
                }

                // L = _Interpreter.L;
                g.Emit(OpCodes.Ldarg_0);
                g.Emit(OpCodes.Ldfld, LuaBase_Interpreter);
                g.Emit(OpCodes.Ldfld, LuaEnv_L);
                g.Emit(OpCodes.Stloc_0);

                //err_func =LuaAPI.load_error_func(L);
                g.Emit(OpCodes.Ldloc_0);
                g.Emit(OpCodes.Call, LuaAPI_load_error_func);
                g.Emit(OpCodes.Stloc_1);

                //translator = _Interpreter.translator;
                g.Emit(OpCodes.Ldarg_0);
                g.Emit(OpCodes.Ldfld, LuaBase_Interpreter);
                g.Emit(OpCodes.Ldfld, LuaEnv_translator);
                g.Emit(OpCodes.Stloc_2);

                //LuaAPI.lua_getref(L, _Reference);
                g.Emit(OpCodes.Ldloc_0);
                g.Emit(OpCodes.Ldarg_0);
                g.Emit(OpCodes.Ldfld, LuaBase_Reference);
                g.Emit(OpCodes.Call, LuaAPI_lua_getref);

                int in_param_count = 0;
                int out_param_count = 0;
                //translator.PushAny(L, param_in)
                for (int i = 0; i < parameters.Length; ++i)
                {
                    var pinfo = parameters[i];
                    if (!pinfo.IsOut)
                    {
                        var ptype = pinfo.ParameterType;
                        var pelemtype = ptype.IsByRef ? ptype.GetElementType() : ptype;

                        g.Emit(OpCodes.Ldloc_2);
                        g.Emit(OpCodes.Ldloc_0);
                        g.Emit(OpCodes.Ldarg, (short)(i + 1));
                        if (ptype.IsByRef)
                        {
                            if (pelemtype.IsValueType)
                            {
                                g.Emit(OpCodes.Ldobj, pelemtype);
                                g.Emit(OpCodes.Box, pelemtype);
                            }
                            else
                            {
                                g.Emit(OpCodes.Ldind_Ref);
                            }
                        }
                        else if (ptype.IsValueType)
                        {
                            g.Emit(OpCodes.Box, ptype);
                        }
                        g.Emit(OpCodes.Callvirt, ObjectTranslator_PushAny);

                        ++in_param_count;
                    }

                    if (pinfo.ParameterType.IsByRef)
                    {
                        ++out_param_count;
                    }
                }

                g.Emit(OpCodes.Ldloc_0);
                g.Emit(OpCodes.Ldc_I4, in_param_count);
                g.Emit(OpCodes.Ldc_I4, out_param_count + (has_return ? 1 : 0));
                g.Emit(OpCodes.Ldloc_1);
                g.Emit(OpCodes.Call, LuaAPI_lua_pcall);
                Label no_exception = g.DefineLabel();
                g.Emit(OpCodes.Brfalse, no_exception);

                g.Emit(OpCodes.Ldarg_0);
                g.Emit(OpCodes.Ldfld, LuaBase_Interpreter);
                g.Emit(OpCodes.Ldloc_1);
                g.Emit(OpCodes.Ldc_I4_1);
                g.Emit(OpCodes.Sub);
                g.Emit(OpCodes.Callvirt, LuaEnv_ThrowExceptionFromError);
                g.MarkLabel(no_exception);

                int offset = 1;
                if (has_return)
                {
                    genGetObjectCall(g, offset++, to_be_impl.ReturnType);
                    g.Emit(OpCodes.Stloc_3);
                }

                for (int i = 0; i < parameters.Length; ++i)
                {
                    var pinfo = parameters[i];
                    var ptype = pinfo.ParameterType;
                    if (ptype.IsByRef)
                    {
                        g.Emit(OpCodes.Ldarg, (short)(i + 1));
                        var pelemtype = ptype.GetElementType();
                        genGetObjectCall(g, offset++, pelemtype);
                        if (pelemtype.IsValueType)
                        {
                            g.Emit(OpCodes.Stobj, pelemtype);
                        }
                        else
                        {
                            g.Emit(OpCodes.Stind_Ref);
                        }

                    }
                }

                if (has_return)
                {
                    g.Emit(OpCodes.Ldloc_3);
                }

                //LuaAPI.lua_settop(L, err_func - 1);
                g.Emit(OpCodes.Ldloc_0);
                g.Emit(OpCodes.Ldloc_1);
                g.Emit(OpCodes.Ldc_I4_1);
                g.Emit(OpCodes.Sub);
                g.Emit(OpCodes.Call, LuaAPI_lua_settop);

                g.Emit(OpCodes.Ret);
            }

            // Constructor
            var ctor_param_types = new Type[] { typeof(int), typeof(LuaEnv) };
            ConstructorInfo parent_ctor = typeof(DelegateBridge).GetConstructor(ctor_param_types);
            var ctor_builder = impl_type_builder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, ctor_param_types);
            var cg = ctor_builder.GetILGenerator();
            cg.Emit(OpCodes.Ldarg_0);
            cg.Emit(OpCodes.Ldarg_1);
            cg.Emit(OpCodes.Ldarg_2);
            cg.Emit(OpCodes.Call, parent_ctor);
            cg.Emit(OpCodes.Ret);

            return impl_type_builder.CreateType();
        }

        private void genGetObjectCall(ILGenerator g, int offset, Type type)
        {
            g.Emit(OpCodes.Ldloc_2); // translator
            g.Emit(OpCodes.Ldloc_0); // L
            g.Emit(OpCodes.Ldloc_1); // err_func
            g.Emit(OpCodes.Ldc_I4, offset);
            g.Emit(OpCodes.Add);
            g.Emit(OpCodes.Ldtoken, type);
            g.Emit(OpCodes.Call, Type_GetTypeFromHandle); // typeof(type)
            g.Emit(OpCodes.Callvirt, ObjectTranslator_GetObject);
            if (type.IsValueType)
            {
                Label not_null = g.DefineLabel();
                Label null_done = g.DefineLabel();
                LocalBuilder local_new = g.DeclareLocal(type);

                g.Emit(OpCodes.Dup);
                g.Emit(OpCodes.Brtrue_S, not_null);

                g.Emit(OpCodes.Pop);
                g.Emit(OpCodes.Ldloca, local_new);
                g.Emit(OpCodes.Initobj, type);
                g.Emit(OpCodes.Ldloc, local_new);
                g.Emit(OpCodes.Br_S, null_done);

                g.MarkLabel(not_null);
                g.Emit(OpCodes.Unbox_Any, type);
                g.MarkLabel(null_done);
            }
            else if (type != typeof(object))
            {
                g.Emit(OpCodes.Castclass, type);
            }
        }

        HashSet<Type> gen_interfaces = new HashSet<Type>();

        public void SetGenInterfaces(List<Type> gen_interfaces)
        {
            gen_interfaces.ForEach((item) =>
            {
                if (!this.gen_interfaces.Contains(item))
                {
                    this.gen_interfaces.Add(item);
                }
            });
        }

        public Type EmitInterfaceImpl(Type to_be_impl)
        {
            if (!to_be_impl.IsInterface)
            {
                throw new InvalidOperationException("interface expected, but got " + to_be_impl);
            }

            if (!gen_interfaces.Contains(to_be_impl))
            {
                throw new InvalidOperationException(to_be_impl.ToString() + " may has CSharpCallLua Attribute or add to CSharpCallLua gen config!");
            }

            TypeBuilder impl_type_builder = CodeEmitModule.DefineType("XLuaGenInterfaceImpl" + (genID++), TypeAttributes.Public | TypeAttributes.Class, typeof(LuaBase), new Type[] { to_be_impl});

            foreach(var member in to_be_impl.GetMembers())
            {
                if (member.MemberType == MemberTypes.Method)
                {
                    MethodInfo method = member as MethodInfo;
                    if (method.Name.StartsWith("get_") || method.Name.StartsWith("set_") ||
                        method.Name.StartsWith("add_") || method.Name.StartsWith("remove_"))
                    {
                        continue;
                    }
                    var parameters = method.GetParameters();

                    Type[] param_types = new Type[parameters.Length];
                    for (int i = 0; i < parameters.Length; ++i)
                    {
                        param_types[i] = parameters[i].ParameterType;
                    }

                    var method_builder = impl_type_builder.DefineMethod(method.Name,
                        MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.Virtual, method.ReturnType, param_types);
                    for (int i = 0; i < parameters.Length; ++i)
                    {
                        method_builder.DefineParameter(i + 1, parameters[i].Attributes, parameters[i].Name);
                    }

                    ILGenerator g = method_builder.GetILGenerator();
                    g.DeclareLocal(typeof(RealStatePtr));//RealStatePtr L;  0
                    g.DeclareLocal(typeof(int));//int err_func; 1
                    g.DeclareLocal(typeof(ObjectTranslator));//ObjectTranslator translator; 2
                    bool has_return = method.ReturnType != typeof(void);
                    if (has_return)
                    {
                        g.DeclareLocal(method.ReturnType); //ReturnType ret; 3
                    }

                    // L = _Interpreter.L;
                    g.Emit(OpCodes.Ldarg_0);
                    g.Emit(OpCodes.Ldfld, LuaBase_Interpreter);
                    g.Emit(OpCodes.Ldfld, LuaEnv_L);
                    g.Emit(OpCodes.Stloc_0);

                    //err_func =LuaAPI.load_error_func(L);
                    g.Emit(OpCodes.Ldloc_0);
                    g.Emit(OpCodes.Call, LuaAPI_load_error_func);
                    g.Emit(OpCodes.Stloc_1);

                    //translator = _Interpreter.translator;
                    g.Emit(OpCodes.Ldarg_0);
                    g.Emit(OpCodes.Ldfld, LuaBase_Interpreter);
                    g.Emit(OpCodes.Ldfld, LuaEnv_translator);
                    g.Emit(OpCodes.Stloc_2);

                    //LuaAPI.lua_getref(L, _Reference);
                    g.Emit(OpCodes.Ldloc_0);
                    g.Emit(OpCodes.Ldarg_0);
                    g.Emit(OpCodes.Ldfld, LuaBase_Reference);
                    g.Emit(OpCodes.Call, LuaAPI_lua_getref);

                    //LuaAPI.lua_pushstring(L, "xxx");
                    g.Emit(OpCodes.Ldloc_0);
                    g.Emit(OpCodes.Ldstr, method.Name);
                    g.Emit(OpCodes.Call, LuaAPI_lua_pushstring);

                    //LuaAPI.xlua_pgettable(L, -2)
                    g.Emit(OpCodes.Ldloc_0);
                    g.Emit(OpCodes.Ldc_I4_S, (sbyte)-2);
                    g.Emit(OpCodes.Call, LuaAPI_xlua_pgettable);
                    Label gettable_no_exception = g.DefineLabel();
                    g.Emit(OpCodes.Brfalse, gettable_no_exception);

                    g.Emit(OpCodes.Ldarg_0);
                    g.Emit(OpCodes.Ldfld, LuaBase_Interpreter);
                    g.Emit(OpCodes.Ldloc_1);
                    g.Emit(OpCodes.Ldc_I4_1);
                    g.Emit(OpCodes.Sub);
                    g.Emit(OpCodes.Callvirt, LuaEnv_ThrowExceptionFromError);
                    g.MarkLabel(gettable_no_exception);

                    //LuaAPI.lua_pushvalue(L, -2);
                    g.Emit(OpCodes.Ldloc_0);
                    g.Emit(OpCodes.Ldc_I4_S, (sbyte)-2);
                    g.Emit(OpCodes.Call, LuaAPI_lua_pushvalue);

                    //LuaAPI.lua_remove(L, -3);
                    g.Emit(OpCodes.Ldloc_0);
                    g.Emit(OpCodes.Ldc_I4_S, (sbyte)-3);
                    g.Emit(OpCodes.Call, LuaAPI_lua_remove);

                    int in_param_count = 0;
                    int out_param_count = 0;
                    //translator.PushAny(L, param_in)
                    for (int i = 0; i < parameters.Length; ++i)
                    {
                        var pinfo = parameters[i];
                        if (!pinfo.IsOut)
                        {
                            var ptype = pinfo.ParameterType;
                            var pelemtype = ptype.IsByRef ? ptype.GetElementType() : ptype;

                            g.Emit(OpCodes.Ldloc_2);
                            g.Emit(OpCodes.Ldloc_0);
                            g.Emit(OpCodes.Ldarg, (short)(i + 1));
                            if (ptype.IsByRef)
                            {
                                if (pelemtype.IsValueType)
                                {
                                    g.Emit(OpCodes.Ldobj, pelemtype);
                                    g.Emit(OpCodes.Box, pelemtype);
                                }
                                else
                                {
                                    g.Emit(OpCodes.Ldind_Ref);
                                }
                            }
                            else if (ptype.IsValueType)
                            {
                                g.Emit(OpCodes.Box, ptype);
                            }
                            g.Emit(OpCodes.Callvirt, ObjectTranslator_PushAny);

                            ++in_param_count;
                        }

                        if (pinfo.ParameterType.IsByRef)
                        {
                            ++out_param_count;
                        }
                    }

                    g.Emit(OpCodes.Ldloc_0);
                    g.Emit(OpCodes.Ldc_I4, in_param_count + 1);
                    g.Emit(OpCodes.Ldc_I4, out_param_count + (has_return ? 1 : 0));
                    g.Emit(OpCodes.Ldloc_1);
                    g.Emit(OpCodes.Call, LuaAPI_lua_pcall);
                    Label no_exception = g.DefineLabel();
                    g.Emit(OpCodes.Brfalse, no_exception);

                    g.Emit(OpCodes.Ldarg_0);
                    g.Emit(OpCodes.Ldfld, LuaBase_Interpreter);
                    g.Emit(OpCodes.Ldloc_1);
                    g.Emit(OpCodes.Ldc_I4_1);
                    g.Emit(OpCodes.Sub);
                    g.Emit(OpCodes.Callvirt, LuaEnv_ThrowExceptionFromError);
                    g.MarkLabel(no_exception);

                    int offset = 1;
                    if (has_return)
                    {
                        genGetObjectCall(g, offset++, method.ReturnType);
                        g.Emit(OpCodes.Stloc_3);
                    }

                    for (int i = 0; i < parameters.Length; ++i)
                    {
                        var pinfo = parameters[i];
                        var ptype = pinfo.ParameterType;
                        if (ptype.IsByRef)
                        {
                            g.Emit(OpCodes.Ldarg, (short)(i + 1));
                            var pelemtype = ptype.GetElementType();
                            genGetObjectCall(g, offset++, pelemtype);
                            if (pelemtype.IsValueType)
                            {
                                g.Emit(OpCodes.Stobj, pelemtype);
                            }
                            else
                            {
                                g.Emit(OpCodes.Stind_Ref);
                            }

                        }
                    }

                    if (has_return)
                    {
                        g.Emit(OpCodes.Ldloc_3);
                    }

                    //LuaAPI.lua_settop(L, err_func - 1);
                    g.Emit(OpCodes.Ldloc_0);
                    g.Emit(OpCodes.Ldloc_1);
                    g.Emit(OpCodes.Ldc_I4_1);
                    g.Emit(OpCodes.Sub);
                    g.Emit(OpCodes.Call, LuaAPI_lua_settop);

                    g.Emit(OpCodes.Ret);
                }
                else if (member.MemberType == MemberTypes.Property)
                {
                    PropertyInfo property = member as PropertyInfo;
                    PropertyBuilder prop_builder = impl_type_builder.DefineProperty(property.Name, property.Attributes, property.PropertyType, Type.EmptyTypes);
                    if (property.Name == "Item")
                    {
                        if (property.CanRead)
                        {
                            var getter_buildler = defineImplementMethod(impl_type_builder, property.GetGetMethod(), 
                                MethodAttributes.Virtual | MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig);
                            genEmptyMethod(getter_buildler.GetILGenerator(), property.PropertyType);
                            prop_builder.SetGetMethod(getter_buildler);
                        }
                        if (property.CanWrite)
                        {
                            var setter_buildler = defineImplementMethod(impl_type_builder, property.GetSetMethod(),
                                MethodAttributes.Virtual | MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig);
                            genEmptyMethod(setter_buildler.GetILGenerator(), property.PropertyType);
                            prop_builder.SetSetMethod(setter_buildler);
                        }
                        continue;
                    }
                    if (property.CanRead)
                    {
                        MethodBuilder getter_buildler = impl_type_builder.DefineMethod("get_" + property.Name, 
                            MethodAttributes.Virtual | MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                            property.PropertyType, Type.EmptyTypes);

                        ILGenerator il = getter_buildler.GetILGenerator();

                        LocalBuilder L = il.DeclareLocal(typeof(RealStatePtr));
                        LocalBuilder oldTop = il.DeclareLocal(typeof(int));
                        LocalBuilder translator = il.DeclareLocal(typeof(ObjectTranslator));
                        LocalBuilder ret = il.DeclareLocal(property.PropertyType);

                        // L = _Interpreter.L;
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, LuaBase_Interpreter);
                        il.Emit(OpCodes.Ldfld, LuaEnv_L);
                        il.Emit(OpCodes.Stloc, L);

                        //oldTop = LuaAPI.lua_gettop(L);
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Call, LuaAPI_lua_gettop);
                        il.Emit(OpCodes.Stloc, oldTop);

                        //translator = _Interpreter.translator;
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, LuaBase_Interpreter);
                        il.Emit(OpCodes.Ldfld, LuaEnv_translator);
                        il.Emit(OpCodes.Stloc, translator);

                        //LuaAPI.lua_getref(L, _Reference);
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, LuaBase_Reference);
                        il.Emit(OpCodes.Call, LuaAPI_lua_getref);

                        //LuaAPI.lua_pushstring(L, "xxx");
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Ldstr, property.Name);
                        il.Emit(OpCodes.Call, LuaAPI_lua_pushstring);

                        //LuaAPI.xlua_pgettable(L, -2)
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Ldc_I4_S, (sbyte)-2);
                        il.Emit(OpCodes.Call, LuaAPI_xlua_pgettable);
                        Label gettable_no_exception = il.DefineLabel();
                        il.Emit(OpCodes.Brfalse, gettable_no_exception);

                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, LuaBase_Interpreter);
                        il.Emit(OpCodes.Ldloc, oldTop);
                        il.Emit(OpCodes.Callvirt, LuaEnv_ThrowExceptionFromError);
                        il.MarkLabel(gettable_no_exception);

                        il.Emit(OpCodes.Ldloc, translator);
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Ldc_I4_S, (sbyte)-1);
                        il.Emit(OpCodes.Ldtoken, property.PropertyType);
                        il.Emit(OpCodes.Call, Type_GetTypeFromHandle); // typeof(type)
                        il.Emit(OpCodes.Callvirt, ObjectTranslator_GetObject);

                        if (property.PropertyType.IsValueType)
                        {
                            Label not_null = il.DefineLabel();
                            Label null_done = il.DefineLabel();
                            LocalBuilder local_new = il.DeclareLocal(property.PropertyType);

                            il.Emit(OpCodes.Dup);
                            il.Emit(OpCodes.Brtrue_S, not_null);

                            il.Emit(OpCodes.Pop);
                            il.Emit(OpCodes.Ldloca, local_new);
                            il.Emit(OpCodes.Initobj, property.PropertyType);
                            il.Emit(OpCodes.Ldloc, local_new);
                            il.Emit(OpCodes.Br_S, null_done);

                            il.MarkLabel(not_null);
                            il.Emit(OpCodes.Unbox_Any, property.PropertyType);
                            il.MarkLabel(null_done);
                        }
                        else if (property.PropertyType != typeof(object))
                        {
                            il.Emit(OpCodes.Castclass, property.PropertyType);
                        }
                        il.Emit(OpCodes.Stloc, ret);

                        //LuaAPI.lua_pop(L, 2);
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Ldc_I4_S, (sbyte)2);
                        il.Emit(OpCodes.Call, LuaAPI_lua_pop);

                        il.Emit(OpCodes.Ldloc, ret);
                        il.Emit(OpCodes.Ret);

                        prop_builder.SetGetMethod(getter_buildler);
                    }
                    if (property.CanWrite)
                    {
                        MethodBuilder setter_builder = impl_type_builder.DefineMethod("set_" + property.Name, 
                            MethodAttributes.Virtual | MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, 
                            null, new Type[] { property.PropertyType });

                        ILGenerator il = setter_builder.GetILGenerator();

                        LocalBuilder L = il.DeclareLocal(typeof(RealStatePtr));
                        LocalBuilder oldTop = il.DeclareLocal(typeof(int));
                        LocalBuilder translator = il.DeclareLocal(typeof(ObjectTranslator));

                        // L = _Interpreter.L;
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, LuaBase_Interpreter);
                        il.Emit(OpCodes.Ldfld, LuaEnv_L);
                        il.Emit(OpCodes.Stloc, L);

                        //oldTop = LuaAPI.lua_gettop(L);
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Call, LuaAPI_lua_gettop);
                        il.Emit(OpCodes.Stloc, oldTop);

                        //translator = _Interpreter.translator;
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, LuaBase_Interpreter);
                        il.Emit(OpCodes.Ldfld, LuaEnv_translator);
                        il.Emit(OpCodes.Stloc, translator);

                        //LuaAPI.lua_getref(L, _Reference);
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, LuaBase_Reference);
                        il.Emit(OpCodes.Call, LuaAPI_lua_getref);

                        //LuaAPI.lua_pushstring(L, "xxx");
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Ldstr, property.Name);
                        il.Emit(OpCodes.Call, LuaAPI_lua_pushstring);

                        //translator.Push(L, value);
                        il.Emit(OpCodes.Ldloc, translator);
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Ldarg_1);
                        if (property.PropertyType.IsValueType)
                        {
                            il.Emit(OpCodes.Box, property.PropertyType);
                        }
                        il.Emit(OpCodes.Callvirt, ObjectTranslator_PushAny);

                        //LuaAPI.xlua_psettable(L, -2)
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Ldc_I4_S, (sbyte)-3);
                        il.Emit(OpCodes.Call, LuaAPI_xlua_psettable);
                        Label settable_no_exception = il.DefineLabel();
                        il.Emit(OpCodes.Brfalse, settable_no_exception);

                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, LuaBase_Interpreter);
                        il.Emit(OpCodes.Ldloc, oldTop);
                        il.Emit(OpCodes.Callvirt, LuaEnv_ThrowExceptionFromError);
                        il.MarkLabel(settable_no_exception);

                        //LuaAPI.lua_pop(L, 1);
                        il.Emit(OpCodes.Ldloc, L);
                        il.Emit(OpCodes.Ldc_I4_S, (sbyte)1);
                        il.Emit(OpCodes.Call, LuaAPI_lua_pop);

                        il.Emit(OpCodes.Ret);

                        prop_builder.SetSetMethod(setter_builder);

                    }
                }
                else if(member.MemberType == MemberTypes.Event)
                {
                    
                    EventInfo event_info = member as EventInfo;
                    EventBuilder event_builder = impl_type_builder.DefineEvent(event_info.Name, event_info.Attributes, event_info.EventHandlerType);
                    if (event_info.GetAddMethod() != null)
                    {
                        var add_buildler = defineImplementMethod(impl_type_builder, event_info.GetAddMethod(),
                            MethodAttributes.Virtual | MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig);
                        genEmptyMethod(add_buildler.GetILGenerator(), typeof(void));
                        event_builder.SetAddOnMethod(add_buildler);
                    }
                    if (event_info.GetRemoveMethod() != null)
                    {
                        var remove_buildler = defineImplementMethod(impl_type_builder, event_info.GetRemoveMethod(),
                            MethodAttributes.Virtual | MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig);
                        genEmptyMethod(remove_buildler.GetILGenerator(), typeof(void));
                        event_builder.SetRemoveOnMethod(remove_buildler);
                    }
                }
            }
            

            // Constructor
            var ctor_param_types = new Type[] { typeof(int), typeof(LuaEnv) };
            ConstructorInfo parent_ctor = typeof(LuaBase).GetConstructor(ctor_param_types);
            var ctor_builder = impl_type_builder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, ctor_param_types);
            var cg = ctor_builder.GetILGenerator();
            cg.Emit(OpCodes.Ldarg_0);
            cg.Emit(OpCodes.Ldarg_1);
            cg.Emit(OpCodes.Ldarg_2);
            cg.Emit(OpCodes.Call, parent_ctor);
            cg.Emit(OpCodes.Ret);

            return impl_type_builder.CreateType();
        }

        private void genEmptyMethod(ILGenerator il, Type returnType)
        {
            if(returnType != typeof(void))
            {
                if (returnType.IsValueType)
                {
                    LocalBuilder local_new = il.DeclareLocal(returnType);
                    il.Emit(OpCodes.Ldloca, local_new);
                    il.Emit(OpCodes.Initobj, returnType);
                    il.Emit(OpCodes.Ldloc, local_new);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
            }
            il.Emit(OpCodes.Ret);
        }

        private MethodBuilder defineImplementMethod(TypeBuilder type_builder, MethodInfo to_be_impl, MethodAttributes attributes)
        {
            var parameters = to_be_impl.GetParameters();

            Type[] param_types = new Type[parameters.Length];
            for (int i = 0; i < parameters.Length; ++i)
            {
                param_types[i] = parameters[i].ParameterType;
            }

            var method_builder = type_builder.DefineMethod(to_be_impl.Name, attributes, to_be_impl.ReturnType, param_types);
            for (int i = 0; i < parameters.Length; ++i)
            {
                method_builder.DefineParameter(i + 1, parameters[i].Attributes, parameters[i].Name);
            }
            return method_builder;
        }
    }
}

#endif

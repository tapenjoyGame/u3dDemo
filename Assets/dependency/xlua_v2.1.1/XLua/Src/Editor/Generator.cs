using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using LuaInterface;
using System;
using System.Reflection;
using System.Text;
using System.Linq;


namespace CSObjectWrapEditor
{
    public static class GeneratorConfig
    {
        public static string common_path = Application.dataPath + "/XLua/Gen/";

        public static string lib_path = common_path + "ByConfig/";

        public static string custom_path = common_path + "ByAttibute/";

        public static string template_path = Application.dataPath + "/XLua/Src/Template/";
    }

    public static class Generator
    {
        static LuaEnv luaenv = new LuaEnv();
        static List<string> OpMethodNames = new List<string>() { "op_Addition", "op_Subtraction", "op_Multiply", "op_Division", "op_Equality", "op_UnaryNegation", "op_LessThan", "op_LessThanOrEqual", "op_Modulus" };

        static int OverloadCosting(MethodBase mi)
        {
            int costing = 0;

            if (!mi.IsStatic)
            {
                costing++;
            }

            foreach (var paraminfo in mi.GetParameters())
            {
                if ((!paraminfo.ParameterType.IsPrimitive ) && (paraminfo.IsIn || !paraminfo.IsOut))
                {
                    costing++;
                }
            }
            costing = costing * 10000 + (mi.GetParameters().Length + (mi.IsStatic ? 0 : 1));
            return costing;
        }

        static IEnumerable<Type> type_has_extension_methods = null;

        static IEnumerable<MethodInfo> GetExtensionMethods(Type extendedType)
        {
            if (type_has_extension_methods == null)
            {
                var gen_types = from assembly in AppDomain.CurrentDomain.GetAssemblies()
                                where !(assembly.ManifestModule is System.Reflection.Emit.ModuleBuilder)
                                from type in assembly.GetExportedTypes()
                                where type.IsDefined(typeof(LuaCallCSharpAttribute), false)
                                select type;
                gen_types = gen_types.Concat(LuaCallCSharp);

                type_has_extension_methods = from type in gen_types
                                             where type.GetMethods(BindingFlags.Static | BindingFlags.Public)
                                                    .Any(method => !method.ContainsGenericParameters && method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false))
                                             select type;
            }
            return from type in type_has_extension_methods
                   where type.IsSealed && !type.IsGenericType && !type.IsNested
                        from method in type.GetMethods(BindingFlags.Static | BindingFlags.Public)
                        where !method.ContainsGenericParameters && method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false)
                        where method.GetParameters()[0].ParameterType == extendedType
                        select method;
        }

        static void getClassInfo(Type type, LuaTable parameters)
        {
            parameters["type"] = type;

            var constructors = new List<MethodBase>();
            var constructor_def_vals = new List<int>();
            if (!type.IsAbstract)
            {
                foreach (var con in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase).Cast<MethodBase>()
                    .Where(constructor => !isObsolete(constructor)))
                {
                    int def_count = 0;
                    constructors.Add(con);
                    constructor_def_vals.Add(def_count);

                    var ps = con.GetParameters();
                    for (int i = ps.Length - 1; i >= 0; i--)
                    {
                        if (ps[i].IsOptional ||
                            (ps[i].IsDefined(typeof(ParamArrayAttribute), false) && i > 0 && ps[i - 1].IsOptional))
                        {
                            def_count++;
                            constructors.Add(con);
                            constructor_def_vals.Add(def_count);
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
            parameters["constructors"] = constructors;
            parameters["constructor_def_vals"] = constructor_def_vals;

            var getters = type.GetProperties().Where(prop => prop.CanRead);
            var setters = type.GetProperties().Where(prop => prop.CanWrite);

            var methodNames = type.GetMethods(BindingFlags.Public | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly).Select(t=>t.Name).Distinct().ToDictionary(t=>t);
            foreach (var getter in getters)
            {
                methodNames.Remove("get_" + getter.Name);
            }

            foreach (var setter in setters)
            {
                methodNames.Remove("set_" + setter.Name);
            }
            List<string> extension_methods_namespace = new List<string>();
            var extension_methods = GetExtensionMethods(type);
            foreach(var extension_method in extension_methods)
            {
                if (extension_method.DeclaringType.Namespace != null 
                    && extension_method.DeclaringType.Namespace != "System.Collections.Generic"
                    && extension_method.DeclaringType.Namespace != "LuaInterface")
                {
                    extension_methods_namespace.Add(extension_method.DeclaringType.Namespace);
                }
            }
            parameters["namespaces"] = extension_methods_namespace.Distinct().ToList();

            //warnning: filter all method start with "op_"  "add_" "remove_" may  filter some ordinary method
            parameters["methods"] = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
                .Where(method=> !method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false) || method.DeclaringType != type)
                .Where(method => methodNames.ContainsKey(method.Name)) //GenericMethod can not be invoke becuase not static info available!
                .Concat(extension_methods)
                .Where(method =>!isMethodInBlackList(method) && !method.IsGenericMethod && !isObsolete(method) && !method.Name.StartsWith("op_") && !method.Name.StartsWith("add_") && !method.Name.StartsWith("remove_"))
                .GroupBy(method => (method.Name + ((method.IsStatic && !method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false)) ? "_xlua_st_" : "")), (k, v) => {
                    var overloads = new List<MethodBase>();
                    List<int> def_vals = new List<int>();
                    foreach (var overload in v.Cast<MethodBase>().OrderBy(mb => OverloadCosting(mb)))
                    {
                        int def_count = 0;
                        overloads.Add(overload);
                        def_vals.Add(def_count);

                        var ps = overload.GetParameters();
                        for (int i = ps.Length - 1; i >=0; i--)
                        {
                            if(ps[i].IsOptional ||
                            (ps[i].IsDefined(typeof(ParamArrayAttribute), false) && i > 0 && ps[i - 1].IsOptional))
                            {
                                def_count++;
                                overloads.Add(overload);
                                def_vals.Add(def_count);
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                    return new {
                        Name = k,
                        IsStatic = overloads[0].IsStatic && !overloads[0].IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false),
                        Overloads = overloads,
                        DefaultValues = def_vals
                    };
                }).ToList();

            parameters["getters"] = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
                
                .Where(prop => prop.CanRead && (prop.GetGetMethod() != null)  && prop.Name != "Item" && !isObsolete(prop) && !isMemberInBlackList(prop)).Select(prop => new { prop.Name, IsStatic = prop.GetGetMethod().IsStatic, ReadOnly = false, Type = prop.PropertyType })
                .Concat(
                    type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
                    .Where(field => !isObsolete(field) && !isMemberInBlackList(field))
                    .Select(field => new { field.Name, field.IsStatic, ReadOnly = field.IsInitOnly || field.IsLiteral, Type = field.FieldType })
                )/*.Where(getter => !typeof(Delegate).IsAssignableFrom(getter.Type))*/.ToList();

            parameters["setters"] = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
                .Where(prop => prop.CanWrite && (prop.GetSetMethod() != null) && prop.Name != "Item" && !isObsolete(prop) && !isMemberInBlackList(prop)).Select(prop => new { prop.Name, IsStatic = prop.GetSetMethod().IsStatic, Type = prop.PropertyType, IsProperty = true })
                .Concat(
                    type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
                    .Where(field => !isObsolete(field) && !isMemberInBlackList(field) && !field.IsInitOnly && !field.IsLiteral)
                    .Select(field => new { field.Name, field.IsStatic, Type = field.FieldType, IsProperty = false })
                )/*.Where(setter => !typeof(Delegate).IsAssignableFrom(setter.Type))*/.ToList();

            parameters["operators"] = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
                .Where(method => OpMethodNames.Contains(method.Name))
                .GroupBy(method => method.Name, (k, v) => new { Name = k, Overloads = v.Cast<MethodBase>().OrderBy(mb => mb.GetParameters().Length).ToList() }).ToList();

            parameters["indexers"] = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
                .Where(method => method.Name == "get_Item" && method.GetParameters().Length == 1)
                .ToList();

            parameters["newindexers"] = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
                .Where(method => method.Name == "set_Item" && method.GetParameters().Length == 2)
                .ToList();

            parameters["events"] = type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly).Where(e => !isObsolete(e) && !isMemberInBlackList(e))
                .Where(ev=> ev.GetAddMethod() != null || ev.GetRemoveMethod() != null)
                .Select(ev => new { IsStatic = ev.GetAddMethod() != null? ev.GetAddMethod().IsStatic: ev.GetRemoveMethod().IsStatic, ev.Name,
                    CanSet = false, CanAdd = ev.GetRemoveMethod() != null, CanRemove = ev.GetRemoveMethod() != null, Type = ev.EventHandlerType})
                .ToList();
        }

        static void getInterfaceInfo(Type type, LuaTable parameters)
        {
            parameters["type"] = type;

            var getters = type.GetProperties().Where(prop => prop.CanRead);
            var setters = type.GetProperties().Where(prop => prop.CanWrite);

            List<string> methodNames = type.GetMethods(BindingFlags.Public | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly).Select(method => method.Name).ToList();
            foreach (var getter in getters)
            {
                methodNames.Remove("get_" + getter.Name);
            }

            foreach (var setter in setters)
            {
                methodNames.Remove("set_" + setter.Name);
            }

            parameters["methods"] = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
                .Where(method => methodNames.Contains(method.Name) && !method.IsGenericMethod && !method.Name.StartsWith("op_") && !method.Name.StartsWith("add_") && !method.Name.StartsWith("remove_")) //GenericMethod can not be invoke becuase not static info available!
                    .ToList();

            parameters["propertys"] = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
                .Where(prop => (prop.CanRead || prop.CanWrite) && prop.Name != "Item")
                    .ToList();

            parameters["events"] = type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly).ToList();

            parameters["indexers"] = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly)
                .Where(prop => (prop.CanRead || prop.CanWrite) && prop.Name == "Item")
                    .ToList();
        }

        static bool isObsolete(MemberInfo mb)
        {
            if (mb == null) return false;
            return mb.IsDefined(typeof(System.ObsoleteAttribute), false);
        }

        static bool isMemberInBlackList(MemberInfo mb)
        {
            if (mb.IsDefined(typeof(BlackListAttribute), false)) return true;

            foreach (var exclude in BlackList)
            {
                if (mb.DeclaringType.FullName == exclude[0] && mb.Name == exclude[1])
                {
                    return true;
                }
            }

            return false;
        }

        static bool isMethodInBlackList(MethodBase mb)
        {
            if (mb.IsDefined(typeof(BlackListAttribute), false)) return true;

            foreach (var exclude in BlackList)
            {
                if (mb.DeclaringType.FullName == exclude[0] && mb.Name == exclude[1])
                {
                    var parameters = mb.GetParameters();
                    bool paramsMatch = true;

                    for (int i = 0; i < parameters.Length; i++)
                    {
                        if (i + 2 >= exclude.Count || parameters[i].ParameterType.FullName != exclude[i + 2])
                        {
                            paramsMatch = false;
                            break;
                        }
                    }
                    if (paramsMatch) return true;
                }
            }
            return false;
        }

        static Dictionary<string, LuaFunction> templateCache = new Dictionary<string, LuaFunction>();
        static void GenOne(Type type, Action<Type, LuaTable> type_info_getter, string template_name, StreamWriter textWriter)
        {
            if (isObsolete(type)) return;
            LuaFunction template;
            if (!templateCache.TryGetValue(template_name, out template))
            {
                template = TemplateEngine.LuaTemplate.Compile(luaenv, File.ReadAllText(GeneratorConfig.template_path + template_name));
                templateCache[template_name] = template;
            }

            LuaTable type_info = luaenv.NewTable();
            LuaTable meta = luaenv.NewTable();
            meta["__index"] = luaenv.Global;
            type_info.SetMetaTable(meta);
            meta.Dispose();

            type_info_getter(type, type_info);

            try
            {
                string genCode = TemplateEngine.LuaTemplate.Execute(template, type_info);
                //string filePath = save_path + type.ToString().Replace("+", "").Replace(".", "").Replace("`", "").Replace("&", "").Replace("[", "").Replace("]", "").Replace(",", "") + file_suffix + ".cs";
                textWriter.Write(genCode);
                textWriter.Flush();
            }
            catch (Exception e)
            {
                Debug.LogError("gen wrap file fail! err=" + e.Message + ", stack=" + e.StackTrace);
            }
            finally
            {
                type_info.Dispose();
            }
        }

        static void GenEnumWrap(IEnumerable<Type> types, string save_path)
        {
            string filePath = save_path + "EnumWrap.cs";
            StreamWriter textWriter = new StreamWriter(filePath, false, Encoding.UTF8);

            GenOne(null, (type, type_info) =>
            {
                type_info["types"] = types.ToList();
            }, "LuaEnumWrap.tpl", textWriter);

            textWriter.Close();
        }

        static void GenInterfaceBridge(IEnumerable<Type> types, string save_path)
        {
            foreach (var wrap_type in types)
            {
                if (!wrap_type.IsInterface) continue;

                string filePath = save_path + wrap_type.ToString().Replace("+", "").Replace(".", "")
                    .Replace("`", "").Replace("&", "").Replace("[", "").Replace("]", "").Replace(",", "") + "Bridge.cs";
                StreamWriter textWriter = new StreamWriter(filePath, false, Encoding.UTF8);
                GenOne(wrap_type, (type, type_info) =>
                {
                    getInterfaceInfo(type, type_info);
                }, "LuaInterfaceBridge.tpl", textWriter);
                textWriter.Close();
            }
        }

        static void GenDelegateBridge(IEnumerable<Type> types, string save_path)
        {
            string filePath = save_path + "DelegatesGensBridge.cs";
            StreamWriter textWriter = new StreamWriter(filePath, false, Encoding.UTF8);
            GenOne(typeof(DelegateBridge), (type, type_info) =>
            {
                type_info["delegates"] = types.Where(wrap_type => typeof(Delegate).IsAssignableFrom(wrap_type))
                    .Select(wrap_type => wrap_type.GetMethod("Invoke")).ToList();
            }, "LuaDelegateBridge.tpl", textWriter);
            textWriter.Close();
        }

        static void GenWrapPusher(IEnumerable<Type> types, string save_path)
        {
            string filePath = save_path + "WrapPusher.cs";
            StreamWriter textWriter = new StreamWriter(filePath, false, Encoding.UTF8);
            GenOne(typeof(ObjectTranslator), (type, type_info) =>
            {
                type_info["type_infos"] = types.Select(t => new { Type = t, Size = SizeOf(t) }).ToList();
            }, "LuaWrapPusher.tpl", textWriter);
            textWriter.Close();
        }

        static void GenWrap(IEnumerable<Type> types, string save_path)
        {
            types = types.Where(type=>!type.IsEnum);

            var typeMap = types.ToDictionary(type => {
                //Debug.Log("type:" + type);
                return type.ToString();
            });

            foreach (var wrap_type in types)
            {
                string filePath = save_path + wrap_type.ToString().Replace("+", "").Replace(".", "")
                    .Replace("`", "").Replace("&", "").Replace("[", "").Replace("]", "").Replace(",", "") + "Wrap.cs";
                StreamWriter textWriter = new StreamWriter(filePath, false, Encoding.UTF8);
                if (wrap_type.IsEnum)
                {
                    GenOne(wrap_type, (type, type_info) =>
                    {
                        type_info["type"] = type;
                        type_info["fields"] = type.GetFields(BindingFlags.GetField | BindingFlags.Public | BindingFlags.Static)
                            .Where(field => !isObsolete(field))
                            .ToList();
                    }, "LuaEnumWrap.tpl", textWriter);
                }
                else if (typeof(Delegate).IsAssignableFrom(wrap_type))
                {


                    GenOne(wrap_type, (type, type_info) =>
                    {
                        type_info["type"] = type;
                        type_info["delegate"] = type.GetMethod("Invoke");
                    }, "LuaDelegateWrap.tpl", textWriter);

                }
                else
                {
                    GenOne(wrap_type, (type, type_info) =>
                    {
                        if (type.BaseType != null && typeMap.ContainsKey(type.BaseType.ToString()))
                        {
                            type_info["base"] = type.BaseType;
                        }
                        getClassInfo(type, type_info);
                    }, "LuaClassWrap.tpl", textWriter);
                }
                textWriter.Close();
            }
        }

        static void clear(string path)
        {
            try
            {
                System.IO.Directory.Delete(path, true);
                AssetDatabase.DeleteAsset(path.Substring(path.IndexOf("Assets") + "Assets".Length));
            }
            catch
            {

            }

            AssetDatabase.Refresh();
        }

        class DelegateByMethodDecComparer : IEqualityComparer<Type>
        {
            public bool Equals(Type x, Type y)
            {
                return Utils.IsParamsMatch(x.GetMethod("Invoke"), y.GetMethod("Invoke"));
            }
            public int GetHashCode(Type obj)
            {
                int hc = 0;
                var method = obj.GetMethod("Invoke");
                hc += method.ReturnType.GetHashCode();
                foreach (var pi in method.GetParameters())
                {
                    hc += pi.ParameterType.GetHashCode();
                }
                return hc;
            }
        }

        public static void GenDelegateBridges()
        {
            IEnumerable<Type> all_types = Assembly.Load("Assembly-CSharp").GetExportedTypes();
            try
            {
                all_types = all_types.Concat(Assembly.Load("Assembly-CSharp-firstpass").GetExportedTypes());
            }
            catch (Exception)
            {

            }

            var delegate_types = all_types.Where(type=>!type.IsGenericTypeDefinition)
                .Where(type => type.IsDefined(typeof(CSharpCallLuaAttribute), false)).Where(type => typeof(Delegate).IsAssignableFrom(type))
                .Concat(CSharpCallLua.Where(type => typeof(Delegate).IsAssignableFrom(type))).Distinct(new DelegateByMethodDecComparer());

            GenDelegateBridge(delegate_types, GeneratorConfig.common_path);
        }

        public static void GenEnumWraps()
        {
            IEnumerable<Type> all_types = Assembly.Load("Assembly-CSharp").GetExportedTypes();
            try
            {
                all_types = all_types.Concat(Assembly.Load("Assembly-CSharp-firstpass").GetExportedTypes());
            }
            catch (Exception)
            {

            }

            var enum_types = all_types.Where(type => !type.IsGenericTypeDefinition)
                .Where(type => type.IsDefined(typeof(LuaCallCSharpAttribute), false)).Where(type => type.IsEnum)
                .Concat(LuaCallCSharp.Where(type => type.IsEnum)).Distinct();

            GenEnumWrap(enum_types, GeneratorConfig.common_path);
        }

        public static void GenLuaRegister(bool minimum = false)
        {
            IEnumerable<Type> all_types = Assembly.Load("Assembly-CSharp").GetExportedTypes();
            try
            {
                all_types = all_types.Concat(Assembly.Load("Assembly-CSharp-firstpass").GetExportedTypes());
            }
            catch (Exception)
            {

            }

            var wraps = minimum ? new List<Type>() : (all_types.Where(type => !type.IsGenericTypeDefinition)
                .Where(type => type.IsDefined(typeof(LuaCallCSharpAttribute), false))
                .Concat(LuaCallCSharp).Distinct());

            var itf_bridges = all_types.Where(type => !type.IsGenericTypeDefinition)
                .Where(type => type.IsDefined(typeof(CSharpCallLuaAttribute), false))
                .Concat(CSharpCallLua).Distinct().Where(t => t.IsInterface);

            string filePath = GeneratorConfig.common_path + "XLuaGenAutoRegister.cs";
            StreamWriter textWriter = new StreamWriter(filePath, false, Encoding.UTF8);
            GenOne(typeof(DelegateBridge), (type, type_info) =>
            {
                type_info["wraps"] = wraps.ToList();
                type_info["itf_bridges"] = itf_bridges.ToList();
            }, "LuaRegister.tpl", textWriter);
            textWriter.Close();
        }

        public static void AllSubStruct(Type type, Action<Type> cb)
        {
            if (!type.IsPrimitive)
            {
                cb(type);
                foreach(var fieldInfo in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    AllSubStruct(fieldInfo.FieldType, cb);
                }

                foreach(var propInfo in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (AdditionalProperties.ContainsKey(type) && AdditionalProperties[type].Contains(propInfo.Name))
                    AllSubStruct(propInfo.PropertyType, cb);
                }
            }
        }

        class XluaFieldInfo
        {
            public string Name;
            public Type Type;
            public bool IsField;
            public int Size;
        }

        public static void GenPackUnpack(IEnumerable<Type> types, string save_path)
        {

            List<Type> all_types = new List<Type>();

            foreach(var type in types)
            {
                AllSubStruct(type, (t) =>
                {
                    all_types.Add(t);
                });
            }

            string filePath = save_path + "PackUnpack.cs";
            StreamWriter textWriter = new StreamWriter(filePath, false, Encoding.UTF8);
            GenOne(typeof(CopyByValue), (type, type_info) =>
            {
                type_info["type_infos"] = all_types.Distinct().Select(t =>
                {
                    var fs = t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Select(fi => new XluaFieldInfo { Name = fi.Name, Type = fi.FieldType, IsField = true, Size = SizeOf(fi.FieldType) })
                        .Concat(t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Where(prop => {
                            return AdditionalProperties.ContainsKey(t) && AdditionalProperties[t].Contains(prop.Name);
                        })
                        .Select(prop=> new XluaFieldInfo { Name = prop.Name, Type = prop.PropertyType, IsField = false, Size = SizeOf(prop.PropertyType) }));
                    int float_field_count = 0;
                    bool only_float = true;
                    foreach (var f in fs)
                    {
                        if (f.Type == typeof(float))
                        {
                            float_field_count++;
                        }
                        else
                        {
                            only_float = false;
                            break;
                        }
                    }
                    List<List<XluaFieldInfo>> grouped_field = null;
                    if (only_float && float_field_count > 1)
                    {
                        grouped_field = new List<List<XluaFieldInfo>>();
                        List<XluaFieldInfo> group = null;
                        foreach (var f in fs)
                        {
                            if (group == null) group = new List<XluaFieldInfo>();
                            group.Add(f);
                            if (group.Count >= 6)
                            {
                                grouped_field.Add(group);
                                group = null;
                            }
                        }
                        if (group != null) grouped_field.Add(group);
                    }
                    return new { Type = t, FieldInfos = fs.ToList(), FieldGroup = grouped_field };
                }).ToList();
            }, "PackUnpack.tpl", textWriter);
            textWriter.Close();
        }

        //lua中要使用到C#库的配置，比如C#标准库，或者Unity API，第三方库等。
        static List<Type> LuaCallCSharp = null;

        //C#静态调用Lua的配置（包括事件的原型），仅可以配delegate，interface
        static List<Type> CSharpCallLua = null;

        //黑名单
        static List<List<string>> BlackList = null;

        static List<Type> GCOptimizeList = null;

        static Dictionary<Type, List<string>> AdditionalProperties = null;

        public static void GetGenConfig()
        {
            LuaCallCSharp = new List<Type>();

            CSharpCallLua = new List<Type>();

            GCOptimizeList = new List<Type>();

            AdditionalProperties = new Dictionary<Type, List<string>>();

            BlackList = new List<List<string>>()
            {
                /*new List<string>(){"UnityEngine.WWW", "movie"},
                new List<string>(){"UnityEngine.Texture2D", "alphaIsTransparency"},
                new List<string>(){"UnityEngine.Security", "GetChainOfTrustValue"},
                new List<string>(){"UnityEngine.CanvasRenderer", "onRequestRebuild"},
                new List<string>(){"UnityEngine.Light", "areaSize"},
                new List<string>(){"UnityEngine.AnimatorOverrideController", "PerformOverrideClipListCleanup"},
    #if !UNITY_WEBPLAYER
			    new List<string>(){"UnityEngine.Application", "ExternalEval"},
    #endif
                new List<string>(){"UnityEngine.GameObject", "networkView"}, //4.6.2 not support
                new List<string>(){"UnityEngine.Component", "networkView"},  //4.6.2 not support
                new List<string>(){"System.IO.FileInfo", "GetAccessControl", "System.Security.AccessControl.AccessControlSections"},
                new List<string>(){"System.IO.FileInfo", "SetAccessControl", "System.Security.AccessControl.FileSecurity"},
                new List<string>(){"System.IO.DirectoryInfo", "GetAccessControl", "System.Security.AccessControl.AccessControlSections"},
                new List<string>(){"System.IO.DirectoryInfo", "SetAccessControl", "System.Security.AccessControl.DirectorySecurity"},
                new List<string>(){"System.IO.DirectoryInfo", "CreateSubdirectory", "System.String", "System.Security.AccessControl.DirectorySecurity"},
                new List<string>(){"System.IO.DirectoryInfo", "Create", "System.Security.AccessControl.DirectorySecurity"},*/
            };

            foreach(var t in (from assembly in AppDomain.CurrentDomain.GetAssemblies()
                              where !(assembly.ManifestModule is System.Reflection.Emit.ModuleBuilder)
                              from type in assembly.GetExportedTypes() select type))
            {
                if(!t.IsInterface && typeof(LuaInterface.GenConfig).IsAssignableFrom(t))
                {
                    var cfg = Activator.CreateInstance(t) as LuaInterface.GenConfig;
                    if (cfg.LuaCallCSharp != null) LuaCallCSharp.AddRange(cfg.LuaCallCSharp.Where(type => !type.IsGenericTypeDefinition));
                    if (cfg.CSharpCallLua != null) CSharpCallLua.AddRange(cfg.CSharpCallLua.Where(type => !type.IsGenericTypeDefinition));
                    if (cfg.BlackList != null) BlackList.AddRange(cfg.BlackList);
                }
                else if (!t.IsInterface && typeof(LuaInterface.GCOptimizeConfig).IsAssignableFrom(t))
                {
                    var cfg = Activator.CreateInstance(t) as LuaInterface.GCOptimizeConfig;
                    if (cfg.TypeList != null) GCOptimizeList.AddRange(cfg.TypeList.Where(type => !type.IsGenericTypeDefinition));
                    if (cfg.AdditionalProperties != null)
                    {
                        foreach(var kv in cfg.AdditionalProperties)
                        {
                            if(!AdditionalProperties.ContainsKey(kv.Key))
                            {
                                AdditionalProperties.Add(kv.Key, kv.Value);
                            }
                        }
                    }
                }
            }
            LuaCallCSharp = LuaCallCSharp.Distinct().Where(type=>/*type.IsPublic && */!isObsolete(type)).ToList();//public的内嵌Enum（其它类型未测试），IsPublic为false，像是mono的bug
            CSharpCallLua = CSharpCallLua.Distinct().Where(type =>/*type.IsPublic && */!isObsolete(type)).ToList();
            GCOptimizeList = GCOptimizeList.Distinct().Where(type =>/*type.IsPublic && */!isObsolete(type)).ToList();
        }

        static Dictionary<Type, int> type_size = new Dictionary<Type, int>()
        {
            { typeof(byte), 1 },
            { typeof(sbyte), 1 },
            { typeof(short), 2 },
            { typeof(ushort), 2 },
            { typeof(int), 4 },
            { typeof(uint), 4 },
            { typeof(long), 8 },
            { typeof(ulong), 8 },
            { typeof(float), 4 },
            { typeof(double), 8 },
        };

        static int SizeOf(Type type)
        {
            if (type_size.ContainsKey(type))
            {
                return type_size[type];
            }

            if (!CopyByValue.IsStruct(type) || type == typeof(Decimal))
            {
                return -1;
            }

            int size = 0;
            foreach(var fieldInfo in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                int t_size = SizeOf(fieldInfo.FieldType);
                if (t_size == -1)
                {
                    size = -1;
                    break;
                }
                size += t_size;
            }
            if (size != -1 && AdditionalProperties.ContainsKey(type))
            {
                var propers = AdditionalProperties[type];
                foreach (var propInfo in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (propers.Contains(propInfo.Name))
                    {
                        int t_size = SizeOf(propInfo.PropertyType);
                        if (t_size == -1)
                        {
                            size = -1;
                            break;
                        }
                        size += t_size;
                    }
                }
            }

            if (!type_size.ContainsKey(type))
            {
                type_size.Add(type, size);
            }

            return size;
        }

        public static void Gen(IEnumerable<Type> wraps, IEnumerable<Type> itf_bridges, string save_path)
        {
            templateCache.Clear();
            Directory.CreateDirectory(save_path);
            GenWrap(wraps, save_path);
            var pushers = wraps.Where(type => !type.IsPrimitive && SizeOf(type) != -1).Where((type) =>
            {
                if(GCOptimizeList.Contains(type))
                {
                    return true;
                }
                object[] ccla = type.GetCustomAttributes(typeof(LuaCallCSharpAttribute), false);
                return ccla.Length == 1 && (((ccla[0] as LuaCallCSharpAttribute).Flag & GenFlag.GCOptimize) != 0);
            });
            GenWrapPusher(pushers, save_path);
            GenPackUnpack(pushers, save_path);
            GenInterfaceBridge(itf_bridges, save_path);
        }

        public static void GenLibWrap(bool minimum = false)
        {
            Gen(minimum ? new List<Type>() : LuaCallCSharp.Where(type => !type.IsGenericTypeDefinition), CSharpCallLua.Where(type => type.IsInterface), GeneratorConfig.lib_path);
        }

        public static void GenCustomWrap(bool minimum = false)
        {
            templateCache.Clear();
            string save_path = GeneratorConfig.custom_path;
            Directory.CreateDirectory(save_path);
            IEnumerable<Type> all_types = Assembly.Load("Assembly-CSharp").GetExportedTypes();
            try
            {
                all_types = all_types.Concat(Assembly.Load("Assembly-CSharp-firstpass").GetExportedTypes());
            }
            catch (Exception)
            {

            }
            var warp_types = minimum ? new List<Type>() : (all_types.Where(type => !type.IsGenericTypeDefinition)
                .Where(type => type.IsDefined(typeof(LuaCallCSharpAttribute), false)));

            Gen(warp_types, all_types.Where(type => type.IsDefined(typeof(CSharpCallLuaAttribute), false)).Where(type => type.IsInterface), GeneratorConfig.custom_path);
        }

        [MenuItem("XLua/Generate Code", false, 11)]
        public static void GenAll()
        {
            Directory.CreateDirectory(GeneratorConfig.common_path);
            GetGenConfig();
            GenDelegateBridges();
            GenEnumWraps();
            GenLibWrap();
            GenCustomWrap();
            GenLuaRegister();
            Debug.Log("finished!");
            AssetDatabase.Refresh();
        }

        [MenuItem("XLua/Clear Generated Code", false, 11)]
        public static void ClearAll()
        {
            clear(GeneratorConfig.common_path);
        }

        //[MenuItem("XLua/Generate Minimum Code", false, 11)]
        public static void GenMinimum()
        {
            Directory.CreateDirectory(GeneratorConfig.common_path);
            GetGenConfig();
            GenDelegateBridges();
            GenLibWrap(true);
            GenCustomWrap(true);
            GenLuaRegister(true);
            Debug.Log("finished!");
            AssetDatabase.Refresh();
        }

    }

    [InitializeOnLoad]
    public class Startup
    {

        static Startup()
        {
            EditorApplication.update += Update;
        }


        static void Update()
        {
            EditorApplication.update -= Update;

            if (!System.IO.Directory.Exists(GeneratorConfig.lib_path) && !System.IO.Directory.Exists(GeneratorConfig.custom_path))
            {
                UnityEngine.Debug.LogWarning("code has not been genrate, may be not work in phone!");
            }
        }

    }
}

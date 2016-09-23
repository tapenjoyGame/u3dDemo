#if USE_UNI_LUA
using LuaAPI = UniLua.Lua;
using RealStatePtr = UniLua.ILuaState;
using LuaCSFunction = UniLua.CSharpFunctionDelegate;
#else
using LuaAPI = LuaDLL.Lua;
using RealStatePtr = System.IntPtr;
using LuaCSFunction = LuaDLL.lua_CSFunction;
#endif

using LuaInterface;
using System.Collections.Generic;
<%ForEachCsList(namespaces, function(namespace)%>using <%=namespace%>;<%end)%>
<%
require "TemplateCommon"

local OpNameMap = {op_Addition = "__AddMeta", op_Subtraction = "__SubMeta", op_Multiply = "__MulMeta", op_Division = "__DivMeta", op_Equality = "__EqMeta", op_UnaryNegation = "__UnmMeta", op_LessThan="__LTMeta", op_LessThanOrEqual="__LEMeta", op_Modulus="__ModMeta"}
local OpCallNameMap = {op_Addition = "+", op_Subtraction = "-", op_Multiply = "*", op_Division = "/", op_Equality = "==", op_UnaryNegation = "-", op_LessThan="<", op_LessThanOrEqual="<=", op_Modulus="%"}

local is_static = function(e) return e.IsStatic end
local is_not_static = function(e) return not e.IsStatic end
local has_methods = IfAny(methods,is_not_static) or IfAny(events, is_not_static)
local has_geter = IfAny(getters, is_not_static)
local has_seter = IfAny(setters, is_not_static)
local has_meta_func = operators and operators.Count > 0
local has_static_getter = IfAny(getters, is_static)
local has_static_setter = IfAny(setters, is_static)
%>
namespace CSObjectWrap
{
    public class <%=CSVariableName(type)%>Wrap
    {
        public static void __Register(RealStatePtr L)
        {
            Dictionary<string, object> classFields = new Dictionary<string, object>(){
                <%ForEachCsList(methods, function(method) if method.IsStatic then %>{"<%=method.Overloads[0].Name%>", new LuaCSFunction(<%=method.Name%>)}, 
                <% end end)%>
				<%ForEachCsList(events, function(event) if event.IsStatic then %>{"<%=event.Name%>", new LuaCSFunction(<%=event.Name%>)},
				<% end end)%>
                <%ForEachCsList(getters, function(getter) if getter.IsStatic and getter.ReadOnly then %>{"<%=getter.Name%>", <%=CsFullTypeName(type).."."..getter.Name%>},
                <%end end)%>
                {"UnderlyingSystemType", typeof(<%=CsFullTypeName(type)%>)},
            };
            <%if has_methods then%>
            Dictionary<string, LuaCSFunction> __Methods_Dic = new Dictionary<string, LuaCSFunction>(){
                <%ForEachCsList(methods, function(method) if not method.IsStatic then %>{"<%=method.Name%>", <%=method.Name%>},
                <% end end)%>
				<%ForEachCsList(events, function(event) if not event.IsStatic then %>{"<%=event.Name%>", new LuaCSFunction(<%=event.Name%>)},
				<% end end)%>
            };
            <%end%>
            <%if has_geter then%>
            Dictionary<string, LuaCSFunction> __Geter_Dic = new Dictionary<string, LuaCSFunction>(){
                <%ForEachCsList(getters, function(getter) if not getter.IsStatic then %>{"<%=getter.Name%>", get_<%=getter.Name%>},
                <%end end)%>
            };
            <%end%>
            <%if has_seter then%>
            Dictionary<string, LuaCSFunction> __Seter_Dic = new Dictionary<string, LuaCSFunction>(){
                <%ForEachCsList(setters, function(setter) if not setter.IsStatic then %>{"<%=setter.Name%>", set_<%=setter.Name%>},
                <%end end)%>
            };
            <%end%>
            <%if has_meta_func then%>
            Dictionary<string, LuaCSFunction> __MetaFucntions_Dic = new Dictionary<string, LuaCSFunction>(){
                <%ForEachCsList(operators, function(operator)%>{"<%=(OpNameMap[operator.Name]):gsub('Meta', ''):lower()%>", <%=OpNameMap[operator.Name]%>},
                <%end)%>
            };
            <%end%>
            <%if has_static_getter then%>
            Dictionary<string, LuaCSFunction> __Static_Geter_Dic = new Dictionary<string, LuaCSFunction>(){
                <%ForEachCsList(getters, function(getter) if getter.IsStatic and (not getter.ReadOnly) then %>{"<%=getter.Name%>", get_<%=getter.Name%>},
                <%end end)%>
            };
            <%end%>
            <%if has_static_setter then%>
            Dictionary<string, LuaCSFunction> __Static_Seter_Dic = new Dictionary<string, LuaCSFunction>(){
                <%ForEachCsList(setters, function(setter) if setter.IsStatic then %>{"<%=setter.Name%>", set_<%=setter.Name%>},
                <%end end)%>
            };
            <%end%>
            Utils.RegisterWrap(L, typeof(<%=CsFullTypeName(type)%>), <%=(has_meta_func and '__MetaFucntions_Dic' or 'null')%>, __CreateInstance, classFields, <%=(has_methods and '__Methods_Dic' or 'null')%>, <%=(has_geter and '__Geter_Dic' or 'null')%>,
                <% if type.IsArray or ((indexers.Count or 0) > 0) then %>__CSIndexer<%else%>null<%end%>, <%=(has_seter and '__Seter_Dic' or 'null')%>, <%if type.IsArray or ((newindexers.Count or 0) > 0) then%>__NewIndexer<%else%>null<%end%>,
                <%=(has_static_getter and '__Static_Geter_Dic' or 'null')%>, <%=(has_static_setter and '__Static_Seter_Dic' or 'null')%>);
        }
        
        [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
        static int __CreateInstance(RealStatePtr L)
        {
            <% 
            if constructors.Count == 0  then 
			   if type.IsValueType then
			%>ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
			translator.Push(L, default(<%=CsFullTypeName(type)%>));
			return 1;
			<%
			   else
            %>return LuaAPI.luaL_error(L, "<%=CsFullTypeName(type)%> does not have a constructor!");<% 
			   end
            else %>
            ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
			try {
				<% ForEachCsList(constructors, function(constructor, ci)
					local parameters = constructor:GetParameters()
					local def_count = constructor_def_vals[ci]
					local param_count = parameters.Length
					local real_param_count = param_count - def_count
                    local has_v_params = param_count > 0 and IsParams(parameters[param_count - 1])
				%>if(LuaAPI.lua_gettop(L)==<%=parameters.Length + 1 - def_count%><%ForEachCsList(parameters, function(parameter, pi) 
                if pi >= real_param_count then return end 
                local parameterType = parameter.ParameterType
                if has_v_params and pi == param_count - 1 then  parameterType = parameterType:GetElementType() end
                %> && <%=GetCheckStatement(parameterType, pi+2)%><% end)%>)
				{
					<%ForEachCsList(parameters, function(parameter, pi) 
                    if pi >= real_param_count then return end 
                    %><%=GetCasterStatement(parameter.ParameterType, pi+2, parameter.Name, true, has_v_params and pi == param_count - 1)%>;
					<%end)%>
					<%=CsFullTypeName(type)%> __cl_gen_ret = new <%=CsFullTypeName(type)%>(<%ForEachCsList(parameters, function(parameter, pi) if pi >= real_param_count then return end; if pi ~=0 then %><%=', '%><% end %><%=parameter.Name%><% end)%>);
					translator.Push(L, __cl_gen_ret);
					return 1;
				}
				<%end)%>
			}
			catch(System.Exception __gen_e) {
				return LuaAPI.luaL_error(L, "c# exception:" + __gen_e +",stack:"+ __gen_e.StackTrace);
			}
            return LuaAPI.luaL_error(L, "invalid arguments to <%=CsFullTypeName(type)%> constructor!");
            <% end %>
        }
        
		<% if type.IsArray or indexers.Count then %>
        [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
        public static int __CSIndexer(RealStatePtr L)
        {
			<%if type.IsArray then %>
			ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
			try {
				if (<%=GetCheckStatement(type, 1)%> && LuaAPI.lua_isnumber(L, 2))
				{
					int index = (int)LuaAPI.lua_tonumber(L, 2);
					<%=GetSelfStatement(type)%>;
					LuaAPI.lua_pushboolean(L, true);
					<%=GetPushStatement(type:GetElementType(), "__cl_gen_to_be_invoked[index]")%>;
					return 2;
				}
			}
			catch(System.Exception __gen_e) {
				return LuaAPI.luaL_error(L, "c# exception:" + __gen_e +",stack:"+ __gen_e.StackTrace);
			}
			<%elseif indexers.Count > 0 then
			%>ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
			try {
				<%
					ForEachCsList(indexers, function(indexer)
						local paramter = indexer:GetParameters()[0]
				%>
				if (<%=GetCheckStatement(type, 1)%> && <%=GetCheckStatement(paramter.ParameterType, 2)%>)
				{
					
					<%=GetSelfStatement(type)%>;
					<%=GetCasterStatement(paramter.ParameterType, 2, "index", true)%>;
					LuaAPI.lua_pushboolean(L, true);
					<%=GetPushStatement(indexer.ReturnType, "__cl_gen_to_be_invoked[index]")%>;
					return 2;
				}
				<%end)%>
			}
			catch(System.Exception __gen_e) {
				return LuaAPI.luaL_error(L, "c# exception:" + __gen_e +",stack:"+ __gen_e.StackTrace);
			}
			<%end%>
            LuaAPI.lua_pushboolean(L, false);
			return 1;
        }
		<% end %>
        
        [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
        public static int __NewIndexer(RealStatePtr L)
        {
			<%if type.IsArray or newindexers.Count > 0 then %>ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);<%end%>
			<%if type.IsArray then 
				local elementType = type:GetElementType()
			%>
			try {
				if (<%=GetCheckStatement(type, 1)%> && LuaAPI.lua_isnumber(L, 2) && <%=GetCheckStatement(elementType, 3)%>)
				{
					int index = (int)LuaAPI.lua_tonumber(L, 2);
					<%=GetSelfStatement(type)%>;
					<%=GetCasterStatement(elementType, 3, "__cl_gen_to_be_invoked[index]")%>;
					LuaAPI.lua_pushboolean(L, true);
					return 1;
				}
			}
			catch(System.Exception __gen_e) {
				return LuaAPI.luaL_error(L, "c# exception:" + __gen_e +",stack:"+ __gen_e.StackTrace);
			}
			<%elseif newindexers.Count > 0 then%>
			try {
				<%ForEachCsList(newindexers, function(newindexer)
						local keyType = newindexer:GetParameters()[0].ParameterType
						local valueType = newindexer:GetParameters()[1].ParameterType
				%>
				if (<%=GetCheckStatement(type, 1)%> && <%=GetCheckStatement(keyType, 2)%> && <%=GetCheckStatement(valueType, 3)%>)
				{
					
					<%=GetSelfStatement(type)%>;
					<%=GetCasterStatement(keyType, 2, "key", true)%>;
					<%if IsStruct(valueType) then%><%=GetCasterStatement(valueType, 3, "__cl_gen_value", true)%>;
					__cl_gen_to_be_invoked[key] = __cl_gen_value;<%else
                  %><%=GetCasterStatement(valueType, 3, "__cl_gen_to_be_invoked[key]")%>;<%end%>
					LuaAPI.lua_pushboolean(L, true);
					return 1;
				}
				<%end)%>
			}
			catch(System.Exception __gen_e) {
				return LuaAPI.luaL_error(L, "c# exception:" + __gen_e +",stack:"+ __gen_e.StackTrace);
			}
			<%end%>
			LuaAPI.lua_pushboolean(L, false);
            return 1;
        }
        
        <%ForEachCsList(operators, function(operator) %>
        [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
        static int <%=OpNameMap[operator.Name]%>(RealStatePtr L)
        {
            ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
            <% if operator.Name ~= "op_UnaryNegation" then 
                ForEachCsList(operator.Overloads, function(overload)
                local left_param = overload:GetParameters()[0]
                local right_param = overload:GetParameters()[1]
            %>
			try {
				if (<%=GetCheckStatement(left_param.ParameterType, 1)%> && <%=GetCheckStatement(right_param.ParameterType, 2)%>)
				{
					<%=GetCasterStatement(left_param.ParameterType, 1, "leftside", true)%>;
					<%=GetCasterStatement(right_param.ParameterType, 2, "rightside", true)%>;
					
					translator.Push(L, leftside <%=OpCallNameMap[operator.Name]%> rightside);
					
					return 1;
				}
			}
			catch(System.Exception __gen_e) {
				return LuaAPI.luaL_error(L, "c# exception:" + __gen_e +",stack:"+ __gen_e.StackTrace);
			}
            <%end)%>
            return LuaAPI.luaL_error(L, "invalid arguments to right hand of [+] operator, need <%=CsFullTypeName(type)%>!");
            <%else%>
            <%=GetCasterStatement(type, 1, "rightside", true)%>;
            try {
                translator.Push(L, <%=OpCallNameMap[operator.Name]%> rightside);
            } catch(System.Exception __gen_e) {
                return LuaAPI.luaL_error(L, "c# exception:" + __gen_e +",stack:"+ __gen_e.StackTrace);
            }
            return 1;
            <%end%>
        }
        <%end)%>
        
        <%ForEachCsList(methods, function(method)%>
        [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
        static int <%=method.Name%>(RealStatePtr L)
        {
            <%
            local need_obj = not method.IsStatic
            if MethodCallNeedTranslator(method) then
            %>
            ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
            <%end%>
            <%if need_obj then%>
            <%=GetSelfStatement(type)%>;
            <%end%>
            <%if method.Overloads.Count > 1 then%>
			int __gen_param_count = LuaAPI.lua_gettop(L);
            <%end%>
            try {
                <%ForEachCsList(method.Overloads, function(overload, oi)
                local parameters = MethodParameters(overload)
                local in_num = CalcCsList(parameters, function(p) return p.IsIn or not p.IsOut end)
                local param_offset = method.IsStatic and 0 or 1
                local out_num = CalcCsList(parameters, function(p) return p.IsOut or p.ParameterType.IsByRef end)
                local in_pos = 0
                local has_return = (overload.ReturnType.FullName ~= "System.Void")
                local def_count = method.DefaultValues[oi]
				local param_count = parameters.Length
                local real_param_count = param_count - def_count
                local has_v_params = param_count > 0 and IsParams(parameters[param_count - 1])
                if method.Overloads.Count > 1 then
                %>if(__gen_param_count <%=has_v_params and ">=" or "=="%> <%=in_num+param_offset-def_count%><%
                    ForEachCsList(parameters, function(parameter, pi)
                        if pi >= real_param_count then return end
                        local parameterType = parameter.ParameterType
                        if has_v_params and pi == param_count - 1 then  parameterType = parameterType:GetElementType() end
                        if parameter.IsIn or not parameter.IsOut then in_pos = in_pos + 1; 
                        %>&& <%=GetCheckStatement(parameterType , in_pos+param_offset)%><% 
                        end 
                    end)%>) <%end%>
                {
                    <% 
                    in_pos = 0;
                    ForEachCsList(parameters, function(parameter, pi) 
                        if pi >= real_param_count then return end
                        %><%if parameter.IsIn or not parameter.IsOut then 
                            in_pos = in_pos + 1
                        %><%=GetCasterStatement(parameter.ParameterType, in_pos+param_offset, parameter.Name, true, has_v_params and pi == param_count - 1)%><%
					    else%><%=CsFullTypeName(parameter.ParameterType)%> <%=parameter.Name%><%end%>;
                    <%end)%>
                    <%
                    if has_return then
                        %>    <%=CsFullTypeName(overload.ReturnType)%> __cl_gen_ret = <%
                    end
                    %><%if method.IsStatic then
                    %><%=CsFullTypeName(type).."."..overload.Name%><%
                    else
                    %>__cl_gen_to_be_invoked.<%=overload.Name%><%
                    end%>( <%ForEachCsList(parameters, function(parameter, pi) 
                        if pi >= real_param_count then return end
                        if pi ~= 0 then %>, <% end; if parameter.IsOut then %>out <% elseif parameter.ParameterType.IsByRef then %>ref <% end %><%=parameter.Name%><% end) %> );
                    <%
                    if has_return then
                    %>    <%=GetPushStatement(overload.ReturnType, "__cl_gen_ret")%>;
                    <%
                    end
                    local in_pos = 0
                    ForEachCsList(parameters, function(parameter, pi)
                        if pi >= real_param_count then return end
                        if parameter.IsIn or not parameter.IsOut then 
                            in_pos = in_pos + 1
                        end
                        if parameter.IsOut or parameter.ParameterType.IsByRef then
                        %><%=GetPushStatement(parameter.ParameterType, parameter.Name)%>;
                        <%if not parameter.IsOut and parameter.ParameterType.IsByRef and IsStruct(parameter.ParameterType:GetElementType()) then 
                  %>translator.Update(L, <%=(in_pos+param_offset)%>, <%=parameter.Name%>);
                        <%end%>
                    <%
                        end
                    end)
                    %>
                    <%if type.IsValueType and not method.IsStatic then%>
                        translator.Update(L, 1, __cl_gen_to_be_invoked);
                    <%end%>
                    
                    return <%=out_num+(has_return and 1 or 0)%>;
                }
                <% end)%>
            } catch(System.Exception __gen_e) {
                return LuaAPI.luaL_error(L, "c# exception:" + __gen_e +",stack:"+ __gen_e.StackTrace);
            }
            <%if method.Overloads.Count > 1 then%>
            return LuaAPI.luaL_error(L, "invalid arguments to <%=CsFullTypeName(type)%>.<%=method.Overloads[0].Name%>!");
            <%end%>
        }
        <% end)%>
        
        
        <%ForEachCsList(getters, function(getter) 
        if getter.IsStatic and getter.ReadOnly then return end --readonly static
        %>
        [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
        static int get_<%=getter.Name%>(RealStatePtr L)
        {
            <%if AccessorNeedTranslator(getter) then %>ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);<%end%>
            try {
			<%if not getter.IsStatic then%>
                <%=GetSelfStatement(type)%>;
                <%=GetPushStatement(getter.Type, "__cl_gen_to_be_invoked."..getter.Name)%>;<% else %>    <%=GetPushStatement(getter.Type, CsFullTypeName(type).."."..getter.Name)%>;<% end%>
            } catch(System.Exception __gen_e) {
                return LuaAPI.luaL_error(L, "c# exception:" + __gen_e +",stack:"+ __gen_e.StackTrace);
            }
            return 1;
        }
        <%end)%>
        
        <%ForEachCsList(setters, function(setter)
        local is_struct = IsStruct(setter.Type)
        %>
        [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
        static int set_<%=setter.Name%>(RealStatePtr L)
        {
            <%if AccessorNeedTranslator(setter) then %>ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);<%end%>
            try {
			<%if not setter.IsStatic then %>
                <%=GetSelfStatement(type)%>;
                <%if is_struct then %><%=GetCasterStatement(setter.Type, 2, "__cl_gen_value", true)%>;
				__cl_gen_to_be_invoked.<%=setter.Name%> = __cl_gen_value;<% else 
              %><%=GetCasterStatement(setter.Type, 2, "__cl_gen_to_be_invoked." .. setter.Name)%>;<%end
            else 
				if is_struct then %><%=GetCasterStatement(setter.Type, 1, "__cl_gen_value", true)%>;
				<%=CsFullTypeName(type)%>.<%=setter.Name%> = __cl_gen_value;<%else
          %>    <%=GetCasterStatement(setter.Type, 1, CsFullTypeName(type) .."." .. setter.Name)%>;<%end
            end%>
            <%if type.IsValueType and not setter.IsStatic then%>
                translator.Update(L, 1, __cl_gen_to_be_invoked);
            <%end%>
            } catch(System.Exception __gen_e) {
                return LuaAPI.luaL_error(L, "c# exception:" + __gen_e +",stack:"+ __gen_e.StackTrace);
            }
            return 0;
        }
        <%end)%>
		
		<%ForEachCsList(events, function(event) if not event.IsStatic then %>
        [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
        static int <%=event.Name%>(RealStatePtr L)
        {
            ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
			int __gen_param_count = LuaAPI.lua_gettop(L);
			<%=GetSelfStatement(type)%>;
            try {
                <%=GetCasterStatement(event.Type, 3, "__gen_delegate", true)%>;
                if (__gen_delegate == null) {
                    return LuaAPI.luaL_error(L, "#3 need <%=CsFullTypeName(event.Type)%>!");
                }
                
				<%if event.CanAdd then%>
				if (__gen_param_count == 3 && LuaAPI.lua_tostring(L, 2) == "+") {
					__cl_gen_to_be_invoked.<%=event.Name%> += __gen_delegate;
					return 0;
				} 
				<%end%>
				<%if event.CanRemove then%>
				if (__gen_param_count == 3 && LuaAPI.lua_tostring(L, 2) == "-") {
					__cl_gen_to_be_invoked.<%=event.Name%> -= __gen_delegate;
					return 0;
				} 
				<%end%>
			} catch(System.Exception __gen_e) {
                return LuaAPI.luaL_error(L, "c# exception:" + __gen_e +",stack:"+ __gen_e.StackTrace);
            }
			LuaAPI.luaL_error(L, "invalid arguments to <%=CsFullTypeName(type)%>.<%=event.Name%>!");
            return 0;
        }
        <%end end)%>
		
		<%ForEachCsList(events, function(event) if event.IsStatic then %>
        [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
        static int <%=event.Name%>(RealStatePtr L)
        {
            ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
			int __gen_param_count = LuaAPI.lua_gettop(L);
            try {
                <%=GetCasterStatement(event.Type, 2, "__gen_delegate", true)%>;
                if (__gen_delegate == null) {
                    return LuaAPI.luaL_error(L, "#2 need <%=CsFullTypeName(event.Type)%>!");
                }
                
				<%if event.CanAdd then%>
				if (__gen_param_count == 2 && LuaAPI.lua_tostring(L, 1) == "+") {
					<%=CsFullTypeName(type)%>.<%=event.Name%> += __gen_delegate;
					return 0;
				} 
				<%end%>
				<%if event.CanRemove then%>
				if (__gen_param_count == 2 && LuaAPI.lua_tostring(L, 1) == "-") {
					<%=CsFullTypeName(type)%>.<%=event.Name%> -= __gen_delegate;
					return 0;
				} 
				<%end%>
			} catch(System.Exception __gen_e) {
                return LuaAPI.luaL_error(L, "c# exception:" + __gen_e +",stack:"+ __gen_e.StackTrace);
            }
			return LuaAPI.luaL_error(L, "invalid arguments to <%=CsFullTypeName(type)%>.<%=event.Name%>!");
        }
        <%end end)%>
    }
}

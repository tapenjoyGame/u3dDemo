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
using System;
<%
require "TemplateCommon"

%>

namespace CSObjectWrap
{
    public class <%=CSVariableName(type)%>Bridge : LuaBase, <%=CsFullTypeName(type)%>
    {
		public <%=CSVariableName(type)%>Bridge(int reference, LuaEnv interpreter) : base(reference, interpreter)
        {
        }
		
        <%
        ForEachCsList(methods, function(method)
            local parameters = method:GetParameters()
            local in_num = CalcCsList(parameters, function(p) return p.IsIn or not p.IsOut end)
            local out_num = CalcCsList(parameters, function(p) return p.IsOut or p.ParameterType.IsByRef end)
            local in_pos = 0
            local has_return = (method.ReturnType.FullName ~= "System.Void")
            local return_type_name = has_return and CsFullTypeName(method.ReturnType) or "void"
            local out_idx = has_return and 2 or 1
			if has_return then out_num = out_num + 1 end
        %>
		public <%=return_type_name%> <%=method.Name%>(<%ForEachCsList(parameters, function(parameter, pi) 
			if pi ~= 0 then 
				%>, <% 
			end
			if parameter.IsOut then 
				%>out <%
			elseif parameter.ParameterType.IsByRef then
				%>ref <%
			end 
			%><%=CsFullTypeName(parameter.ParameterType)%> <%=parameter.Name%><% 
		end) %>)
		{
			RealStatePtr L = _Interpreter.L;
			int err_func = LuaAPI.load_error_func(L);
			<%if CallNeedTranslator(method, "") then %>ObjectTranslator translator = _Interpreter.translator;<%end%>
			
			LuaAPI.lua_getref(L, _Reference);
			LuaAPI.lua_pushstring(L, "<%=method.Name%>");
			if (0 != LuaAPI.xlua_pgettable(L, -2))
            {
				_Interpreter.ThrowExceptionFromError(err_func - 1);
			}
            if(!LuaAPI.lua_isfunction(L, -1))
            {
                LuaAPI.lua_pushstring(L, "no such function <%=method.Name%>");
                _Interpreter.ThrowExceptionFromError(err_func - 1);
            }
            LuaAPI.lua_pushvalue(L, -2);
            LuaAPI.lua_remove(L, -3);
			<%ForEachCsList(parameters, function(parameter) 
				if parameter.IsIn or not parameter.IsOut then 
					%><%=GetPushStatement(parameter.ParameterType, parameter.Name)%>;
			<% 
				end
			end) %>
			int __gen_error = LuaAPI.lua_pcall(L, <%=in_num + 1%>, <%=out_num%>, err_func);
            if (__gen_error != 0)
                _Interpreter.ThrowExceptionFromError(err_func - 1);
			
			<%ForEachCsList(parameters, function(parameter) 
				if parameter.IsOut or parameter.ParameterType.IsByRef then 
					%><%=GetCasterStatement(parameter.ParameterType, "err_func" .. (" + "..out_idx), parameter.Name)%>;
			<%
				out_idx = out_idx + 1
				end
			end) %>
			<%if has_return then %><%=GetCasterStatement(method.ReturnType, "err_func + 1", "__gen_ret", true)%>;<% end%>
			LuaAPI.lua_settop(L, err_func - 1);
			<%if has_return then %>return  __gen_ret;<% end%>
		}
        <%end)%>

        <%
        ForEachCsList(propertys, function(property)
        %>
        public <%=CsFullTypeName(property.PropertyType)%> <%=property.Name%> 
        {
            <%if property.CanRead then%>
            get 
            {
                RealStatePtr L = _Interpreter.L;
				int oldTop = LuaAPI.lua_gettop(L);
                <%if not JustLuaType(property.PropertyType) then %>ObjectTranslator translator = _Interpreter.translator;<%end%>
                LuaAPI.lua_getref(L, _Reference);
				LuaAPI.lua_pushstring(L, "<%=property.Name%>");
                if (0 != LuaAPI.xlua_pgettable(L, -2))
				{
					_Interpreter.ThrowExceptionFromError(oldTop);
				}
                <%=GetCasterStatement(property.PropertyType, "-1", "__gen_ret", true)%>;
                LuaAPI.lua_pop(L, 2);
                return __gen_ret;
            }
            <%end%>
            <%if property.CanWrite then%>
            set
            {
                RealStatePtr L = _Interpreter.L;
				int oldTop = LuaAPI.lua_gettop(L);
                <%if not JustLuaType(property.PropertyType) then %>ObjectTranslator translator = _Interpreter.translator;<%end%>
                LuaAPI.lua_getref(L, _Reference);
				LuaAPI.lua_pushstring(L, "<%=property.Name%>");
                <%=GetPushStatement(property.PropertyType, "value")%>;
                if (0 != LuaAPI.xlua_psettable(L, -3))
				{
					_Interpreter.ThrowExceptionFromError(oldTop);
				}
                LuaAPI.lua_pop(L, 1);
            }
            <%end%>
        }
        <%end)%>
        
        <%ForEachCsList(events, function(event) %>
        public event <%=CsFullTypeName(event.EventHandlerType)%> <%=event.Name%>;
        <%end)%>
		
		<%ForEachCsList(indexers, function(indexer) 
		local ptype = (indexer:GetGetMethod() or indexer:GetSetMethod()):GetParameters()[0].ParameterType
		%>
        public <%=CsFullTypeName(indexer.PropertyType)%> this[<%=CsFullTypeName(ptype)%> field] { 
		<%if indexer:GetGetMethod() then%>
		    get { return default(<%=CsFullTypeName(indexer.PropertyType)%>); }
		<%end%>
		<%if indexer:GetSetMethod() then%>
			set { } 
		<%end%>
		}
        <%end)%>
	}
}

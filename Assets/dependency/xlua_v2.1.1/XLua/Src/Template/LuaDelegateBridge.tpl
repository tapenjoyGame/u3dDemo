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
<%
require "TemplateCommon"
%>

namespace LuaInterface
{
    public partial class DelegateBridge : LuaBase
    {
		<%
		ForEachCsList(delegates, function(delegate)
		local parameters = delegate:GetParameters()
		local in_num = CalcCsList(parameters, function(p) return p.IsIn or not p.IsOut end)
		local out_num = CalcCsList(parameters, function(p) return p.IsOut or p.ParameterType.IsByRef end)
		local in_pos = 0
		local has_return = (delegate.ReturnType.FullName ~= "System.Void")
		local return_type_name = has_return and CsFullTypeName(delegate.ReturnType) or "void"
		local out_idx = has_return and 2 or 1
		if has_return then out_num = out_num + 1 end
		%>
		public <%=return_type_name%> <%=CSVariableName(delegate.ReturnType)%>(<%ForEachCsList(parameters, function(parameter, pi) 
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
			int err_func =LuaAPI.load_error_func(L);
			<%if CallNeedTranslator(delegate, "") then %>ObjectTranslator translator = _Interpreter.translator;<%end%>
			
			LuaAPI.lua_getref(L, _Reference);
			
			<%ForEachCsList(parameters, function(parameter) 
				if parameter.IsIn or not parameter.IsOut then 
					%><%=GetPushStatement(parameter.ParameterType, parameter.Name)%>;
			<% 
				end
			end) %>
			int __gen_error = LuaAPI.lua_pcall(L, <%=in_num%>, <%=out_num%>, err_func);
            if (__gen_error != 0)
                _Interpreter.ThrowExceptionFromError(err_func - 1);
			
			<%ForEachCsList(parameters, function(parameter) 
				if parameter.IsOut or parameter.ParameterType.IsByRef then 
					%><%=GetCasterStatement(parameter.ParameterType, "err_func" .. (" + "..out_idx), parameter.Name)%>;
			<%
				out_idx = out_idx + 1
				end
			end) %>
			<%if has_return then %><%=GetCasterStatement(delegate.ReturnType, "err_func + 1", "__gen_ret", true)%>;<% end%>
			LuaAPI.lua_settop(L, err_func - 1);
			<%if has_return then %>return  __gen_ret;<% end%>
		}
        <%end)%>
        
		public bool __Gen_Flag { get {return true;}}
	}
}
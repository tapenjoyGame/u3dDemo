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
<%
require "TemplateCommon"

local parameters = delegate:GetParameters()
local in_num = CalcCsList(parameters, function(p) return p.IsIn or not p.IsOut end)
local out_num = CalcCsList(parameters, function(p) return p.IsOut or p.ParameterType.IsByRef end)
local in_pos = 0
local has_return = (delegate.ReturnType.Name ~= "Void")
%>

namespace CSObjectWrap
{
    public class <%=CSVariableName(type)%>Wrap
    {
		public static void __Register(RealStatePtr L)
        {
            Dictionary<string, object> classFields = new Dictionary<string, object>();
            classFields["UnderlyingSystemType"] = typeof(<%=CsFullTypeName(type)%>);
            
            Utils.RegisterWrap(L, typeof(<%=CsFullTypeName(type)%>), __MetaFucntions_Dic, null, classFields);
        }
		
		static Dictionary<string, LuaCSFunction> __MetaFucntions_Dic = new Dictionary<string, LuaCSFunction>(){
            {"__call", __CallMeta},
			{"__add", __AddMeta},
			{"__sub", __SubMeta},
        };
		
		[MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
        static int __CallMeta(RealStatePtr L)
        {
			ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
			try {
				if(LuaAPI.lua_gettop(L) == <%=in_num+1%> && <%=GetCheckStatement(type, 1)%><%
					ForEachCsList(parameters, function(parameter) 
						if parameter.IsIn or not parameter.IsOut then in_pos = in_pos + 1; 
						%>&& <%=GetCheckStatement(parameter.ParameterType, in_pos+1)%><% 
						end 
					end)%>)
				{
                <% 
                in_pos = 0;
                ForEachCsList(parameters, function(parameter) 
                    %><% 
                    if parameter.IsIn or not parameter.IsOut then 
                        in_pos = in_pos + 1
                        %><%=GetCasterStatement(parameter.ParameterType, in_pos+1, parameter.Name, true)%><%
					else%><%=CsFullTypeName(parameter.ParameterType)%> <%=parameter.Name%><%end%>;
                <%end)%>
				<%=GetSelfStatement(type)%>;
				
				<%
                if has_return then
                    %>    <%=CsFullTypeName(delegate.ReturnType)%> __cl_gen_ret = <%
                end
                %>    __cl_gen_to_be_invoked( <%ForEachCsList(parameters, function(parameter, pi) if pi ~= 0 then %>, <% end; if parameter.IsOut then %>out <% elseif parameter.ParameterType.IsByRef then %>ref <% end %><%=parameter.Name%><% end) %> );
                <%
                if has_return then
                %>    <%=GetPushStatement(delegate.ReturnType, "__cl_gen_ret")%>;
                <%
                end
                local in_pos = 0
                ForEachCsList(parameters, function(parameter)
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
					
					return <%=out_num+(has_return and 1 or 0)%>;
				}
			} catch(System.Exception __gen_e) {
				return LuaAPI.luaL_error(L, "c# exception:" + __gen_e +",stack:"+ __gen_e.StackTrace);
			}
            return LuaAPI.luaL_error(L, "invalid arguments to Delegate <%=CsFullTypeName(type)%>!");
		}
		
		[MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
        static int __AddMeta(RealStatePtr L)
		{
			ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
			try {
				<%=GetCasterStatement(type, 1, "leftside", true)%>;
				<%=GetCasterStatement(type, 2, "rightside", true)%>;
				<%=GetPushStatement(type, "leftside + rightside")%>;
				return 1;
			} catch(System.Exception __gen_e) {
				return LuaAPI.luaL_error(L, "c# exception:" + __gen_e +",stack:"+ __gen_e.StackTrace);
			}
		}
		
		[MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
        static int __SubMeta(RealStatePtr L)
		{
			ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
			try {
				<%=GetCasterStatement(type, 1, "leftside", true)%>;
				<%=GetCasterStatement(type, 2, "rightside", true)%>;
				<%=GetPushStatement(type, "leftside - rightside")%>;
				return 1;
			} catch(System.Exception __gen_e) {
				return LuaAPI.luaL_error(L, "c# exception:" + __gen_e +",stack:"+ __gen_e.StackTrace);
			}
		}
	}
}
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
using System.Collections.Generic;
<%
require "TemplateCommon"
%>

namespace CSObjectWrap
{
    public class XLua_Gen_Initer_Register__
	{
	    static XLua_Gen_Initer_Register__()
        {
		    LuaInterface.LuaEnv.AddIniter((luaenv, translator) => {
			    <%ForEachCsList(wraps, function(wrap)%>
				translator.DelayWrapLoader(typeof(<%=CsFullTypeName(wrap)%>), <%=CSVariableName(wrap)%>Wrap.__Register);
				<%end)%>
				<%ForEachCsList(itf_bridges, function(itf_bridge)%>
				LuaInterface.Utils.RegisterInterfaceBridge(luaenv.L, typeof(<%=CsFullTypeName(itf_bridge)%>), typeof(<%=CSVariableName(itf_bridge)%>Bridge));
				<%end)%>
			});
		}
		
		
	}
	
}
namespace LuaInterface
{
	public partial class ObjectTranslator
	{
		static CSObjectWrap.XLua_Gen_Initer_Register__ s_gen_reg_dumb_obj = new CSObjectWrap.XLua_Gen_Initer_Register__();
		static CSObjectWrap.XLua_Gen_Initer_Register__ gen_reg_dumb_obj {get{return s_gen_reg_dumb_obj;}}
	}
}

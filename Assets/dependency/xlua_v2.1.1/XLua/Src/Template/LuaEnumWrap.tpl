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
%>

namespace CSObjectWrap
{
    <%ForEachCsList(types, function(type)%>
    public class <%=CSVariableName(type)%>Wrap
    {
		public static void __Register(RealStatePtr L)
        {
            Utils.RegisterEnumWrap(L, typeof(<%=CsFullTypeName(type)%>), new LuaCSFunction(__CastFrom));
        }
		
		[MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
        static int __CastFrom(RealStatePtr L)
		{
			ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
			return translator.TranslateToEnumToTop(L, typeof(<%=CsFullTypeName(type)%>), 1);
		}
	}
    <%end)%>
}
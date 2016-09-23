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
    public partial class ObjectTranslator
    {
        <%if type_infos.Count > 0 then
        local init_class_name = "IniterAdder" .. CSVariableName(type_infos[0].Type)
        %>
        class <%=init_class_name%>
        {
            static <%=init_class_name%>()
            {
            <%ForEachCsList(type_infos, function(type_info)
            if not type_info.Type.IsValueType then return end
            local full_type_name = CsFullTypeName(type_info.Type)
            %>
                AddIniter((translator) => {
                    translator.RegisterCustomOp(typeof(<%=full_type_name%>), 
                        (RealStatePtr L, object obj) => {
                            translator.Push(L, (<%=full_type_name%>)obj);
                        },
                        (RealStatePtr L, int idx) => {
                            <%=full_type_name%> val;
                            translator.Get(L, idx, out val);
                            return val;
                        },
                        (RealStatePtr L, int idx, object obj) => {
                            translator.Update(L, idx, (<%=full_type_name%>)obj);
                        }
                    );
                });
            <%end)%>
            }
        }
        
        static <%=init_class_name%> s_<%=init_class_name%>_dumb_obj = new <%=init_class_name%>();
        static <%=init_class_name%> <%=init_class_name%>_dumb_obj {get{return s_<%=init_class_name%>_dumb_obj;}}
        <%end%>
        
        <%ForEachCsList(type_infos, function(type_info)
        local type_id_var_name = CSVariableName(type_info.Type) .. '_TypeID'
        local full_type_name = CsFullTypeName(type_info.Type)
        %>int <%=type_id_var_name%> = -1;
        <%if type_info.Type.IsValueType then%>
        public void Push(RealStatePtr L, <%=full_type_name%> val)
        {
            if (<%=type_id_var_name%> == -1)
            {
                <%=type_id_var_name%> = getTypeId(L, typeof(<%=full_type_name%>));
            }
            IntPtr buff = LuaAPI.xlua_pushstruct(L, <%=type_info.Size%>, <%=type_id_var_name%>);
            if (!CopyByValue.Pack(buff, 0, val))
            {
                throw new Exception("pack fail:value="+val);
            }
        }
        public void Get(RealStatePtr L, int index, out <%=full_type_name%> val)
        {
            if (LuaAPI.lua_type(L, index) == LuaTypes.LUA_TUSERDATA)
            {
                IntPtr buff = LuaAPI.lua_touserdata(L, index);
                if (!CopyByValue.UnPack(buff, 0, out val))
                {
                    throw new Exception("uppack fail:value="+val);
                }
            }
            else
            {
                val = (<%=full_type_name%>)objectCasters.GetCaster(typeof(<%=full_type_name%>))(L, index, null);
            }
        }
        public void Update(RealStatePtr L, int index, <%=full_type_name%> val)
        {
            if (LuaAPI.lua_type(L, index) == LuaTypes.LUA_TUSERDATA)
            {
                IntPtr buff = LuaAPI.lua_touserdata(L, index);
                if (!CopyByValue.Pack(buff, 0, val))
                {
                    throw new Exception("pack fail:value="+val);
                }
            }
            else
            {
                throw new Exception("try to update a data with lua type:" + LuaAPI.lua_type(L, index));
            }
        }
        <%else%>
        public void Push(RealStatePtr L, <%=full_type_name%> o)
        {
            if (<%=type_id_var_name%> == -1)
            {
                <%=type_id_var_name%> = getTypeId(L, typeof(<%=full_type_name%>));
            }
            PushObject(L, o, <%=type_id_var_name%>);
        }
        <%end%>
        
        <%end)%>
        
    }
}
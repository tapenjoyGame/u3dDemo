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
    public static partial class CopyByValue
    {
        <%ForEachCsList(type_infos, function(type_info)
        local full_type_name = CsFullTypeName(type_info.Type)
        %>
        public static bool Pack(IntPtr buff, int offset, <%=full_type_name%> field)
        {
            <%
            local offset_inner = 0
            if not type_info.FieldGroup then
            ForEachCsList(type_info.FieldInfos, function(fieldInfo)
            %>
            if(!Pack(buff, offset<%=(offset_inner == 0 and "" or (" + " .. offset_inner))%>, field.<%=fieldInfo.Name%>))
            {
                return false;
            }
            <%
            offset_inner = offset_inner + fieldInfo.Size
            end)
            else
            ForEachCsList(type_info.FieldGroup, function(group)
            %>
            if(!LuaAPI.xlua_pack_float<%=(group.Count == 1 and "" or group.Count)%>(buff, offset<%=(offset_inner == 0 and "" or (" + " .. offset_inner))%><%
            ForEachCsList(group, function(fieldInfo, i)
            %>, field.<%=fieldInfo.Name%><%
            end)
            %>))
            {
                return false;
            }
            <%
            offset_inner = offset_inner + group.Count * 4
            end)
            end%>
            return true;
        }
        public static bool UnPack(IntPtr buff, int offset, out <%=full_type_name%> field)
        {
            field = default(<%=full_type_name%>);
            <%
            local offset_inner = 0
            if not type_info.FieldGroup then
            ForEachCsList(type_info.FieldInfos, function(fieldInfo)
            if fieldInfo.IsField then
            %>
            if(!UnPack(buff, offset<%=(offset_inner == 0 and "" or (" + " .. offset_inner))%>, out field.<%=fieldInfo.Name%>))
            {
                return false;
            }
            <%else%>
            var <%=fieldInfo.Name%> = field.<%=fieldInfo.Name%>;
            if(!UnPack(buff, offset<%=(offset_inner == 0 and "" or (" + " .. offset_inner))%>, out <%=fieldInfo.Name%>))
            {
                return false;
            }
            field.<%=fieldInfo.Name%> = <%=fieldInfo.Name%>;
            <%
            end
            offset_inner = offset_inner + fieldInfo.Size
            end)
            else
            ForEachCsList(type_info.FieldGroup, function(group)
            %>
            <%ForEachCsList(group, function(fieldInfo)%>float <%=fieldInfo.Name%> = default(float);
            <%end)%>
            if(!LuaAPI.xlua_unpack_float<%=(group.Count == 1 and "" or group.Count)%>(buff, offset<%=(offset_inner == 0 and "" or (" + " .. offset_inner))%><%
            ForEachCsList(group, function(fieldInfo)
            %>, out <%=fieldInfo.Name%><%
            end)
            %>))
            {
                return false;
            }
            <%ForEachCsList(group, function(fieldInfo)%>field.<%=fieldInfo.Name%> = <%=fieldInfo.Name%>;
            <%end)%>
            <%
            offset_inner = offset_inner + group.Count * 4
            end)
            end%>
            return true;
        }
        <%end)%>
    }
}
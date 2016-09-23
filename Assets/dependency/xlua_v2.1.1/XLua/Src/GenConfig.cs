using UnityEngine;
using System.Collections.Generic;
using System;

namespace LuaInterface
{
    //注意：用户自己代码不建议在这里配置，建议通过标签来声明!!
    public interface GenConfig 
    {
        //lua中要使用到C#库的配置，比如C#标准库，或者Unity API，第三方库等。
        List<Type> LuaCallCSharp { get; }

        //C#静态调用Lua的配置（包括事件的原型），仅可以配delegate，interface
        List<Type> CSharpCallLua { get; }

        //黑名单
        List<List<string>> BlackList { get; }
    }

    public interface GCOptimizeConfig
    {
        List<Type> TypeList { get; }
        Dictionary<Type, List<string>> AdditionalProperties { get; }
    }

    public class SysGCOptimize : GCOptimizeConfig
    {
        public List<Type> TypeList
        {
            get
            {
                return new List<Type>() {
                    typeof(Vector2),
                    typeof(Vector3),
                    typeof(Vector4),
                    typeof(Color),
                    typeof(Quaternion),
                    typeof(Ray),
                    typeof(Bounds),
                    typeof(Ray2D),
                };
            }
        }

        public Dictionary<Type, List<string>> AdditionalProperties
        {
            get
            {
                return new Dictionary<Type, List<string>>()
                {
                    { typeof(Ray), new List<string>() { "origin", "direction" } },
                    { typeof(Ray2D), new List<string>() { "origin", "direction" } },
                    { typeof(Bounds), new List<string>() { "center", "extents" } },
                };
            }
        }
    }
}
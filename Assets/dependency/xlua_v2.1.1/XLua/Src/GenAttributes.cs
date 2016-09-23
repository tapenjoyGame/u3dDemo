using System;

namespace LuaInterface
{
    public enum GenFlag
    {
        No = 0,
        GCOptimize = 1
    }

    //如果你要生成Lua调用CSharp的代码，加这个标签
    public class LuaCallCSharpAttribute : Attribute
    {
        GenFlag flag;
        public GenFlag Flag {
            get
            {
                return flag;
            }
        }

        public LuaCallCSharpAttribute(GenFlag flag = GenFlag.No)
        {
            this.flag = flag;
        }
    }

    //生成CSharp调用Lua，加这标签
    [AttributeUsage(AttributeTargets.Delegate | AttributeTargets.Interface)]
    public class CSharpCallLuaAttribute : Attribute
    {
    }

    //如果某属性、方法不需要生成，加这个标签
    public class BlackListAttribute : Attribute
    {

    }
}



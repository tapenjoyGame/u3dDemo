/**
 * singleton模式
 */
  
namespace jz
{
    public class jzSingleton<T> where T : new ()
    {
        public static T instance
        {
            get
            {
                return SingletonCreator.instance;
            }
        }

        class SingletonCreator
        {
            internal static readonly T instance = new T();
        }
    }

}


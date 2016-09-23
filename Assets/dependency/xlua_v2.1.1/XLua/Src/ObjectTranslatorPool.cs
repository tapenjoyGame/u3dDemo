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


namespace LuaInterface
{
	internal class ObjectTranslatorPool
	{
		private static volatile ObjectTranslatorPool instance = new ObjectTranslatorPool ();		
		private Dictionary<RealStatePtr, ObjectTranslator> translators = new Dictionary<RealStatePtr, ObjectTranslator>();
		
		public static ObjectTranslatorPool Instance
		{
			get
			{
				return instance;
			}
		}
		
		public ObjectTranslatorPool ()
		{
		}
		
		public void Add (RealStatePtr L, ObjectTranslator translator)
		{
			translators.Add(L , translator);			
		}

        RealStatePtr lastState = default(RealStatePtr);
        ObjectTranslator lastTranslator = default(ObjectTranslator);

		public ObjectTranslator Find (RealStatePtr L)
		{
            if (lastState == L) return lastTranslator;
            if (translators.ContainsKey(L))
            {
                lastState = L;
                lastTranslator = translators[L];
                return lastTranslator;
            }

			RealStatePtr main = Utils.GetMainState (L);

            if (translators.ContainsKey(main))
            {
                lastState = L;
                lastTranslator = translators[main];
                translators[L] = lastTranslator;
                return lastTranslator;
            }
			
			return null;
		}
		
		public void Remove (RealStatePtr L)
		{
			if (!translators.ContainsKey (L))
				return;
			
            if (lastState == L)
            {
                lastState = default(RealStatePtr);
                lastTranslator = default(ObjectTranslator);
            }
            ObjectTranslator translator = translators[L];
            List<RealStatePtr> toberemove = new List<RealStatePtr>();

            foreach(var kv in translators)
            {
                if (kv.Value == translator)
                {
                    toberemove.Add(kv.Key);
                }
            }

            foreach (var ls in toberemove)
            {
                translators.Remove(ls);
            }
        }
    }
}


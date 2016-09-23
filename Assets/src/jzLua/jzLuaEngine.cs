/**
 * lua引擎管理。
 */

using LuaInterface;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace jz
{
    public class jzLuaEngine : jzSingleton<jzLuaEngine>
    {
        private LuaEnv mLuaEnv = null;
        public LuaEnv luaEnv
        {
            get
            {
                return this.mLuaEnv;
            }
        }

        private LuaTable mScriptEnv;
        public LuaTable scriptEnv
        {
            get
            {
                return this.mScriptEnv;
            }
        }

        /**
         * 初始化lua引擎
         * @return  error code
         */
        public int init()
        {
            mLuaEnv = new LuaEnv();
            mLuaEnv.AddLoader(Loader());

            mScriptEnv = mLuaEnv.NewTable();
            LuaTable meta = mLuaEnv.NewTable();
            meta["__index"] = mLuaEnv.Global;
            mScriptEnv.SetMetaTable(meta);
            meta.Dispose();

            //加载lua模块默认的入口
            doScript("init");

            return 0;
        }

        public void dispose()
        {
            if(this.mLuaEnv != null)
            {
                this.mLuaEnv.Dispose();
                this.mLuaEnv = null;
            }

            this.mScriptEnv = null;
        }

        public int doString(string codes)
        {
            this.mLuaEnv.DoString(codes);
            return 0;
        }

        public int doScript(string fileName)
        {
            string codes = "require \"" + fileName + "\"";
            return doString(codes);
        }

        static List<string> paths = new List<string> { "" };

        static protected bool CheckPath(string filepath)
        {
#if !UNITY_EDITOR && UNITY_ANDROID
        using(UnityEngine.WWW www = new UnityEngine.WWW(filepath))
        {
            while (!www.isDone)
            {
            }

            if (string.IsNullOrEmpty(www.error))
            {
                return true;
            }
        }
#else
            if (File.Exists(filepath))
            {
                return true;
            }
#endif

            return false;
        }

        static protected byte[] LoadFromPath(string filepath)
        {
#if !UNITY_EDITOR && UNITY_ANDROID
        using(UnityEngine.WWW www = new UnityEngine.WWW(filepath))
        {
            while (!www.isDone)
            {
            }

            if (string.IsNullOrEmpty(www.error))
            {
                return System.Text.Encoding.UTF8.GetBytes(www.text);
            }
        }
#else
            if (File.Exists(filepath))
            {
                Stream stream = File.Open(filepath, FileMode.Open, FileAccess.Read);
                StreamReader reader = new StreamReader(stream);
                string text = reader.ReadToEnd();
                stream.Close();

                return System.Text.Encoding.UTF8.GetBytes(text);
            }
#endif

            return null;
        }

        static protected string BaseDir()
        {
            return Application.streamingAssetsPath + "/script/src/";
        }

        static internal byte[] CheckAndLoadFromPath(string path)
        {
            string baseDir = BaseDir();

            string filename = path.Replace('.', '/') + ".lua";

            string filepath = null;
            bool fileExist = false;
            for (int index = 0; index < paths.Count; index++)
            {
                var dir = paths[index];
                filepath = baseDir + dir + filename;

                if (CheckPath(filepath))
                {
                    fileExist = true;
                    break;
                }

                filepath = filepath.Replace(".lua", "/init.lua");
                if (CheckPath(filepath))
                {
                    fileExist = true;
                    break;
                }
            }

            if (fileExist)
            {
                byte[] bytes = LoadFromPath(filepath);

                return bytes;
            }
            else
            {
                Debug.LogError(string.Format("no such file '{0}' in path '{1}'!", filename, filepath));
            }

            return null;
        }

        public static LuaEnv.CustomLoader Loader()
        {
            return (ref string filename) =>
            {
                return CheckAndLoadFromPath(filename);
            };
        }
    }
}

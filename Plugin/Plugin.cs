using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using LuaInterface;
using UnityEngine;
using Mjslib;

namespace LeagueTablecloth
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency("vg.mjg.mjslib")]
    public class Plugin : BasePlugin
    {
        public const string Guid = "vg.mjg.league_tablecloth";
        public const string Name = "league_tablecloth";
        public const string Version = "0.3.0";

        internal static Plugin? Instance;

        public override void Load()
        {
            Instance = this;
            Log.LogInfo($"{Name} {Version} loading; deferring Lua setup to Mjslib.Lua.Ready");

            var dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            Tablecloth.Init(Path.Combine(dllDir, "assets"));

            Lua.Ready(OnLuaReady);
        }

        private void OnLuaReady(LuaState luaState)
        {
            Lua.RegisterNativeFunction(luaState, "LeagueTablecloth", "compose", Compose);
            Lua.RegisterNativeFunction(luaState, "LeagueTablecloth", "release", Release);
            Log.LogInfo("registered LeagueTablecloth.compose and LeagueTablecloth.release");

            var source = LoadEmbeddedLua();
            if (source == null)
            {
                Log.LogError("embedded tablecloth.lua resource missing; tablecloth disabled");
                return;
            }
            Lua.Load(source, "@LeagueTablecloth");
            Log.LogInfo("queued embedded tablecloth.lua chunk");
        }

        private static string? LoadEmbeddedLua()
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("LeagueTablecloth.tablecloth.lua");
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static int Compose(IntPtr L)
        {
            try
            {
                var nicknames = ReadNicknames(L, 1);
                int viewerSeat = 1;
                if (LuaDLL.lua_type(L, 2) == LuaTypes.LUA_TNUMBER)
                    viewerSeat = (int)Math.Round(LuaDLL.lua_tonumber(L, 2));

                var tex = Tablecloth.Compose(nicknames, viewerSeat);
                if (tex == null)
                {
                    LuaDLL.lua_pushnil(L);
                    return 1;
                }
                ToLua.Push(L, tex);
                return 1;
            }
            catch (Exception e)
            {
                Instance?.Log.LogError($"LeagueTablecloth.compose failed: {e}");
                LuaDLL.lua_pushnil(L);
                return 1;
            }
        }

        private static int Release(IntPtr L)
        {
            try
            {
                Tablecloth.Release();
            }
            catch (Exception e)
            {
                Instance?.Log.LogError($"LeagueTablecloth.release failed: {e}");
            }
            return 0;
        }

        private static string[] ReadNicknames(IntPtr L, int idx)
        {
            var result = new string[4] { "", "", "", "" };
            if (!LuaDLL.lua_istable(L, idx))
                return result;

            int n = LuaDLL.lua_objlen(L, idx);
            int max = Math.Min(n, 4);
            for (int i = 1; i <= max; i++)
            {
                LuaDLL.lua_rawgeti(L, idx, i);
                if (LuaDLL.lua_type(L, -1) == LuaTypes.LUA_TSTRING)
                    result[i - 1] = LuaDLL.lua_tostring(L, -1) ?? "";
                LuaDLL.lua_pop(L, 1);
            }
            return result;
        }
    }
}

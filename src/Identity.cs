using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LeagueTablecloth
{
    // nickname to triangle filename mapping
    // players.json is a flat object mapping each nickname to the asset it should display,
    // like { ">name1": "1.png", ">name2": "default.png", ... }
    internal static class Identity
    {
        public static Dictionary<string, string> LoadNicknameMap(string path)
        {
            var map = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(path))
                {
                    Log($"could not open players.json at {path}. all seats set default");
                    return map;
                }

                var text = File.ReadAllText(path);
                if (string.IsNullOrEmpty(text))
                {
                    Log($"players.json at {path} was empty. all seats set to default");
                    return map;
                }

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    Log("players.json is not a JSON object. all seats set default");
                    return map;
                }

                foreach (var entry in root.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.String) continue;
                    var key = entry.Name.Trim();
                    var file = entry.Value.GetString()!.Trim();
                    if (key.Length == 0 || file.Length == 0) continue;
                    map[key] = file; // last write wins on a duplicate nickname
                }

                Log($"loaded {map.Count} nickname mappings");
            }
            catch (Exception e)
            {
                // any parse/IO error leaves the map empty, so every seat falls through to default.png
                Log($"players.json failed to parse ({e.Message}); all seats -> default");
                return new Dictionary<string, string>();
            }

            return map;
        }

        public static string ResolveTriangle(Dictionary<string, string> map, string? nickname)
        {
            if (nickname != null)
            {
                var key = nickname.Trim();
                if (key.Length != 0 && map.TryGetValue(key, out var file))
                    return file;
            }
            return "default.png";
        }

        private static void Log(string msg) => Plugin.Instance?.Log.LogInfo($"[tablecloth] {msg}");
    }
}

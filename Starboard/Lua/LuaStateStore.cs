using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Overlay_Renderer.Helpers;

namespace Starboard.Lua
{
    internal static class LuaStateStore
    {
        private static readonly string RootDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Starboard",
            "LuaState");

        static LuaStateStore()
        {
            try
            {
                Directory.CreateDirectory(RootDir);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[LuaStateStore] Failed to create root dir '{RootDir}': {ex.Message}");
            }
        }

        public static Dictionary<string, object?> LoadState(string appId)
        {
            try
            {
                string safeName = MakeSafeFileName(appId);
                string path = Path.Combine(RootDir, safeName + ".json");

                if (!File.Exists(path))
                    return new Dictionary<string, object?>();

                var json = File.ReadAllText(path);
                var dict = JsonConvert.DeserializeObject<Dictionary<string, object?>>(json);
                return dict ?? new Dictionary<string, object?>();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[LuaStateStore] LoadState failed for appId='{appId}': {ex.Message}");
                return new Dictionary<string, object?>();
            }
        }

        public static void SaveState(string appId, Dictionary<string, object?> state)
        {
            try
            {
                string safeName = MakeSafeFileName(appId);
                string path = Path.Combine(RootDir, safeName + ".json");

                var json = JsonConvert.SerializeObject(state, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[LuaStateStore] SaveState failed for appId='{appId}': {ex.Message}");
            }
        }

        private static string MakeSafeFileName(string id)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                id = id.Replace(c, '_');

            return id;
        }
    }
}

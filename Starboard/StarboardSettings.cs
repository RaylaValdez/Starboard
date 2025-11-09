using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using Overlay_Renderer.Helpers;
using ImGuiNET;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Starboard
{
    internal sealed class StarboardSettings
    {
        public bool FirstRunCompleted { get; set; } = false;
        public VIRTUAL_KEY OpenMobiglassKeybindVk { get; set; } = VIRTUAL_KEY.VK_F1;
        public ImGuiKey OpenMobiglassKeybind { get; set; } = ImGuiKey.F1;
        public VIRTUAL_KEY OpenMobimapKeybindVk { get; set; } = VIRTUAL_KEY.VK_F2;
        public ImGuiKey OpenMobiMapKeybind { get; set; } = ImGuiKey.F2;
        public VIRTUAL_KEY OpenMobiCommsKeybindVk { get; set; } = VIRTUAL_KEY.VK_F11;
        public ImGuiKey OpenMobiCommsKeybind { get; set; } = ImGuiKey.F11;
        public bool UsesJoyPad { get; set; } = false;


    }

    internal sealed class StarboardSettingsStore
    {
        private static readonly string _baseDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Starboard");

        private static readonly string _settingsPath =
            Path.Combine(_baseDir, "StarboardSettings.json");

        public static StarboardSettings Current { get; private set; } = new();

        public static void Load()
        {
            try
            {
                Directory.CreateDirectory(_baseDir);

                if (!File.Exists(_settingsPath))
                {
                    Save();
                    return;
                }

                var json = File.ReadAllText(_settingsPath);
                var loaded = JsonSerializer.Deserialize<StarboardSettings>(json);

                if (loaded != null)
                    Current = loaded;
            }
            catch (Exception ex)
            {
                Logger.Warn($"StarboardSettings load failed: {ex.Message}");
                Current = new StarboardSettings();
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(_baseDir);

                var json = JsonSerializer.Serialize(
                    Current,
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                Logger.Warn($"StarboardSettings save failed: {ex.Message}");
            }
        }
    }
}

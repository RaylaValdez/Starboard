using ImGuiNET;
using Overlay_Renderer.Helpers;
using System.Text.Json;
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
        public bool DevMode { get; set; } = false;
        public List<ControllerBinding> OpenMobiglassControllerBinds { get; set; } = new() { new ControllerBinding() };
        public List<ControllerBinding> OpenMobimapControllerBinds { get; set; } = new() { new ControllerBinding() };
        public List<ControllerBinding> OpenMobicommsControllerBinds { get; set; } = new() { new ControllerBinding() };
        public float IdleCloseSeconds { get; set; } = 15f;
    }

    internal sealed class StarboardSettingsStore
    {
        private static readonly string _baseDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
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

                if (Current.OpenMobiglassControllerBinds == null || Current.OpenMobiglassControllerBinds.Count == 0)
                    Current.OpenMobiglassControllerBinds = new List<ControllerBinding> { new ControllerBinding() };

                if (Current.OpenMobimapControllerBinds == null || Current.OpenMobimapControllerBinds.Count == 0)
                    Current.OpenMobimapControllerBinds = new List<ControllerBinding> { new ControllerBinding() };

                if (Current.OpenMobicommsControllerBinds == null || Current.OpenMobicommsControllerBinds.Count == 0)
                    Current.OpenMobicommsControllerBinds = new List<ControllerBinding> { new ControllerBinding() };

                if (Current.IdleCloseSeconds <= 0)
                    Current.IdleCloseSeconds = 15f;
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

                JsonSerializerOptions jsonSerializerOptions = new() { WriteIndented = true };
                JsonSerializerOptions options = jsonSerializerOptions;
                var json = JsonSerializer.Serialize(
                    Current,
                    options);

                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                Logger.Warn($"StarboardSettings save failed: {ex.Message}");
            }
        }
    }
}

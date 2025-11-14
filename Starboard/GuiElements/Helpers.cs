using ImGuiNET;
using Overlay_Renderer.Methods;

namespace Starboard.GuiElements
{
    public static class Helpers
    {
        private static bool _mobiglassOpen = false;

        public static bool CheckMobiglassOpen()
        {
            var settings = StarboardSettingsStore.Current;

            bool keyboardToggle =
                ImGui.IsKeyPressed(settings.OpenMobiglassKeybind, false) ||
                ImGui.IsKeyPressed(settings.OpenMobiMapKeybind, false) ||
                ImGui.IsKeyPressed(settings.OpenMobiCommsKeybind, false);

            bool controllerToggle = false;
            while (ControllerInput.TryGetNextButtonPress(out var btn))
            {
                if (MatchesAnyControllerBinding(btn, settings.OpenMobiglassControllerBinds) ||
                    MatchesAnyControllerBinding(btn, settings.OpenMobimapControllerBinds) ||
                    MatchesAnyControllerBinding(btn, settings.OpenMobicommsControllerBinds))
                {
                    controllerToggle = true;
                }
            }

            bool escapePressed = ImGui.IsKeyPressed(ImGuiKey.Escape, false);

            if (keyboardToggle || controllerToggle)
            {
                _mobiglassOpen = !_mobiglassOpen;
            }

            if (escapePressed)
            {
                _mobiglassOpen = false;
            }

            return _mobiglassOpen;
        }

        public static void ForceCloseMobiglass()
        {
            _mobiglassOpen = false;
        }

        private static bool MatchesAnyControllerBinding(
            ControllerButton button,
            List<ControllerBinding> binds)
        {
            if (binds == null)
                return false;

            for (int i = 0; i < binds.Count; i++)
            {
                var b = binds[i];
                if (b.DeviceInstanceGuid == button.DeviceInstanceGuid &&
                    b.ButtonIndex == button.ButtonIndex)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

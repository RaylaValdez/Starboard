using ImGuiNET;
using System.Numerics;
using Overlay_Renderer.Methods;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Starboard.GuiElements
{
    internal static class HotkeyPicker
    {
        private static uint _activeId = 0;

        public static bool Draw(string label, Vector2 size, ref VIRTUAL_KEY boundVk, ref ImGuiKey boundKey)
        {
            ImGui.PushID(label);

            uint thisId = ImGui.GetID("##hotkey_picker");

            bool isActive = _activeId == thisId;

            string displayText;
            if (isActive)
                displayText = "Press a key...";
            else if (boundKey == ImGuiKey.None)
                displayText = "Not set";
            else
                displayText = boundKey.ToString();

            if (ImGui.Button(displayText, size))
            {
                _activeId = thisId;
                isActive = true;
            }

            bool changed = false;

            if (isActive)
            {
                if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
                {
                    _activeId = 0;
                }
                else if (ImGuiInput.TryGetPressedMappedKey(out var vk, out var imguiKey))
                {
                    boundVk = vk;
                    boundKey = imguiKey;
                    _activeId = 0;
                    changed = true;
                }
            }

            ImGui.PopID();
            return changed;
        }
    }
}

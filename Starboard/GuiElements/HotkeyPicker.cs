using ImGuiNET;
using System.Numerics;
using Overlay_Renderer.Methods;
using Windows.Win32.UI.Input.KeyboardAndMouse; // VIRTUAL_KEY

namespace Starboard.GuiElements
{
    internal static class HotkeyPicker
    {
        // Which control (by ImGui ID) is currently capturing, 0 = none
        private static uint _activeId = 0;

        public static bool Draw(string label, Vector2 size, ref VIRTUAL_KEY boundVk, ref ImGuiKey boundKey)
        {
            // Make this widget’s ID scope unique per label
            ImGui.PushID(label);

            // Use a *stable* internal ID for this picker
            uint thisId = ImGui.GetID("##hotkey_picker");

            bool isActive = _activeId == thisId;

            string displayText;
            if (isActive)
                displayText = "Press a key...";
            else if (boundKey == ImGuiKey.None)
                displayText = "Not set";
            else
                displayText = boundKey.ToString();

            // Render the button. Note: visible text can change,
            // but ID is based on "##hotkey_picker", not the text.
            if (ImGui.Button(displayText, size))
            {
                // Clicking sets this picker as the active one
                _activeId = thisId;
                isActive = true;
            }

            bool changed = false;

            if (isActive)
            {
                // ESC cancels capture
                if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
                {
                    _activeId = 0;
                }
                else if (ImGuiInput.TryGetPressedMappedKey(out var vk, out var imguiKey))
                {
                    boundVk = vk;
                    boundKey = imguiKey;
                    _activeId = 0;   // done capturing
                    changed = true;
                }
            }

            ImGui.PopID();
            return changed;
        }
    }
}

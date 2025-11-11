using ImGuiNET;
using System.Numerics;
using Overlay_Renderer.Methods;

namespace Starboard.GuiElements
{
    internal static class ControllerButtonPicker
    {
        private static uint _activeId = 0;

        public static bool Draw(string label, Vector2 size, ControllerBinding binding)
        {
            ImGui.PushID(label);

            uint thisId = ImGui.GetID("##controller_picker");
            bool isActive = _activeId == thisId;

            string displayText;
            if (isActive)
                displayText = "Press controller...";
            else
                displayText = binding?.ToString() ?? "Not set";

            bool changed = false;

            if (ImGui.Button(displayText, size))
            {
                _activeId = thisId;
                isActive = true;
            }

            if (isActive)
            {
                // Escape cancels
                if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
                {
                    _activeId = 0;
                }
                else if (ImGuiInput.TryGetPressedControllerButton(out var btn))
                {
                    binding?.SetFrom(btn);
                    _activeId = 0;
                    changed = true;
                }
            }

            ImGui.PopID();
            return changed;
        }
    }
}

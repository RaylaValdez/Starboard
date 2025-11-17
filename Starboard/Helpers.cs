using ImGuiNET;
using Overlay_Renderer.Methods;
using System.Numerics;

namespace Starboard
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

    // Starboard-wide colour palette
    internal static class StarboardPalette
    {
        // Base text
        public static readonly Vector4 Text = new(1.00f, 1.00f, 1.00f, 1.00f);
        public static readonly Vector4 TextDisabled = new(0.55f, 0.55f, 0.55f, 1.00f);

        // Backgrounds – slightly blue-tinted dark greys (RSI-ish)
        public static readonly Vector4 WindowBg = new(0.08f, 0.10f, 0.13f, 0.97f);
        public static readonly Vector4 ChildBg = new(0.10f, 0.12f, 0.16f, 0.98f);
        public static readonly Vector4 PopupBg = new(0.06f, 0.08f, 0.11f, 0.98f);

        // Borders / lines
        public static readonly Vector4 Border = new(1f, 1f, 1f, 0.35f);
        public static readonly Vector4 BorderSoft = new(0.25f, 0.30f, 0.35f, 0.60f);

        // **Main accent** – this is the ToggleSwitch “on” color
        // (0.00f, 0.78f, 0.60f, 1.00f)
        public static readonly Vector4 Accent = new(0.00f, 0.78f, 0.60f, 1.00f);

        // Hover / active variants of the accent
        public static readonly Vector4 AccentHover = new(0.10f, 0.90f, 0.75f, 1.00f);
        public static readonly Vector4 AccentActive = new(0.00f, 0.95f, 0.80f, 1.00f);

        // Muted accent for frames etc
        public static readonly Vector4 AccentFrame = new(0.00f, 0.45f, 0.40f, 1.00f);

        // Errors / warnings
        public static readonly Vector4 ErrorBg = new(0.80f, 0.10f, 0.10f, 0.95f);
        public static readonly Vector4 ErrorBorder = new(1.00f, 0.40f, 0.40f, 1.00f);
        public static readonly Vector4 Warning = new(0.98f, 0.78f, 0.16f, 1.00f);

        // Drag & drop highlight / progress bar fill
        public static readonly Vector4 DragTarget = new(0.00f, 0.78f, 0.60f, 0.80f);
        public static readonly Vector4 ProgressFill = new(0.00f, 0.78f, 0.60f, 1.00f);
        public static readonly Vector4 ProgressFillHover = new(0.10f, 0.90f, 0.75f, 1.00f);
    }

    public static class StarboardStyle
    {
        /// <summary>
        /// Apply the unified Starboard / Star Citizen-ish ImGui style.
        /// This replaces the old ImGuiStylePresets.ApplyDark().
        /// </summary>
        public static void ApplyStarboardStyle()
        {
            var style = ImGui.GetStyle();
            var colors = style.Colors;

            // Layout / rounding
            style.WindowRounding = 10f;
            style.ChildRounding = 8f;
            style.FrameRounding = 6f;
            style.ScrollbarRounding = 10f;
            style.GrabRounding = 6f;
            style.TabRounding = 6f;

            style.WindowBorderSize = 2f;
            style.FrameBorderSize = 0f;

            style.WindowPadding = new Vector2(10f, 8f);
            style.FramePadding = new Vector2(8f, 4f);
            style.ItemSpacing = new Vector2(6f, 4f);
            style.ItemInnerSpacing = new Vector2(6f, 3f);
            style.IndentSpacing = 18f;

            // --- Core text & background ---
            colors[(int)ImGuiCol.Text] = StarboardPalette.Text;
            colors[(int)ImGuiCol.TextDisabled] = StarboardPalette.TextDisabled;
            colors[(int)ImGuiCol.WindowBg] = StarboardPalette.WindowBg;
            colors[(int)ImGuiCol.ChildBg] = StarboardPalette.ChildBg;
            colors[(int)ImGuiCol.PopupBg] = StarboardPalette.PopupBg;
            colors[(int)ImGuiCol.Border] = StarboardPalette.Border;
            colors[(int)ImGuiCol.BorderShadow] = new Vector4(0, 0, 0, 0);

            // --- Frames / widgets ---
            colors[(int)ImGuiCol.FrameBg] = StarboardPalette.AccentFrame * new Vector4(1f, 1f, 1f, 0.40f);
            colors[(int)ImGuiCol.FrameBgHovered] = StarboardPalette.AccentFrame * new Vector4(1f, 1f, 1f, 0.70f);
            colors[(int)ImGuiCol.FrameBgActive] = StarboardPalette.AccentFrame * new Vector4(1f, 1f, 1f, 0.90f);

            colors[(int)ImGuiCol.CheckMark] = StarboardPalette.Accent;
            colors[(int)ImGuiCol.SliderGrab] = StarboardPalette.Accent;
            colors[(int)ImGuiCol.SliderGrabActive] = StarboardPalette.AccentHover;

            // --- Buttons ---
            colors[(int)ImGuiCol.Button] = new Vector4(0.35f, 0.35f, 0.37f, 0.50f);
            colors[(int)ImGuiCol.ButtonHovered] = StarboardPalette.AccentHover * new Vector4(1f, 1f, 1f, 0.80f);
            colors[(int)ImGuiCol.ButtonActive] = StarboardPalette.AccentActive;

            // --- Headers (e.g. selectable highlights, collapsibles) ---
            colors[(int)ImGuiCol.Header] = StarboardPalette.Accent * new Vector4(1f, 1f, 1f, 0.25f);
            colors[(int)ImGuiCol.HeaderHovered] = StarboardPalette.AccentHover * new Vector4(1f, 1f, 1f, 0.50f);
            colors[(int)ImGuiCol.HeaderActive] = StarboardPalette.AccentActive;

            // --- Tabs ---
            colors[(int)ImGuiCol.Tab] = StarboardPalette.ChildBg;
            colors[(int)ImGuiCol.TabHovered] = StarboardPalette.AccentHover * new Vector4(1f, 1f, 1f, 0.70f);
            colors[(int)ImGuiCol.TabSelected] = StarboardPalette.Accent * new Vector4(1f, 1f, 1f, 0.85f);
            colors[(int)ImGuiCol.TabDimmed] = StarboardPalette.ChildBg;
            colors[(int)ImGuiCol.TabDimmedSelected] = StarboardPalette.ChildBg;

            // --- Scrollbars ---
            colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0, 0, 0, 0);
            colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.31f, 0.31f, 0.34f, 1.00f);
            colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.41f, 0.41f, 0.44f, 1.00f);
            colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.51f, 0.51f, 0.55f, 1.00f);

            // --- Title bar / menu ---
            colors[(int)ImGuiCol.TitleBg] = StarboardPalette.WindowBg * new Vector4(1f, 1f, 1f, 0.90f);
            colors[(int)ImGuiCol.TitleBgActive] = StarboardPalette.WindowBg;
            colors[(int)ImGuiCol.TitleBgCollapsed] = StarboardPalette.WindowBg;
            colors[(int)ImGuiCol.MenuBarBg] = StarboardPalette.WindowBg;

            // --- Resizing / docking grips ---
            colors[(int)ImGuiCol.ResizeGrip] = StarboardPalette.Accent * new Vector4(1f, 1f, 1f, 0.20f);
            colors[(int)ImGuiCol.ResizeGripHovered] = StarboardPalette.AccentHover * new Vector4(1f, 1f, 1f, 0.60f);
            colors[(int)ImGuiCol.ResizeGripActive] = StarboardPalette.AccentActive;

            // --- Separators ---
            colors[(int)ImGuiCol.Separator] = StarboardPalette.BorderSoft;
            colors[(int)ImGuiCol.SeparatorHovered] = StarboardPalette.AccentHover;
            colors[(int)ImGuiCol.SeparatorActive] = StarboardPalette.AccentActive;

            // --- Tables / selection ---
            colors[(int)ImGuiCol.TableRowBg] = new Vector4(1f, 1f, 1f, 0.00f);
            colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(1f, 1f, 1f, 0.03f);

            // --- Progress bars / histograms (fix the weird yellow) ---
            colors[(int)ImGuiCol.PlotHistogram] = StarboardPalette.ProgressFill;
            colors[(int)ImGuiCol.PlotHistogramHovered] = StarboardPalette.ProgressFillHover;

            // --- Drag & drop highlight (fix the yellow rectangle) ---
            colors[(int)ImGuiCol.DragDropTarget] = StarboardPalette.DragTarget;

            // Optional: tweak nav highlights
            colors[(int)ImGuiCol.NavWindowingHighlight] = StarboardPalette.Accent * new Vector4(1f, 1f, 1f, 0.40f);
        }
    }
}


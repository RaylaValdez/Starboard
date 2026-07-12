using ImGuiNET;
using Overlay_Renderer.Methods;
using System.Numerics;

namespace Starboard
{
    /// <summary>
    /// Contains various Methods to make my life easier
    /// </summary>
    public static class Helpers
    {
        private enum MobiState { Closed, Opening, Open, Closing }
        private static MobiState _mobiState = MobiState.Closed;
        private static float _stateTimer = 0f;

        private static bool _prevAnyMobiKeyDown = false;
        private static bool _prevEscapeDown = false;

        private const float DebounceSeconds = 0.15f;

        /// <summary>
        /// State-machine based Mobiglass detection.
        /// Uses raw IsKeyDown (hardware state) instead of IsKeyPressed (queued events)
        /// so frame drops won't cause missed or duplicated toggles.
        /// </summary>
        public static bool CheckMobiglassOpen(float dt)
        {
            var settings = StarboardSettingsStore.Current;

            bool anyMobiKeyDown =
                ImGui.IsKeyDown(settings.OpenMobiglassKeybind) ||
                ImGui.IsKeyDown(settings.OpenMobiMapKeybind) ||
                ImGui.IsKeyDown(settings.OpenMobiCommsKeybind);

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
            if (controllerToggle)
                anyMobiKeyDown = true;

            bool keyJustPressed = anyMobiKeyDown && !_prevAnyMobiKeyDown;
            _prevAnyMobiKeyDown = anyMobiKeyDown;

            bool escapeDown = ImGui.IsKeyDown(ImGuiKey.Escape);
            bool escapeJustPressed = escapeDown && !_prevEscapeDown;
            _prevEscapeDown = escapeDown;

            switch (_mobiState)
            {
                case MobiState.Closed:
                    if (keyJustPressed)
                    {
                        _mobiState = MobiState.Opening;
                        _stateTimer = 0f;
                    }
                    break;

                case MobiState.Opening:
                    _stateTimer += dt;
                    if (escapeJustPressed)
                        _mobiState = MobiState.Closed;
                    else if (keyJustPressed)
                        _mobiState = MobiState.Closed;
                    else if (_stateTimer >= DebounceSeconds)
                        _mobiState = MobiState.Open;
                    break;

                case MobiState.Open:
                    if (keyJustPressed)
                    {
                        _mobiState = MobiState.Closing;
                        _stateTimer = 0f;
                    }
                    else if (escapeJustPressed)
                        _mobiState = MobiState.Closed;
                    break;

                case MobiState.Closing:
                    _stateTimer += dt;
                    if (escapeJustPressed)
                        _mobiState = MobiState.Closed;
                    else if (keyJustPressed)
                        _mobiState = MobiState.Open;
                    else if (_stateTimer >= DebounceSeconds)
                        _mobiState = MobiState.Closed;
                    break;
            }

            return _mobiState == MobiState.Open || _mobiState == MobiState.Opening;
        }

        /// <summary>
        /// Forces the flag for Starboard to be visible back to false
        /// </summary>
        public static void ForceCloseMobiglass()
        {
            _mobiState = MobiState.Closed;
            _stateTimer = 0f;
            _prevAnyMobiKeyDown = false;
            _prevEscapeDown = false;
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

    internal static class StarboardPalette
    {
        public static readonly Vector4 Text = new(1.00f, 1.00f, 1.00f, 1.00f);
        public static readonly Vector4 TextDisabled = new(0.55f, 0.55f, 0.55f, 1.00f);

        public static readonly Vector4 WindowBg = new(0.08f, 0.10f, 0.13f, 0.97f);
        public static readonly Vector4 ChildBg = new(0.10f, 0.12f, 0.16f, 0.98f);
        public static readonly Vector4 PopupBg = new(0.06f, 0.08f, 0.11f, 0.98f);

        public static readonly Vector4 Border = new(1f, 1f, 1f, 0.35f);
        public static readonly Vector4 BorderSoft = new(0.25f, 0.30f, 0.35f, 0.60f);

        public static readonly Vector4 Accent = new(0.00f, 0.78f, 0.60f, 1.00f);

        public static readonly Vector4 AccentHover = new(0.10f, 0.90f, 0.75f, 1.00f);
        public static readonly Vector4 AccentActive = new(0.00f, 0.95f, 0.80f, 1.00f);

        public static readonly Vector4 AccentFrame = new(0.00f, 0.45f, 0.40f, 1.00f);

        public static readonly Vector4 ErrorBg = new(0.80f, 0.10f, 0.10f, 0.95f);
        public static readonly Vector4 ErrorBorder = new(1.00f, 0.40f, 0.40f, 1.00f);
        public static readonly Vector4 Warning = new(0.98f, 0.78f, 0.16f, 1.00f);

        public static readonly Vector4 DragTarget = new(0.00f, 0.78f, 0.60f, 0.80f);
        public static readonly Vector4 ProgressFill = new(0.00f, 0.78f, 0.60f, 1.00f);
        public static readonly Vector4 ProgressFillHover = new(0.10f, 0.90f, 0.75f, 1.00f);
    }

    /// <summary>
    /// The Theme of the entire Starboard application.
    /// </summary>
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

            colors[(int)ImGuiCol.Text] = StarboardPalette.Text;
            colors[(int)ImGuiCol.TextDisabled] = StarboardPalette.TextDisabled;
            colors[(int)ImGuiCol.WindowBg] = StarboardPalette.WindowBg;
            colors[(int)ImGuiCol.ChildBg] = StarboardPalette.ChildBg;
            colors[(int)ImGuiCol.PopupBg] = StarboardPalette.PopupBg;
            colors[(int)ImGuiCol.Border] = StarboardPalette.Border;
            colors[(int)ImGuiCol.BorderShadow] = new Vector4(0, 0, 0, 0);

            colors[(int)ImGuiCol.FrameBg] = StarboardPalette.AccentFrame * new Vector4(1f, 1f, 1f, 0.40f);
            colors[(int)ImGuiCol.FrameBgHovered] = StarboardPalette.AccentFrame * new Vector4(1f, 1f, 1f, 0.70f);
            colors[(int)ImGuiCol.FrameBgActive] = StarboardPalette.AccentFrame * new Vector4(1f, 1f, 1f, 0.90f);

            colors[(int)ImGuiCol.CheckMark] = StarboardPalette.Accent;
            colors[(int)ImGuiCol.SliderGrab] = StarboardPalette.Accent;
            colors[(int)ImGuiCol.SliderGrabActive] = StarboardPalette.AccentHover;

            colors[(int)ImGuiCol.Button] = new Vector4(0.35f, 0.35f, 0.37f, 0.50f);
            colors[(int)ImGuiCol.ButtonHovered] = StarboardPalette.AccentHover * new Vector4(1f, 1f, 1f, 0.80f);
            colors[(int)ImGuiCol.ButtonActive] = StarboardPalette.AccentActive;

            colors[(int)ImGuiCol.Header] = StarboardPalette.Accent * new Vector4(1f, 1f, 1f, 0.25f);
            colors[(int)ImGuiCol.HeaderHovered] = StarboardPalette.AccentHover * new Vector4(1f, 1f, 1f, 0.50f);
            colors[(int)ImGuiCol.HeaderActive] = StarboardPalette.AccentActive;

            colors[(int)ImGuiCol.Tab] = StarboardPalette.ChildBg;
            colors[(int)ImGuiCol.TabHovered] = StarboardPalette.AccentHover * new Vector4(1f, 1f, 1f, 0.70f);
            colors[(int)ImGuiCol.TabSelected] = StarboardPalette.Accent * new Vector4(1f, 1f, 1f, 0.85f);
            colors[(int)ImGuiCol.TabDimmed] = StarboardPalette.ChildBg;
            colors[(int)ImGuiCol.TabDimmedSelected] = StarboardPalette.ChildBg;

            colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0, 0, 0, 0);
            colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.31f, 0.31f, 0.34f, 1.00f);
            colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.41f, 0.41f, 0.44f, 1.00f);
            colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.51f, 0.51f, 0.55f, 1.00f);

            colors[(int)ImGuiCol.TitleBg] = StarboardPalette.WindowBg * new Vector4(1f, 1f, 1f, 0.90f);
            colors[(int)ImGuiCol.TitleBgActive] = StarboardPalette.WindowBg;
            colors[(int)ImGuiCol.TitleBgCollapsed] = StarboardPalette.WindowBg;
            colors[(int)ImGuiCol.MenuBarBg] = StarboardPalette.WindowBg;

            colors[(int)ImGuiCol.ResizeGrip] = StarboardPalette.Accent * new Vector4(1f, 1f, 1f, 0.20f);
            colors[(int)ImGuiCol.ResizeGripHovered] = StarboardPalette.AccentHover * new Vector4(1f, 1f, 1f, 0.60f);
            colors[(int)ImGuiCol.ResizeGripActive] = StarboardPalette.AccentActive;

            colors[(int)ImGuiCol.Separator] = StarboardPalette.BorderSoft;
            colors[(int)ImGuiCol.SeparatorHovered] = StarboardPalette.AccentHover;
            colors[(int)ImGuiCol.SeparatorActive] = StarboardPalette.AccentActive;

            colors[(int)ImGuiCol.TableRowBg] = new Vector4(1f, 1f, 1f, 0.00f);
            colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(1f, 1f, 1f, 0.03f);

            colors[(int)ImGuiCol.PlotHistogram] = StarboardPalette.ProgressFill;
            colors[(int)ImGuiCol.PlotHistogramHovered] = StarboardPalette.ProgressFillHover;

            colors[(int)ImGuiCol.DragDropTarget] = StarboardPalette.DragTarget;

            colors[(int)ImGuiCol.NavWindowingHighlight] = StarboardPalette.Accent * new Vector4(1f, 1f, 1f, 0.40f);
        }
    }

    internal sealed class ControllerBinding
    {
        public Guid DeviceInstanceGuid { get; set; }
        public int ButtonIndex { get; set; }
        public string DeviceName { get; set; } = "";

        public override string ToString()
        {
            if (DeviceInstanceGuid == Guid.Empty)
                return "Not set";

            return $"{DeviceName} [Button {ButtonIndex + 1}]";
        }

        public void SetFrom(ControllerButton src)
        {
            DeviceInstanceGuid = src.DeviceInstanceGuid;
            ButtonIndex = src.ButtonIndex;
            DeviceName = src.DeviceName;
        }
    }
}


using ImGuiNET;
using System.Numerics;

namespace Starboard.GuiElements
{
    internal static class ToggleSwitch
    {
        private static readonly Dictionary<uint, float> _animT = new();

        /// <summary>
        /// Draws an animated toggle switch.
        /// </summary>
        /// <param name="id">Unique ID per control (per window).</param>
        /// <param name="size">Track size (width, height) in pixels.</param>
        /// <param name="value">Bool being toggled.</param>
        /// <param name="onText">Text to show for ON.</param>
        /// <param name="offText">Text to show for OFF.</param>
        /// <returns>True if value changed this frame.</returns>
        public static bool Draw(
            string id,
            Vector2 size,
            ref bool value,
            string onText = "ON",
            string offText = "OFF")
        {
            ImGui.PushID(id);

            uint thisId = ImGui.GetID("##toggle_switch");

            float height = size.Y > 0 ? size.Y : ImGui.GetFrameHeight();
            float width = size.X > 0 ? size.X : height * 2.4f;
            var trackSize = new Vector2(width, height);

            Vector2 pos = ImGui.GetCursorScreenPos();
            var dl = ImGui.GetWindowDrawList();
            float radius = height * 0.5f;

            ImGui.InvisibleButton("##btn", trackSize);
            bool hovered = ImGui.IsItemHovered();
            bool clicked = ImGui.IsItemClicked();

            bool oldValue = value;
            if (clicked)
                value = !value;

            float t = _animT.TryGetValue(thisId, out float stored) ? stored : (value ? 1f : 0f);
            float target = value ? 1f : 0f;

            float speed = 12f;
            float dt = ImGui.GetIO().DeltaTime;
            t += (target - t) * MathF.Min(1f, speed * dt);
            _animT[thisId] = t;

            Vector4 colOnBg = new(0.00f, 0.78f, 0.60f, 1.00f);
            Vector4 colOffBg = new(0.15f, 0.16f, 0.18f, 1.00f);
            Vector4 colBorder = new(0.90f, 0.90f, 0.95f, 1.00f);
            Vector4 colKnob = new(0.98f, 0.98f, 0.99f, 1.00f);

            if (hovered)
            {
                colOnBg = Lerp(colOnBg, new Vector4(1f, 1f, 1f, 1f), 0.10f);
                colOffBg = Lerp(colOffBg, new Vector4(1f, 1f, 1f, 1f), 0.08f);
            }

            Vector4 bg = Lerp(colOffBg, colOnBg, t);

            uint bgCol = ImGui.GetColorU32(bg);
            uint borderCol = ImGui.GetColorU32(colBorder);
            uint knobCol = ImGui.GetColorU32(colKnob);

            dl.AddRectFilled(pos, pos + trackSize, bgCol, radius);
            dl.AddRect(pos, pos + trackSize, borderCol, radius, ImDrawFlags.None, 1.5f);

            float innerWidth = width - 2f * radius;
            float knobX = pos.X + radius + innerWidth * t;
            Vector2 knobCenter = new(knobX, pos.Y + radius);
            float knobRadius = radius - 2.0f;

            dl.AddCircleFilled(knobCenter + new Vector2(0, 1.5f), knobRadius + 1.5f,
                               ImGui.GetColorU32(new Vector4(0, 0, 0, 0.35f)), 24);

            dl.AddCircleFilled(knobCenter, knobRadius, knobCol, 24);

            Vector2 onCenter = new(pos.X + width * 0.30f, pos.Y + height * 0.50f);
            Vector2 offCenter = new(pos.X + width * 0.70f, pos.Y + height * 0.50f);

            float onAlpha = Math.Clamp((t - 0.4f) / 0.2f, 0f, 1f);
            float offAlpha = Math.Clamp((0.6f - t) / 0.2f, 0f, 1f);

            Vector4 onTextCol = new(0.05f, 0.07f, 0.09f, onAlpha);
            Vector4 offTextCol = new(0.80f, 0.80f, 0.85f, offAlpha);

            if (!string.IsNullOrEmpty(onText))
            {
                var ts = ImGui.CalcTextSize(onText);
                var p = new Vector2(onCenter.X - ts.X * 0.5f, onCenter.Y - ts.Y * 0.5f);
                dl.AddText(p, ImGui.GetColorU32(onTextCol), onText);
            }

            if (!string.IsNullOrEmpty(offText))
            {
                var ts = ImGui.CalcTextSize(offText);
                var p = new Vector2(offCenter.X - ts.X * 0.5f, offCenter.Y - ts.Y * 0.5f);
                dl.AddText(p, ImGui.GetColorU32(offTextCol), offText);
            }

            ImGui.PopID();
            return value != oldValue;
        }

        private static Vector4 Lerp(Vector4 a, Vector4 b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return new Vector4(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                a.Z + (b.Z - a.Z) * t,
                a.W + (b.W - a.W) * t
            );
        }
    }
}

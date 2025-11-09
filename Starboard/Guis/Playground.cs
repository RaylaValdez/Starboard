using ImGuiNET;
using Overlay_Renderer.Methods;
using Starboard.GuiElements;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Starboard.Guis
{
    internal class Playground
    {
        private static Rectangle _mobiFramePx;
        private static float _dpiScale = 1f;

        private static readonly MobiPillButton _pill = new();

        public static void Initialize(IntPtr cassioTex, float dpiScale, Rectangle mobiFrame)
        {
            _dpiScale = dpiScale;
            _mobiFramePx = mobiFrame;

            _pill.Initialize(cassioTex, dpiScale, mobiFrame);
        }

        public static void SetMobiFrame(Rectangle mobiFrame)
        {
            _mobiFramePx = mobiFrame;
            _pill.UpdateMobiFrame(mobiFrame);
        }

        public static void Draw(float dt)
        {
            // Simple debug window
            ImGui.Begin("Starboard Debug", ImGuiWindowFlags.AlwaysAutoResize);
            ImGui.Text("Starboard Overlay");
            ImGui.Text($"Mobi frame: {_mobiFramePx.Width} x {_mobiFramePx.Height}");
            ImGui.Text($"DPI scale: {_dpiScale:F2}");
            HitTestRegions.AddCurrentWindow();
            ImGui.End();

            // All pill UI (tuning window + actual pill) is delegated to MobiPillButton
            _pill.Draw(dt);
        }
    }
}

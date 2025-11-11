using ImGuiNET;
using Overlay_Renderer.Methods;
using Starboard.GuiElements;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Starboard.Guis
{
    internal class Playground
    {
        private static Rectangle _mobiFramePx;
        private static float _dpiScale = 1f;

        private static readonly MobiPillButton _pill = new();

        private static bool _mobiOpen = false;
        private static bool _mobiOpenLastFrame = false;

        public static void Initialize(IntPtr cassioTex, float dpiScale, Rectangle mobiFrame)
        {
            _dpiScale = dpiScale;
            _mobiFramePx = mobiFrame;
        }

        public static void SetMobiFrame(Rectangle mobiFrame)
        {
            _mobiFramePx = mobiFrame;
        }

        public static void Draw(float dt)
        {
            var io = ImGui.GetIO();

            // Simple debug window
            ImGui.Begin("Starboard Debug", ImGuiWindowFlags.AlwaysAutoResize);
            ImGui.Text("Starboard Overlay");
            ImGui.Text($"Mobi frame: {_mobiFramePx.Width} x {_mobiFramePx.Height}");
            ImGui.Text($"DPI scale: {_dpiScale:F2}");
            HitTestRegions.AddCurrentWindow();
            ImGui.End();

            // Central mobiglass state logic
            _mobiOpen = Helpers.CheckMobiglassOpen();

            // Detect transitions
            if (!_mobiOpen && _mobiOpenLastFrame)
            {
                // Mobiglass just closed → reset main window state
                StarboardMain.ResetOnMobiClosed();
            }

            _mobiOpenLastFrame = _mobiOpen;

            if (_mobiOpen)
            {
                StarboardMain.Draw(dt);
            }
        }

    }
}

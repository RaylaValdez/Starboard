using ImGuiNET;
using Overlay_Renderer.Methods;
using Starboard.GuiElements;
using System.Drawing;
using System.Numerics;

namespace Starboard.Guis
{
    internal class Playground
    {
        private static Rectangle _mobiFramePx;
        private static float _dpiScale = 1f;

        private static readonly MobiPillButton _pill = new();

        private static bool _mobiOpen = false;
        private static bool _mobiOpenLastFrame = false;

        private static ImFontPtr _orbiBoldFont;
        private static ImFontPtr _orbiRegFont;
        private static ImFontPtr _orbiRegFontSmall;

        private static IntPtr _cassioTex = IntPtr.Zero;

        // --- Loading + fade sequencing ---
        private enum Phase { FadeIn, Preload, FadeOut, Done }
        private static Phase _phase = Phase.FadeIn;

        private static bool _isLoaded = false;        // mirrors completion for your status text
        private static bool _preloadActive = true;    // drives the progress bar
        private static float _preloadTime = 0f;

        private const float FadeInDuration = 2.0f;  // seconds
        private const float PreloadDuration = 5.0f;  // seconds
        private const float FadeOutDuration = 1.0f;  // seconds

        private static float _fadeInTime = 0f;
        private static float _fadeOutTime = 0f;

        public static void Initialize(
            IntPtr cassioTex,
            float dpiScale,
            Rectangle mobiFrame,
            ImFontPtr fontBold,
            ImFontPtr font,
            ImFontPtr smallFont)
        {
            _dpiScale = dpiScale;
            _mobiFramePx = mobiFrame;
            _orbiBoldFont = fontBold;
            _orbiRegFont = font;
            _orbiRegFontSmall = smallFont;
            _cassioTex = cassioTex;

            // reset sequence
            _phase = Phase.FadeIn;
            _isLoaded = false;
            _preloadActive = true;
            _preloadTime = 0f;
            _fadeInTime = 0f;
            _fadeOutTime = 0f;
        }

        public static void SetMobiFrame(Rectangle mobiFrame)
        {
            _mobiFramePx = mobiFrame;
        }

        public static void Draw(float dt)
        {
            // --- Phase progression + global alpha ---
            float globalAlpha = 1f;

            switch (_phase)
            {
                case Phase.FadeIn:
                {
                    _fadeInTime = MathF.Min(_fadeInTime + dt, FadeInDuration);
                    float t = _fadeInTime / FadeInDuration;         // 0..1
                    // smoothstep for nicer ease
                    globalAlpha = t * t * (3f - 2f * t);

                    if (_fadeInTime >= FadeInDuration)
                    {
                        _phase = Phase.Preload;
                    }
                    break;
                }
                case Phase.Preload:
                {
                    globalAlpha = 1f;

                    if (_preloadActive)
                    {
                        _mobiOpen = true;
                        _preloadTime = MathF.Min(_preloadTime + dt, PreloadDuration);
                        if (_preloadTime >= PreloadDuration)
                        {
                            _preloadActive = false;
                            _isLoaded = true;
                            _phase = Phase.FadeOut;
                            _mobiOpen = false;
                        }
                    }
                    break;
                }
                case Phase.FadeOut:
                {
                    _fadeOutTime = MathF.Min(_fadeOutTime + dt, FadeOutDuration);
                    float t = _fadeOutTime / FadeOutDuration;       // 0..1
                    float eased = t * t * (3f - 2f * t);
                    globalAlpha = 1f - eased;

                    if (_fadeOutTime >= FadeOutDuration)
                    {
                        _phase = Phase.Done;
                        // hide the loading window completely now
                    }
                    break;
                }
                case Phase.Done:
                {
                    // Nothing to draw for the loading window anymore.
                    // Still continue with main UI below if mobi is open.
                    break;
                }
            }

            // Draw the loading window for all phases except Done
            if (_phase != Phase.Done)
            {
                ImGui.SetNextWindowSize(new Vector2(500f, 150f), ImGuiCond.Always);
                ImGui.SetNextWindowPos(new Vector2(50f, 50f), ImGuiCond.Always);

                // Apply global alpha for the fade effect (affects all contents)
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, globalAlpha);

                ImGui.Begin("##StarboardLoading",
                    ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoResize |
                    ImGuiWindowFlags.NoCollapse |
                    ImGuiWindowFlags.NoSavedSettings |
                    ImGuiWindowFlags.NoTitleBar);

                float windowWidth = ImGui.GetWindowSize().X;

                if (_cassioTex != IntPtr.Zero)
                {
                    float iconH = 64f * _dpiScale;
                    var iconSize = new Vector2(iconH, iconH);
                    float iconX = (windowWidth - iconH) * 0.025f;
                    ImGui.SetCursorPosX(iconX);
                    ImGui.Image(_cassioTex, iconSize);
                }

                ImGui.SameLine();

                float textHeight = ImGui.GetTextLineHeight();
                float yOffset = 38f * _dpiScale - textHeight;
                if (yOffset < 0) yOffset = 0;
                var cursor = ImGui.GetCursorPos();
                ImGui.SetCursorPosY(cursor.Y + yOffset);

                unsafe
                {
                    if (_orbiBoldFont.NativePtr != null)
                        ImGui.PushFont(_orbiBoldFont);
                }

                ImGui.Text($"Starboard is {(_isLoaded ? "loaded" : "loading")}");

                ImGui.Dummy(new Vector2(0f, 6f * _dpiScale));

                // Progress only during Preload (show full bar during FadeOut if you like)
                if (_phase == Phase.Preload)
                {
                    float t = _preloadTime / PreloadDuration;
                    float eased = t * t * (3f - 2f * t);
                    string overlay = $"{MathF.Round(eased * 100f)}%";
                    ImGui.ProgressBar(eased, new Vector2(-1, 0), overlay);
                }
                else if (_phase == Phase.FadeOut)
                {
                    // Keep a full bar visible while fading out (looks nice)
                    ImGui.ProgressBar(1f, new Vector2(-1, 0), "100%");
                }

                unsafe
                {
                    if (_orbiBoldFont.NativePtr != null)
                        ImGui.PopFont();
                }

                HitTestRegions.AddCurrentWindow();
                ImGui.End();

                ImGui.PopStyleVar(); // Alpha
            }

            // --- Central mobiglass state logic continues as before ---
            _mobiOpen = Helpers.CheckMobiglassOpen();

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

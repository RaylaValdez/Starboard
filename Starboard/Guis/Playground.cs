using ImGuiNET;
using OpenCvSharp;
using Overlay_Renderer.Methods;
using System.Drawing;
using System.Numerics;

namespace Starboard.Guis
{
    internal class Playground
    {
        private static Rectangle _mobiFramePx;
        private static float _dpiScale = 1f;

        private static bool _mobiOpen = false;
        private static bool _mobiOpenLastFrame = false;

        private static ImFontPtr _orbiBoldFont;
        private static ImFontPtr _orbiRegFont;
        private static ImFontPtr _orbiRegFontSmall;

        private static IntPtr _cassioTex = IntPtr.Zero;

        // --- Loading + fade sequencing ---
        private enum Phase { FadeIn, Preload, FadeOut, Done }
        private static Phase _phase = Phase.FadeIn;

        private static bool _isLoaded = false;        
        private static bool _preloadActive = true;    
        private static float _preloadTime = 0f;

        private const float FadeInDuration = 0.25f;
        private const float FadeOutDuration = 0.4f;  

        private static float _fadeInTime = 0f;
        private static float _fadeOutTime = 0f;

        private static float _preloadDuration;
        private static readonly Random _rng = new Random();

        // --- StarboardMain fade knobs (hardcoded, tweak here) ---
        private const float MainFadeInSeconds = 0.1f;
        private const float MainFadeOutSeconds = 0.4f;
        private static float _mainFadeT = 0f; 

        // --- StarboardMain visibility during loading phases ---
        private const bool ShowMainDuringFadeIn = false;
        private const bool ShowMainDuringPreload = true;
        private const bool ShowMainDuringFadeOut = false;


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

            _mainFadeT = 0f;
            _mobiOpen = false;
            _mobiOpenLastFrame = false;

            _preloadDuration = RandRange(0.5f, 3f);
        }

        public static void SetMobiFrame(Rectangle mobiFrame)
        {
            _mobiFramePx = mobiFrame;
        }

        public static void Draw(float dt)
        {
            bool logicalMobiOpen = Helpers.CheckMobiglassOpen();

            if (!logicalMobiOpen && _mobiOpenLastFrame)
            {
                StarboardMain.ResetOnMobiClosed();
            }
          
            float globalAlpha = 1f;
            bool overlayFromLoading = 
                (_phase == Phase.FadeIn && ShowMainDuringFadeIn) ||
                (_phase == Phase.Preload && ShowMainDuringPreload) ||
                (_phase == Phase.FadeOut &&  ShowMainDuringFadeOut);

            switch (_phase)
            {
                case Phase.FadeIn:
                {
                    _fadeInTime = MathF.Min(_fadeInTime + dt, FadeInDuration);
                    float t = _fadeInTime / FadeInDuration;         // 0..1
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
                        _preloadTime = MathF.Min(_preloadTime + dt, _preloadDuration);
                        if (_preloadTime >= _preloadDuration)
                        {
                            _preloadActive = false;
                            _isLoaded = true;
                            _phase = Phase.FadeOut;
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
                    }
                    break;
                }
                case Phase.Done:
                {
                    break;
                }
            }

            if (_phase != Phase.Done)
            {
                ImGui.SetNextWindowSize(new Vector2(500f, 150f), ImGuiCond.Always);

                if (_mobiFramePx.Width > 0 && _mobiFramePx.Height > 0)
                {
                    var mobiTopLeft = new Vector2(_mobiFramePx.Left, _mobiFramePx.Top);
                    var offset = new Vector2(50f, 50f);
                    ImGui.SetNextWindowPos(mobiTopLeft + offset, ImGuiCond.Always);
                }

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

                if (_phase == Phase.Preload)
                {
                    float t = _preloadTime / _preloadDuration;
                    float eased = t * t * (3f - 2f * t);

                    int pct = (int)MathF.Round(eased * 100f);
                    string overlay = pct + "%";

                    if (pct == 69)
                        overlay = "Nice.";

                    ImGui.ProgressBar(eased, new Vector2(-1, 0), overlay);
                }
                else if (_phase == Phase.FadeOut)
                {
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

            bool overlayRequested = overlayFromLoading || logicalMobiOpen;

            float idleTimeout = StarboardSettingsStore.Current.IdleCloseSeconds;
            if (idleTimeout <= 0f)
                idleTimeout = 15f;

            bool idleCloseActive = (_phase == Phase.Done);
            bool idleExpired = idleCloseActive && StarboardMain.SecondsSinceInteraction >= idleTimeout;

            if (idleExpired && _mobiOpen)
            {
                StarboardMain.ResetOnMobiClosed();
                Helpers.ForceCloseMobiglass();
                logicalMobiOpen = false;
            }

            bool targetVisible = overlayRequested && !idleExpired;

            float fadeInSec = MainFadeInSeconds;
            float fadeOutSec = MainFadeOutSeconds;

            if (fadeInSec <= 0.01f) fadeInSec = 0.01f;
            if (fadeOutSec <= 0.01f) fadeOutSec = 0.01f;

            if (targetVisible)
            {
                _mainFadeT = MathF.Min(1f, _mainFadeT + dt / fadeInSec);
            }
            else
            {
                _mainFadeT = MathF.Max(0f, _mainFadeT - dt / fadeOutSec);
            }

            float mainAlpha = _mainFadeT * _mainFadeT * (3f - 2f * _mainFadeT);

            bool drawMain = mainAlpha > 0.001f;

            if (drawMain)
            {
                StarboardMain.Draw(dt, mainAlpha);
            }

            _mobiOpen = drawMain;
            _mobiOpenLastFrame = _mobiOpen;
        }

        private static float RandRange(float min, float max)
        {
            return (float)(_rng.NextDouble() * (max - min) + min);
        }
    }
}

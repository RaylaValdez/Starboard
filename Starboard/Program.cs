using ImGuiNET;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text.Json;
using Windows.System;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Starboard;

internal static class Program
{
    // --- Constants ---
    private const int HotkeyIdF10 = 1; // interactive / click-through
    private const int HotkeyIdF1 = 2; // overlay visible toggle
    private const int HotkeyIdF2 = 3;
    private const ushort VkF10 = 0x79;
    private const ushort VkF1 = 0x70;
    private const ushort VkF2 = 0x71;
    private const int MouseWheelDeltaPerNotch = 120;
    private const int SleepWhenIdleMs = 50;
    private const int TrackerIntervalMs = 16;

    // Mobi frame (kept for anchor math)
    private static Rectangle _mobiFramePx;
    private static Rectangle _roiPixels;

    // ---- Runtime-tweakable pill layout (persisted to pill_layout.json) ----
    private sealed class PillLayout
    {
        public float BarHeightRel { get; set; } = 0.104f; // bar height / frame height
        public float PillHeightFrac { get; set; } = 0.78f;  // pill height / bar height
        public float PillWidthToHeight { get; set; } = 2.35f;  // pill width = height * ratio
        public float RightMarginRel { get; set; } = 0.028f; // from frame right
        public float BottomMarginRel { get; set; } = 0.020f; // from frame bottom
        public float InnerPadFrac { get; set; } = 0.18f;  // inner padding as % of pill H
        public float IconHeightFrac { get; set; } = 0.55f;  // icon height as % of inner box H
        public float BorderThickness { get; set; } = 2.0f;   // border px (scaled by DPI)
        public float ShadowOpacity { get; set; } = 0.35f; // 0..1
        public float ShadowMaxOffsetPx { get; set; } = 10.0f; // max offset at edges, before DPI
        public int ShadowBlurTaps { get; set; } = 4;     // 2..6 is plenty
        public float ShadowDownBias { get; set; } = 0.30f; // extra downward push (0..1)
        // --- Hover animation knobs ---
        public float HoverLiftFrac { get; set; } = 0.12f; // icon lift = iconH * this * hoverT
        public float HoverScaleFrac { get; set; } = 0.06f; // icon scale delta on hover
        public float HoverTiltWidenFrac { get; set; } = 0.08f; // top edge widens by iconW * this * hoverT
        public float HoverBgBrighten { get; set; } = 0.12f; // extra bg alpha added on hover
        public float HoverSpeed { get; set; } = 10.0f; // approach speed for hoverT easing
        public float ShadowHoverBoost { get; set; } = 0.60f; // extra shadow offset multiplier on hover


    }
    private static PillLayout _pill = new PillLayout();
    private static readonly string _pillCfgPath = "pill_layout.json";
    private static bool _showPillTuning = true; // toggle with F2

    private const float relX = 0.14f;  // ROI you used before (kept for debug rect)
    private const float relY = 0.898f;
    private const float relW = 0.075f;
    private const float relH = 0.10f;

    private static HWND _starCitizenHwnd;
    private static bool _isInteractive = false;
    private static float _accumulatedWheel = 0;
    private static float _dpiScale = 1.0f;

    // last-known rect of the Pill Tuning window (so we can make it clickable)
    private static Vector2 _tuningWinPos, _tuningWinSize;


    private static float _pillHoverT = 0f;         // 0..1, animated
    private static readonly System.Diagnostics.Stopwatch _animClock = System.Diagnostics.Stopwatch.StartNew();
    private static double _lastAnimSec = 0;
    private static float _dt = 1f / 120f;         // per-frame dt for our animation


    // Texture for the logo
    private static IntPtr _cassioTex = IntPtr.Zero;
    private static int _cassioW, _cassioH;

    [STAThread]
    private static void Main()
    {
        if (!TryFindStarCitizenWindow(out _starCitizenHwnd))
        {
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < until && !TryFindStarCitizenWindow(out _starCitizenHwnd))
                Thread.Sleep(250);
            if (_starCitizenHwnd == HWND.Null) return;
        }

        using var overlay = new OverlayWindow("Starboard Overlay", _starCitizenHwnd);
        using var d3d = new D3DHost(overlay.Hwnd);
        using var imgui = new ImGuiRendererD3D11(d3d.Device, d3d.Context);

        // Load the Cassiopeia icon once
        try
        {
            _cassioTex = imgui.CreateTextureFromFile("Assets/Icons/cassiopia.png", out _cassioW, out _cassioH);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to load icon: {ex.Message}");
            _cassioTex = IntPtr.Zero;
        }

        // Place overlay initially
        PInvoke.GetClientRect(_starCitizenHwnd, out RECT clientRect);
        var topLeft = new System.Drawing.Point(0, 0);
        PInvoke.ClientToScreen(_starCitizenHwnd, ref topLeft);
        overlay.UpdateBounds(new RECT
        {
            left = topLeft.X,
            top = topLeft.Y,
            right = topLeft.X + clientRect.right,
            bottom = topLeft.Y + clientRect.bottom
        });
        overlay.Visible = false; // F1 controls

        try { uint dpi = PInvoke.GetDpiForWindow(_starCitizenHwnd); _dpiScale = MathF.Max(1.0f, dpi / 96.0f); } catch { }

        _mobiFramePx = ComputeMobiFrame(clientRect.right, clientRect.bottom);
        _roiPixels = RoiFromMobiRelative(_mobiFramePx, relX, relY, relW, relH);

        LoadPillLayout(); // if file exists

        var cts = new CancellationTokenSource();
        new Thread(() => TrackStarCitizen(cts.Token, overlay)) { IsBackground = true }.Start();

        RegisterKeyboardRawInput(overlay.Hwnd);
        PInvoke.RegisterHotKey(HWND.Null, HotkeyIdF10, 0, VkF10);
        PInvoke.RegisterHotKey(HWND.Null, HotkeyIdF1, 0, VkF1);
        PInvoke.RegisterHotKey(HWND.Null, HotkeyIdF2, 0, VkF2);


        unsafe
        {
            uint pid = 0; PInvoke.GetWindowThreadProcessId(_starCitizenHwnd, &pid);
            var scProc = Process.GetProcessById((int)pid);
            scProc.EnableRaisingEvents = true;
            scProc.Exited += (_, __) => PInvoke.PostMessage(overlay.Hwnd, PInvoke.WM_CLOSE, default, default);
        }

        MSG msg; bool running = true;
        while (running)
        {
            while (PInvoke.PeekMessage(out msg, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
            {
                if (msg.message == PInvoke.WM_QUIT) { running = false; break; }

                if (msg.message == PInvoke.WM_HOTKEY)
                {
                    if (msg.wParam == HotkeyIdF10) { overlay.ToggleClickThrough(); _isInteractive = !_isInteractive; }
                    else if (msg.wParam == HotkeyIdF1) { overlay.Visible = !overlay.Visible; }
                }
                else if (msg.message == PInvoke.WM_INPUT) HandleRawInput(msg.lParam, overlay);
                else if (msg.message == PInvoke.WM_CHAR && _isInteractive) ImGui.GetIO().AddInputCharacter((uint)msg.wParam.Value);
                else if ((msg.message == PInvoke.WM_KEYDOWN || msg.message == PInvoke.WM_SYSKEYDOWN ||
                          msg.message == PInvoke.WM_KEYUP || msg.message == PInvoke.WM_SYSKEYUP) && _isInteractive)
                {
                    bool down = (msg.message == PInvoke.WM_KEYDOWN || msg.message == PInvoke.WM_SYSKEYDOWN);
                    var key = VkToImGuiKey((int)msg.wParam.Value);
                    if (key != ImGuiKey.None) ImGui.GetIO().AddKeyEvent(key, down);
                    var io = ImGui.GetIO();
                    io.KeyCtrl = (PInvoke.GetKeyState(0x11) & 0x8000) != 0;
                    io.KeyShift = (PInvoke.GetKeyState(0x10) & 0x8000) != 0;
                    io.KeyAlt = (PInvoke.GetKeyState(0x12) & 0x8000) != 0;
                    io.KeySuper = (PInvoke.GetKeyState(0x5B) & 0x8000) != 0 || (PInvoke.GetKeyState(0x5C) & 0x8000) != 0;
                }
                else if (msg.message == PInvoke.WM_MOUSEWHEEL)
                {
                    short delta = (short)((msg.wParam.Value >> 16) & 0xFFFF);
                    _accumulatedWheel += delta / (float)MouseWheelDeltaPerNotch;
                }

                PInvoke.TranslateMessage(msg);
                PInvoke.DispatchMessage(msg);
            }

            if (overlay.Visible && (PInvoke.GetForegroundWindow() == _starCitizenHwnd || _isInteractive))
            {
                // per-frame dt for animations
                double now = _animClock.Elapsed.TotalSeconds;
                _dt = (float)Math.Clamp(now - _lastAnimSec, 0, 0.1);   // clamp to avoid spikes
                _lastAnimSec = now;

                d3d.EnsureSize(overlay.ClientWidth, overlay.ClientHeight);
                d3d.BeginFrame();

                var io = ImGui.GetIO();
                io.DisplayFramebufferScale = new Vector2(_dpiScale, _dpiScale);
                UpdateImGuiMouseState(overlay.Hwnd);

                imgui.NewFrame(overlay.ClientWidth, overlay.ClientHeight);
                var dl = ImGui.GetForegroundDrawList();

                ImGui.Begin("Debug", ImGuiWindowFlags.AlwaysAutoResize);
                ImGui.Text("F1: show/hide");
                ImGui.Text("F10: interactive");
                ImGui.Text($"DPI scale: {_dpiScale:F2}");
                // capture its rect BEFORE End()
                var dbgPos = ImGui.GetWindowPos();
                var dbgSize = ImGui.GetWindowSize();
                ImGui.End();

                if (_showPillTuning)
                    DrawPillTuningUI();


                // --- Compute pill rect & draw ---
                var (pillPos, pillSize, rounding) = ComputePillRect(_mobiFramePx);
                DrawCenteredPill(dl, pillPos, pillSize, rounding, _cassioTex, "APPLETS", active: false, ref _pillHoverT, _dt);

                // --- Build hit-test regions (client coords) ---
                static Windows.Win32.Foundation.RECT MakeRect(Vector2 pos, Vector2 size)
                {
                    return new Windows.Win32.Foundation.RECT
                    {
                        left = (int)MathF.Floor(pos.X),
                        top = (int)MathF.Floor(pos.Y),
                        right = (int)MathF.Ceiling(pos.X + size.X),
                        bottom = (int)MathF.Ceiling(pos.Y + size.Y)
                    };
                }

                var hitRegions = new List<Windows.Win32.Foundation.RECT>(4);

                // 1) pill
                hitRegions.Add(MakeRect(pillPos, pillSize));

                // 2) debug window
                hitRegions.Add(MakeRect(dbgPos, dbgSize));

                // 3) tuning window (only if visible)
                if (_showPillTuning)
                    hitRegions.Add(MakeRect(_tuningWinPos, _tuningWinSize));

                // Tell the overlay: only these areas should receive mouse; everything else passes through
                overlay.SetHitTestRegions(hitRegions.ToArray());


                imgui.Render(d3d.SwapChain);
                d3d.Present();
            }
            else
            {
                Thread.Sleep(SleepWhenIdleMs);
            }
        }

        cts.Cancel();
        PInvoke.UnregisterHotKey(HWND.Null, HotkeyIdF10);
        PInvoke.UnregisterHotKey(HWND.Null, HotkeyIdF1);
    }

    // ---------------- draw helpers ----------------

    private static (Vector2 pos, Vector2 size, float rounding) ComputePillRect(Rectangle frame)
    {
        // Bottom mobi bar metrics
        float barH = frame.Height * _pill.BarHeightRel;
        float pillH = barH * _pill.PillHeightFrac;
        float pillW = pillH * _pill.PillWidthToHeight;

        float rightMargin = frame.Width * _pill.RightMarginRel;
        float bottomMargin = frame.Height * _pill.BottomMarginRel;

        var pos = new Vector2(
            frame.Right - rightMargin - pillW,
            frame.Bottom - bottomMargin - pillH
        );
        var size = new Vector2(pillW, pillH);
        float rounding = pillH * 0.25f;
        return (pos, size, rounding);
    }

    private static void DrawCenteredPill(ImDrawListPtr dl, Vector2 pos, Vector2 size, float rounding,
    IntPtr iconTex, string label, bool active, ref float hoverT, float dt)
    {
        Vector2 min = pos, max = pos + size;

        // Interactivity hit area first, so we can use IsItemHovered() for animation
        ImGui.SetCursorScreenPos(min);
        ImGui.InvisibleButton("##pill_btn", size);
        bool hovered = ImGui.IsItemHovered();

        // Animate hoverT -> [0..1] with a critically-damped-ish approach
        float target = hovered ? 1f : 0f;
        float speed = _pill.HoverSpeed; // feel free to tune
        hoverT += (target - hoverT) * (1f - MathF.Exp(-speed * dt));

        // Colors
        uint colBgBase = ImGui.GetColorU32(new Vector4(0.05f, 0.12f, 0.18f, active ? 0.88f : 0.65f));
        uint colBorder = ImGui.GetColorU32(new Vector4(0.75f, 0.90f, 1.00f, 0.90f));
        uint colText = ImGui.GetColorU32(new Vector4(0.90f, 0.98f, 1.00f, 0.95f));
        uint colHoverAdd = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, _pill.HoverBgBrighten * hoverT)); // brighten bg

        // Background + border
        dl.AddRectFilled(min, max, colBgBase, rounding);
        if (hoverT > 0f) dl.AddRectFilled(min, max, colHoverAdd, rounding);
        dl.AddRect(min, max, colBorder, rounding, ImDrawFlags.None, _pill.BorderThickness);

        // Inner content rect
        float innerPad = size.Y * _pill.InnerPadFrac;
        var innerMin = min + new Vector2(innerPad, innerPad * 0.8f);
        var innerMax = max - new Vector2(innerPad, innerPad * 0.9f);
        var innerSize = innerMax - innerMin;

        // Icon base geometry (square, centered)
        float iconH0 = innerSize.Y * _pill.IconHeightFrac;
        float iconW0 = iconH0;
        var iconMin = new Vector2(innerMin.X + (innerSize.X - iconW0) * 0.5f, innerMin.Y);
        var iconMax = iconMin + new Vector2(iconW0, iconH0);

        // --- Hover “lift + tilt” for icon (2.5D look) ---
        // lift up, slight scale, widen top edge
        float liftPx = iconH0 * (_pill.HoverLiftFrac * hoverT);
        float scale = 1f + _pill.HoverScaleFrac * hoverT;
        float topWiden = iconW0 * (_pill.HoverTiltWidenFrac * hoverT);


        var center = (iconMin + iconMax) * 0.5f;
        float hx = (iconW0 * 0.5f) * scale;
        float hy = (iconH0 * 0.5f) * scale;

        // Corners for quad (tilt forward: top moves up & widens)
        var bl = new Vector2(center.X - hx, center.Y + hy);
        var br = new Vector2(center.X + hx, center.Y + hy);
        var tl = new Vector2(center.X - hx - topWiden, center.Y - hy - liftPx);
        var tr = new Vector2(center.X + hx + topWiden, center.Y - hy - liftPx);

        // --- Directional light-blue “shadow/glow” toward center (scaled by hover) ---
        if (iconTex != IntPtr.Zero && _pill.ShadowOpacity > 0f && _pill.ShadowBlurTaps > 0)
        {
            var frameCenter = new Vector2(_mobiFramePx.Left + _mobiFramePx.Width * 0.5f,
                                          _mobiFramePx.Top + _mobiFramePx.Height * 0.5f);
            var iconCenter = center;
            var v = iconCenter - frameCenter;
            if (v.LengthSquared() < 1e-4f) v = new Vector2(0, 1);

            // outward + down bias, then flip X so it leans toward center
            var dir = Vector2.Normalize(new Vector2(v.X, v.Y + _pill.ShadowDownBias));
            dir = new Vector2(-dir.X, dir.Y);

            float distNorm = Math.Clamp(v.Length() / (_mobiFramePx.Width * 0.5f), 0f, 1f);
            float strength = 0.35f + 0.65f * MathF.Pow(distNorm, 1.25f);
            float maxOffset = _pill.ShadowMaxOffsetPx * _dpiScale;

            // when hovered, push it a bit more
            var baseOff = dir * (maxOffset * strength * (1f + _pill.ShadowHoverBoost * hoverT));

            int taps = Math.Max(1, _pill.ShadowBlurTaps);
            for (int i = 1; i <= taps; i++)
            {
                float t = i / (float)taps;
                var off = baseOff * t;
                float a = _pill.ShadowOpacity * (1.0f - t) * 0.9f;

                // very light blue
                uint col = ImGui.GetColorU32(new Vector4(0.82f, 0.95f, 1.00f, a));

                dl.AddImageQuad(iconTex,
                    tl + off, tr + off, br + off, bl + off,
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                    col);
            }
        }

        // Main icon (tilted)
        if (iconTex != IntPtr.Zero)
        {
            dl.AddImageQuad(iconTex, tl, tr, br, bl,
                            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
        }

        // Centered label under icon
        var textSize = ImGui.CalcTextSize(label);
        var textPos = new Vector2(
            innerMin.X + (innerSize.X - textSize.X) * 0.5f,
            (br.Y) + (innerSize.Y - (br.Y - innerMin.Y) - textSize.Y) * 0.5f
        );
        dl.AddText(textPos, colText, label);

        // Optional hover outline
        if (hoverT > 0f)
            dl.AddRect(min, max, ImGui.GetColorU32(new Vector4(1, 1, 1, 0.10f * hoverT)), rounding, ImDrawFlags.None, 1.5f);
    }



    // --------------- utility / input / window tracking ----------------

    private static void UpdateImGuiMouseState(HWND hwnd)
    {
        var io = ImGui.GetIO();

        // Always provide position. WM_NCHITTEST decides who gets the click.
        PInvoke.GetCursorPos(out System.Drawing.Point cursorScreen);
        PInvoke.ScreenToClient(hwnd, ref cursorScreen);
        io.MousePos = new Vector2(cursorScreen.X, cursorScreen.Y);

        io.MouseDown[0] = PInvoke.GetAsyncKeyState((int)VirtualKey.LeftButton) < 0;
        io.MouseDown[1] = PInvoke.GetAsyncKeyState((int)VirtualKey.RightButton) < 0;
        io.MouseDown[2] = PInvoke.GetAsyncKeyState((int)VirtualKey.MiddleButton) < 0;

        io.MouseWheel = _accumulatedWheel;
        _accumulatedWheel = 0;
    }

    private static unsafe void RegisterKeyboardRawInput(HWND hwnd)
    {
        var rid = new Windows.Win32.UI.Input.RAWINPUTDEVICE
        {
            usUsagePage = 0x01,
            usUsage = 0x06,
            dwFlags = Windows.Win32.UI.Input.RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK,
            hwndTarget = hwnd
        };
        _ = PInvoke.RegisterRawInputDevices(&rid, 1, (uint)sizeof(Windows.Win32.UI.Input.RAWINPUTDEVICE));
    }

    private static unsafe void HandleRawInput(LPARAM lParam, OverlayWindow overlay)
    {
        uint size = 0;
        var hraw = new HRAWINPUT(lParam.Value);
        PInvoke.GetRawInputData(hraw, Windows.Win32.UI.Input.RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, null, &size, (uint)sizeof(Windows.Win32.UI.Input.RAWINPUTHEADER));
        if (size == 0) return;

        byte* buffer = stackalloc byte[(int)size];
        if (PInvoke.GetRawInputData(hraw, Windows.Win32.UI.Input.RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, buffer, &size, (uint)sizeof(Windows.Win32.UI.Input.RAWINPUTHEADER)) != size)
            return;

        var raw = *(Windows.Win32.UI.Input.RAWINPUT*)buffer;
        if (raw.header.dwType != 1) return;

        var kb = raw.data.keyboard;
        const ushort RiKeyBreak = 0x0001;

        if (kb.VKey == VkF10 && (kb.Flags & RiKeyBreak) == 0) { overlay.ToggleClickThrough(); _isInteractive = !_isInteractive; }
        if (kb.VKey == VkF1 && (kb.Flags & RiKeyBreak) == 0) { overlay.Visible = !overlay.Visible; }
        if (kb.VKey == VkF2 && (kb.Flags & RiKeyBreak) == 0) { _showPillTuning = !_showPillTuning; }

    }

    private static bool TryFindStarCitizenWindow(out HWND hwnd)
    {
        hwnd = HWND.Null; HWND found = HWND.Null;
        PInvoke.EnumWindows(new WNDENUMPROC((candidate, _) =>
        {
            unsafe
            {
                uint pid = 0; PInvoke.GetWindowThreadProcessId(candidate, &pid);
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    if (!proc.ProcessName.Equals("StarCitizen", StringComparison.OrdinalIgnoreCase)) return true;
                    if (!PInvoke.IsWindowVisible(candidate)) return true;
                    found = candidate; return false;
                }
                catch { return true; }
            }
        }), default);
        hwnd = found; return hwnd != HWND.Null;
    }

    private static ImGuiKey VkToImGuiKey(int vk) => vk switch
    {
        0x08 => ImGuiKey.Backspace,
        0x09 => ImGuiKey.Tab,
        0x0D => ImGuiKey.Enter,
        0x1B => ImGuiKey.Escape,
        0x21 => ImGuiKey.PageUp,
        0x22 => ImGuiKey.PageDown,
        0x23 => ImGuiKey.End,
        0x24 => ImGuiKey.Home,
        0x25 => ImGuiKey.LeftArrow,
        0x26 => ImGuiKey.UpArrow,
        0x27 => ImGuiKey.RightArrow,
        0x28 => ImGuiKey.DownArrow,
        0x2D => ImGuiKey.Insert,
        0x2E => ImGuiKey.Delete,
        0x20 => ImGuiKey.Space,
        int n when n >= 0x70 && n <= 0x7B => ImGuiKey.F1 + (n - 0x70),
        _ => ImGuiKey.None
    };

    private static Rectangle ComputeMobiFrame(int clientW, int clientH)
    {
        const float aspect = 16f / 9f;
        int targetW = Math.Min(clientW, (int)Math.Round(clientH * aspect));
        int targetH = (int)Math.Round(targetW / aspect);
        int x = (clientW - targetW) / 2;
        int y = (clientH - targetH) / 2;
        return new Rectangle(x, y, targetW, targetH);
    }

    private static Rectangle RoiFromMobiRelative(Rectangle mobi, float rx, float ry, float rw, float rh)
    {
        int x = mobi.X + (int)Math.Round(rx * mobi.Width);
        int y = mobi.Y + (int)Math.Round(ry * mobi.Height);
        int w = (int)Math.Round(rw * mobi.Width);
        int h = (int)Math.Round(rh * mobi.Height);
        return new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h));
    }

    private static void TrackStarCitizen(CancellationToken token, OverlayWindow overlay)
    {
        RECT lastRect = default;
        bool lastMin = false;

        while (!token.IsCancellationRequested)
        {
            if (_starCitizenHwnd == HWND.Null || !PInvoke.IsWindow(_starCitizenHwnd))
            {
                overlay.Visible = false; Thread.Sleep(250); continue;
            }

            PInvoke.GetClientRect(_starCitizenHwnd, out RECT client);
            var topLeft = new System.Drawing.Point(0, 0);
            PInvoke.ClientToScreen(_starCitizenHwnd, ref topLeft);
            RECT screenRect = new RECT { left = topLeft.X, top = topLeft.Y, right = topLeft.X + client.right, bottom = topLeft.Y + client.bottom };

            bool minimized = PInvoke.IsIconic(_starCitizenHwnd);
            if (minimized != lastMin) { if (minimized) overlay.Visible = false; lastMin = minimized; }

            if (screenRect.left != lastRect.left || screenRect.top != lastRect.top || screenRect.right != lastRect.right || screenRect.bottom != lastRect.bottom)
            {
                overlay.UpdateBounds(screenRect);
                lastRect = screenRect;

                int w = client.right, h = client.bottom;
                _mobiFramePx = ComputeMobiFrame(w, h);
                _roiPixels = RoiFromMobiRelative(_mobiFramePx, relX, relY, relW, relH);
            }

            Thread.Sleep(TrackerIntervalMs);
        }
    }

    private static void DrawPillTuningUI()
    {
        ImGui.Begin("Pill Tuning", ImGuiWindowFlags.AlwaysAutoResize);

        bool changed = false;

        float bar = _pill.BarHeightRel;
        if (ImGui.SliderFloat("BarHeightRel", ref bar, 0.05f, 0.20f, "%.3f")) { _pill.BarHeightRel = bar; changed = true; }

        float phf = _pill.PillHeightFrac;
        if (ImGui.SliderFloat("PillHeightFrac", ref phf, 0.40f, 1.00f, "%.3f")) { _pill.PillHeightFrac = phf; changed = true; }

        float wr = _pill.PillWidthToHeight;
        if (ImGui.SliderFloat("Width:Height", ref wr, 1.20f, 3.50f, "%.2f")) { _pill.PillWidthToHeight = wr; changed = true; }

        float rm = _pill.RightMarginRel;
        if (ImGui.SliderFloat("RightMarginRel", ref rm, 0.000f, 0.080f, "%.3f")) { _pill.RightMarginRel = rm; changed = true; }

        float bm = _pill.BottomMarginRel;
        if (ImGui.SliderFloat("BottomMarginRel", ref bm, 0.000f, 0.080f, "%.3f")) { _pill.BottomMarginRel = bm; changed = true; }

        float pad = _pill.InnerPadFrac;
        if (ImGui.SliderFloat("InnerPadFrac", ref pad, 0.05f, 0.35f, "%.3f")) { _pill.InnerPadFrac = pad; changed = true; }

        float ih = _pill.IconHeightFrac;
        if (ImGui.SliderFloat("IconHeightFrac", ref ih, 0.30f, 0.90f, "%.3f")) { _pill.IconHeightFrac = ih; changed = true; }

        float bt = _pill.BorderThickness;
        if (ImGui.SliderFloat("BorderThickness", ref bt, 0.5f, 5.0f, "%.1f")) { _pill.BorderThickness = bt; changed = true; }

        float so = _pill.ShadowOpacity;
        if (ImGui.SliderFloat("ShadowOpacity", ref so, 0.0f, 1.0f, "%.2f")) _pill.ShadowOpacity = so;

        float sm = _pill.ShadowMaxOffsetPx;
        if (ImGui.SliderFloat("ShadowMaxOffsetPx", ref sm, 0.0f, 30.0f, "%.1f")) _pill.ShadowMaxOffsetPx = sm;

        int taps = _pill.ShadowBlurTaps;
        if (ImGui.SliderInt("ShadowBlurTaps", ref taps, 1, 8)) _pill.ShadowBlurTaps = Math.Max(1, taps);

        float db = _pill.ShadowDownBias;
        if (ImGui.SliderFloat("ShadowDownBias", ref db, 0.0f, 1.0f, "%.2f")) _pill.ShadowDownBias = db;

        ImGui.Separator();
        ImGui.TextDisabled("Hover Anim");

        float sp = _pill.HoverSpeed;
        if (ImGui.SliderFloat("HoverSpeed", ref sp, 1.0f, 25.0f, "%.1f")) _pill.HoverSpeed = sp;

        float hb = _pill.HoverBgBrighten;
        if (ImGui.SliderFloat("HoverBgBrighten", ref hb, 0.0f, 0.30f, "%.2f")) _pill.HoverBgBrighten = hb;

        float lf = _pill.HoverLiftFrac;
        if (ImGui.SliderFloat("HoverLiftFrac", ref lf, 0.00f, 0.30f, "%.3f")) _pill.HoverLiftFrac = lf;

        float sc = _pill.HoverScaleFrac;
        if (ImGui.SliderFloat("HoverScaleFrac", ref sc, 0.00f, 0.20f, "%.3f")) _pill.HoverScaleFrac = sc;

        float tw = _pill.HoverTiltWidenFrac;
        if (ImGui.SliderFloat("HoverTiltWidenFrac", ref tw, 0.00f, 0.30f, "%.3f")) _pill.HoverTiltWidenFrac = tw;

        float shb = _pill.ShadowHoverBoost;
        if (ImGui.SliderFloat("ShadowHoverBoost", ref shb, 0.0f, 1.5f, "%.2f")) _pill.ShadowHoverBoost = shb;



        if (ImGui.Button("Reset Defaults")) { _pill = new PillLayout(); changed = true; }
        ImGui.SameLine();
        if (ImGui.Button("Save")) SavePillLayout();
        ImGui.SameLine();
        if (ImGui.Button("Reload")) LoadPillLayout();

        ImGui.TextDisabled("F10 = interactive (click sliders), F2 = toggle this panel.");

        // record its rect so Program.cs can add it to hit-test
        _tuningWinPos = ImGui.GetWindowPos();
        _tuningWinSize = ImGui.GetWindowSize();


        ImGui.End();
    }


    private static void LoadPillLayout()
    {
        try
        {
            if (File.Exists(_pillCfgPath))
            {
                var json = File.ReadAllText(_pillCfgPath);
                var loaded = JsonSerializer.Deserialize<PillLayout>(json);
                if (loaded != null) _pill = loaded;
            }
        }
        catch (Exception ex) { Logger.Warn($"Load pill_layout.json failed: {ex.Message}"); }
    }

    private static void SavePillLayout()
    {
        try
        {
            var json = JsonSerializer.Serialize(_pill, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_pillCfgPath, json);
        }
        catch (Exception ex) { Logger.Warn($"Save pill_layout.json failed: {ex.Message}"); }
    }

    private static Vector2 CenterTextInRect(Vector2 min, Vector2 max, string text)
    {
        var sz = ImGui.CalcTextSize(text);
        return new Vector2(min.X + (max.X - min.X - sz.X) * 0.5f,
                           min.Y + (max.Y - min.Y - sz.Y) * 0.5f);
    }

}

using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Text.Json;
using ImGuiNET;
using Overlay_Renderer;
using Overlay_Renderer.ImGuiRenderer;
using Overlay_Renderer.Methods;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Overlay_Renderer.Helpers;

namespace Starboard;

internal static class Program
{
    // --- Pill layout config (persisted to pill_layout.json) ------------------

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

        public float ShadowOpacity { get; set; } = 0.35f;
        public float ShadowMaxOffsetPx { get; set; } = 10.0f;
        public int ShadowBlurTaps { get; set; } = 4;
        public float ShadowDownBias { get; set; } = 0.30f;

        // Hover animation
        public float HoverLiftFrac { get; set; } = 0.12f;
        public float HoverScaleFrac { get; set; } = 0.06f;
        public float HoverTiltWidenFrac { get; set; } = 0.08f;
        public float HoverBgBrighten { get; set; } = 0.12f;
        public float HoverSpeed { get; set; } = 10.0f;
        public float ShadowHoverBoost { get; set; } = 0.60f;
    }

    private static PillLayout _pill = new();
    private static readonly string _pillCfgPath = "pill_layout.json";
    private static bool _showPillTuning = true; // can be toggled later if you want hotkeys

    // Current mobi-frame region in overlay client pixels
    private static Rectangle _mobiFramePx;

    // Animation state
    private static float _pillHoverT = 0f;
    private static readonly Stopwatch _animClock = Stopwatch.StartNew();
    private static double _lastAnimSec = 0;
    private static float _dt = 1f / 120f;

    // DPI scale from target window
    private static float _dpiScale = 1.0f;

    // Icon texture
    private static IntPtr _cassioTex = IntPtr.Zero;
    private static int _cassioW, _cassioH;

    [STAThread]
    private static void Main(string[] args)
    {
        Logger.Info("Starboard starting...");

        // Find Star Citizen main window via Overlay-Renderer's helper
        IntPtr rawHwnd = FindProcess.WaitForMainWindow("StarCitizen", retries: 40, delayMs: 500);
        if (rawHwnd == IntPtr.Zero)
        {
            Logger.Error("Could not find StarCitizen main window. Exiting.");
            return;
        }

        var targetHwnd = new HWND(rawHwnd);
        unsafe
        {
            Logger.Info($"Attached to StarCitizen window 0x{(nuint)targetHwnd.Value:X}");
        }

        using var overlay = new OverlayWindow(targetHwnd);
        using var d3dHost = new D3DHost(overlay.Hwnd);
        using var imguiRenderer = new ImGuiRendererD3D11(d3dHost.Device, d3dHost.Context);

        // Optional: apply your ImGui style preset (from Overlay-Renderer)
        ImGuiStylePresets.ApplyDark();

        // Load icon texture
        try
        {
            _cassioTex = imguiRenderer.CreateTextureFromFile(
                Path.Combine("Assets", "Icons", "cassiopia.png"),
                out _cassioW,
                out _cassioH);
            Logger.Info("Loaded Cassiopeia icon texture.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to load icon texture: {ex.Message}");
            _cassioTex = IntPtr.Zero;
        }

        // Initial mobi frame based on default client size
        _mobiFramePx = ComputeMobiFrame(overlay.ClientWidth, overlay.ClientHeight);

        // DPI scale from target window
        try
        {
            uint dpi = PInvoke.GetDpiForWindow(targetHwnd);
            _dpiScale = MathF.Max(1.0f, dpi / 96.0f);
        }
        catch
        {
            _dpiScale = 1.0f;
        }

        // Try to load pill layout from disk
        LoadPillLayout();

        var cts = new CancellationTokenSource();

        // Track Star Citizen window: keep overlay aligned & update frame size
        bool firstSize = true;
        var trackingTask = WindowTracker.StartTrackingAsync(
            targetHwnd,
            overlay,
            cts.Token,
            (w, h) =>
            {
                d3dHost.EnsureSize(w, h);

                _mobiFramePx = ComputeMobiFrame(w, h);

                if (firstSize)
                {
                    firstSize = false;
                    overlay.Visible = true;
                }
            });

        // Close overlay when Star Citizen exits
        TryHookProcessExit(targetHwnd, overlay);

        RunMessageAndRenderLoop(overlay, d3dHost, imguiRenderer, cts);

        cts.Cancel();
        try { trackingTask.Wait(500); } catch { /* ignore */ }

        Logger.Info("Starboard shutting down.");
    }

    private static void RunMessageAndRenderLoop(
        OverlayWindow overlay,
        D3DHost d3dHost,
        ImGuiRendererD3D11 imguiRenderer,
        CancellationTokenSource cts)
    {
        MSG msg;

        while (!cts.IsCancellationRequested)
        {
            // Pump Win32 messages
            while (PInvoke.PeekMessage(out msg, HWND.Null, 0, 0,
                       PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
            {
                if (msg.message == PInvoke.WM_QUIT)
                {
                    cts.Cancel();
                    return;
                }

                PInvoke.TranslateMessage(msg);
                PInvoke.DispatchMessage(msg);
            }

            if (overlay.Hwnd.IsNull)
            {
                cts.Cancel();
                return;
            }

            // Per-frame dt for animation
            double now = _animClock.Elapsed.TotalSeconds;
            _dt = (float)Math.Clamp(now - _lastAnimSec, 0, 0.1);
            _lastAnimSec = now;

            // Render frame
            d3dHost.BeginFrame();
            imguiRenderer.NewFrame(overlay.ClientWidth, overlay.ClientHeight);

            ImGuiInput.UpdateMouse(overlay);
            ImGuiInput.UpdateKeyboard();

            HitTestRegions.BeginFrame();
            DrawUi();
            HitTestRegions.ApplyToOverlay(overlay);

            imguiRenderer.Render(d3dHost.SwapChain);
            d3dHost.Present();

            Thread.Sleep(16);
        }
    }

    // ------------------------------------------------------------------------
    // UI BUILD
    // ------------------------------------------------------------------------

    private static void DrawUi()
    {
        // Simple debug window
        ImGui.Begin("Starboard Debug", ImGuiWindowFlags.AlwaysAutoResize);
        ImGui.Text("Starboard Overlay");
        ImGui.Text($"Mobi frame: {_mobiFramePx.Width} x {_mobiFramePx.Height}");
        ImGui.Text($"DPI scale: {_dpiScale:F2}");
        HitTestRegions.AddCurrentWindow();
        ImGui.End();

        // Pill tuning window (ImGui-only, but should still be clickable)
        if (_showPillTuning)
            DrawPillTuningUI();

        // Draw the pill
        if (_mobiFramePx.Width > 0 && _mobiFramePx.Height > 0)
        {
            var dl = ImGui.GetForegroundDrawList();
            var (pillPos, pillSize, rounding) = ComputePillRect(_mobiFramePx);

            DrawCenteredPill(dl, pillPos, pillSize, rounding,
                _cassioTex, "APPLETS", active: false, ref _pillHoverT, _dt);

            // Make the pill clickable
            HitTestRegions.AddRect(pillPos, pillSize);
        }
    }

    // ------------------------------------------------------------------------
    // Pill geometry / drawing
    // ------------------------------------------------------------------------

    private static (Vector2 pos, Vector2 size, float rounding) ComputePillRect(Rectangle frame)
    {
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

    private static void DrawCenteredPill(
        ImDrawListPtr dl,
        Vector2 pos,
        Vector2 size,
        float rounding,
        IntPtr iconTex,
        string label,
        bool active,
        ref float hoverT,
        float dt)
    {
        Vector2 min = pos, max = pos + size;

        // Interactivity hit area via invisible button
        ImGui.SetCursorScreenPos(min);
        ImGui.InvisibleButton("##pill_btn", size);
        bool hovered = ImGui.IsItemHovered();

        // Animate hoverT -> [0..1]
        float target = hovered ? 1f : 0f;
        float speed = _pill.HoverSpeed;
        hoverT += (target - hoverT) * (1f - MathF.Exp(-speed * dt));

        // Colors
        uint colBgBase = ImGui.GetColorU32(new Vector4(0.05f, 0.12f, 0.18f, active ? 0.88f : 0.65f));
        uint colBorder = ImGui.GetColorU32(new Vector4(0.75f, 0.90f, 1.00f, 0.90f));
        uint colText = ImGui.GetColorU32(new Vector4(0.90f, 0.98f, 1.00f, 0.95f));
        uint colHoverAdd = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, _pill.HoverBgBrighten * hoverT));

        // Background + border
        dl.AddRectFilled(min, max, colBgBase, rounding);
        if (hoverT > 0f) dl.AddRectFilled(min, max, colHoverAdd, rounding);
        dl.AddRect(min, max, colBorder, rounding, ImDrawFlags.None, _pill.BorderThickness);

        // Inner content rect
        float innerPad = size.Y * _pill.InnerPadFrac;
        var innerMin = min + new Vector2(innerPad, innerPad * 0.8f);
        var innerMax = max - new Vector2(innerPad, innerPad * 0.9f);
        var innerSize = innerMax - innerMin;

        // Icon base geometry
        float iconH0 = innerSize.Y * _pill.IconHeightFrac;
        float iconW0 = iconH0;
        var iconMin = new Vector2(innerMin.X + (innerSize.X - iconW0) * 0.5f, innerMin.Y);
        var iconMax = iconMin + new Vector2(iconW0, iconH0);

        // Hover “lift + tilt”
        float liftPx = iconH0 * (_pill.HoverLiftFrac * hoverT);
        float scale = 1f + _pill.HoverScaleFrac * hoverT;
        float topWiden = iconW0 * (_pill.HoverTiltWidenFrac * hoverT);

        var center = (iconMin + iconMax) * 0.5f;
        float hx = (iconW0 * 0.5f) * scale;
        float hy = (iconH0 * 0.5f) * scale;

        var bl = new Vector2(center.X - hx, center.Y + hy);
        var br = new Vector2(center.X + hx, center.Y + hy);
        var tl = new Vector2(center.X - hx - topWiden, center.Y - hy - liftPx);
        var tr = new Vector2(center.X + hx + topWiden, center.Y - hy - liftPx);

        // Directional glow/shadow
        if (iconTex != IntPtr.Zero && _pill.ShadowOpacity > 0f && _pill.ShadowBlurTaps > 0)
        {
            var frameCenter = new Vector2(
                _mobiFramePx.Left + _mobiFramePx.Width * 0.5f,
                _mobiFramePx.Top + _mobiFramePx.Height * 0.5f);
            var iconCenter = center;
            var v = iconCenter - frameCenter;
            if (v.LengthSquared() < 1e-4f) v = new Vector2(0, 1);

            var dir = Vector2.Normalize(new Vector2(v.X, v.Y + _pill.ShadowDownBias));
            dir = new Vector2(-dir.X, dir.Y);

            float distNorm = Math.Clamp(v.Length() / (_mobiFramePx.Width * 0.5f), 0f, 1f);
            float strength = 0.35f + 0.65f * MathF.Pow(distNorm, 1.25f);
            float maxOffset = _pill.ShadowMaxOffsetPx * _dpiScale;

            var baseOff = dir * (maxOffset * strength * (1f + _pill.ShadowHoverBoost * hoverT));

            int taps = Math.Max(1, _pill.ShadowBlurTaps);
            for (int i = 1; i <= taps; i++)
            {
                float t = i / (float)taps;
                var off = baseOff * t;
                float a = _pill.ShadowOpacity * (1.0f - t) * 0.9f;

                uint col = ImGui.GetColorU32(new Vector4(0.82f, 0.95f, 1.00f, a));

                dl.AddImageQuad(iconTex,
                    tl + off, tr + off, br + off, bl + off,
                    new Vector2(0, 0), new Vector2(1, 0),
                    new Vector2(1, 1), new Vector2(0, 1),
                    col);
            }
        }

        // Icon
        if (iconTex != IntPtr.Zero)
        {
            dl.AddImageQuad(iconTex, tl, tr, br, bl,
                new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(1, 1), new Vector2(0, 1));
        }

        // Label
        var textSize = ImGui.CalcTextSize(label);
        var textPos = new Vector2(
            innerMin.X + (innerSize.X - textSize.X) * 0.5f,
            (br.Y) + (innerSize.Y - (br.Y - innerMin.Y) - textSize.Y) * 0.5f
        );
        dl.AddText(textPos, colText, label);

        if (hoverT > 0f)
            dl.AddRect(min, max, ImGui.GetColorU32(new Vector4(1, 1, 1, 0.10f * hoverT)),
                rounding, ImDrawFlags.None, 1.5f);
    }

    // ------------------------------------------------------------------------
    // ImGui windows for tuning
    // ------------------------------------------------------------------------

    private static void DrawPillTuningUI()
    {
        ImGui.Begin("Pill Tuning", ImGuiWindowFlags.AlwaysAutoResize);

        bool changed = false;

        float bar = _pill.BarHeightRel;
        if (ImGui.SliderFloat("BarHeightRel", ref bar, 0.05f, 0.20f, "%.3f"))
        { _pill.BarHeightRel = bar; changed = true; }

        float phf = _pill.PillHeightFrac;
        if (ImGui.SliderFloat("PillHeightFrac", ref phf, 0.40f, 1.00f, "%.3f"))
        { _pill.PillHeightFrac = phf; changed = true; }

        float wr = _pill.PillWidthToHeight;
        if (ImGui.SliderFloat("Width:Height", ref wr, 1.20f, 3.50f, "%.2f"))
        { _pill.PillWidthToHeight = wr; changed = true; }

        float rm = _pill.RightMarginRel;
        if (ImGui.SliderFloat("RightMarginRel", ref rm, 0.000f, 0.080f, "%.3f"))
        { _pill.RightMarginRel = rm; changed = true; }

        float bm = _pill.BottomMarginRel;
        if (ImGui.SliderFloat("BottomMarginRel", ref bm, 0.000f, 0.080f, "%.3f"))
        { _pill.BottomMarginRel = bm; changed = true; }

        float pad = _pill.InnerPadFrac;
        if (ImGui.SliderFloat("InnerPadFrac", ref pad, 0.05f, 0.35f, "%.3f"))
        { _pill.InnerPadFrac = pad; changed = true; }

        float ih = _pill.IconHeightFrac;
        if (ImGui.SliderFloat("IconHeightFrac", ref ih, 0.30f, 0.90f, "%.3f"))
        { _pill.IconHeightFrac = ih; changed = true; }

        float bt = _pill.BorderThickness;
        if (ImGui.SliderFloat("BorderThickness", ref bt, 0.5f, 5.0f, "%.1f"))
        { _pill.BorderThickness = bt; changed = true; }

        float so = _pill.ShadowOpacity;
        if (ImGui.SliderFloat("ShadowOpacity", ref so, 0.0f, 1.0f, "%.2f"))
            _pill.ShadowOpacity = so;

        float sm = _pill.ShadowMaxOffsetPx;
        if (ImGui.SliderFloat("ShadowMaxOffsetPx", ref sm, 0.0f, 30.0f, "%.1f"))
            _pill.ShadowMaxOffsetPx = sm;

        int taps = _pill.ShadowBlurTaps;
        if (ImGui.SliderInt("ShadowBlurTaps", ref taps, 1, 8))
            _pill.ShadowBlurTaps = Math.Max(1, taps);

        float db = _pill.ShadowDownBias;
        if (ImGui.SliderFloat("ShadowDownBias", ref db, 0.0f, 1.0f, "%.2f"))
            _pill.ShadowDownBias = db;

        ImGui.Separator();
        ImGui.TextDisabled("Hover Anim");

        float sp = _pill.HoverSpeed;
        if (ImGui.SliderFloat("HoverSpeed", ref sp, 1.0f, 25.0f, "%.1f"))
            _pill.HoverSpeed = sp;

        float hb = _pill.HoverBgBrighten;
        if (ImGui.SliderFloat("HoverBgBrighten", ref hb, 0.0f, 0.30f, "%.2f"))
            _pill.HoverBgBrighten = hb;

        float lf = _pill.HoverLiftFrac;
        if (ImGui.SliderFloat("HoverLiftFrac", ref lf, 0.00f, 0.30f, "%.3f"))
            _pill.HoverLiftFrac = lf;

        float sc = _pill.HoverScaleFrac;
        if (ImGui.SliderFloat("HoverScaleFrac", ref sc, 0.00f, 0.20f, "%.3f"))
            _pill.HoverScaleFrac = sc;

        float tw = _pill.HoverTiltWidenFrac;
        if (ImGui.SliderFloat("HoverTiltWidenFrac", ref tw, 0.00f, 0.30f, "%.3f"))
            _pill.HoverTiltWidenFrac = tw;

        float shb = _pill.ShadowHoverBoost;
        if (ImGui.SliderFloat("ShadowHoverBoost", ref shb, 0.0f, 1.5f, "%.2f"))
            _pill.ShadowHoverBoost = shb;

        if (ImGui.Button("Reset Defaults")) { _pill = new PillLayout(); changed = true; }
        ImGui.SameLine();
        if (ImGui.Button("Save")) SavePillLayout();
        ImGui.SameLine();
        if (ImGui.Button("Reload")) LoadPillLayout();

        // Mark this window as an interactive region
        HitTestRegions.AddCurrentWindow();

        ImGui.End();

        if (changed)
        {
            // optional: autosave on change if you want
        }
    }

    // ------------------------------------------------------------------------
    // Layout persistence / helpers
    // ------------------------------------------------------------------------

    private static void LoadPillLayout()
    {
        try
        {
            if (File.Exists(_pillCfgPath))
            {
                var json = File.ReadAllText(_pillCfgPath);
                var loaded = JsonSerializer.Deserialize<PillLayout>(json);
                if (loaded != null) _pill = loaded;
                Logger.Info("Loaded pill_layout.json");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Load pill_layout.json failed: {ex.Message}");
        }
    }

    private static void SavePillLayout()
    {
        try
        {
            var json = JsonSerializer.Serialize(_pill, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_pillCfgPath, json);
            Logger.Info("Saved pill_layout.json");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Save pill_layout.json failed: {ex.Message}");
        }
    }

    private static Rectangle ComputeMobiFrame(int clientW, int clientH)
    {
        const float aspect = 16f / 9f;
        int targetW = Math.Min(clientW, (int)Math.Round(clientH * aspect));
        int targetH = (int)Math.Round(targetW / aspect);
        int x = (clientW - targetW) / 2;
        int y = (clientH - targetH) / 2;
        return new Rectangle(x, y, targetW, targetH);
    }

    private static void TryHookProcessExit(HWND targetHwnd, OverlayWindow overlay)
    {
        try
        {
            unsafe
            {
                uint pid = 0;
                PInvoke.GetWindowThreadProcessId(targetHwnd, &pid);
                if (pid == 0) return;

                var proc = Process.GetProcessById((int)pid);
                proc.EnableRaisingEvents = true;
                proc.Exited += (_, _) =>
                {
                    Logger.Info("StarCitizen exited, closing overlay.");
                    PInvoke.PostMessage(overlay.Hwnd, PInvoke.WM_CLOSE, default, default);
                };
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to hook StarCitizen process exit: {ex.Message}");
        }
    }
}

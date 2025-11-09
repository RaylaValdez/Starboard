using ImGuiNET;
using Overlay_Renderer;
using Overlay_Renderer.Helpers;
using Overlay_Renderer.ImGuiRenderer;
using Overlay_Renderer.Methods;
using Starboard.Guis;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Text.Json;
using Windows.System;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Windows.Win32.UI.Input.KeyboardAndMouse;


namespace Starboard;

internal static class Program
{
    // Current mobi-frame region in overlay client pixels
    private static Rectangle _mobiFramePx;

    // Animation clock for dt
    private static readonly Stopwatch _animClock = new();
    private static double _lastAnimSec = 0;
    private static float _dt = 1f / 120f;
    

    // DPI scale from target window
    private static float _dpiScale = 1.0f;

    // Icon texture
    private static IntPtr _cassioTex = IntPtr.Zero;
    private static int _cassioW, _cassioH;

    // Font
    private static ImFontPtr _orbiBoldFont;
    private static ImFontPtr _orbiRegFont;
    private static ImFontPtr _orbiRegFontSmall;

    private static VIRTUAL_KEY _openMobiglassVk;
    private static ImGuiKey _openMobiglassImGui;
    private static VIRTUAL_KEY _openMobiMapVk;
    private static ImGuiKey _openMobiMapImGui;
    private static VIRTUAL_KEY _openMobiCommsVk;
    private static ImGuiKey _openMobiCommsImGui;
    private static bool _usesJoypad;
    private static bool _firstRunComplete;

    [STAThread]
    private static void Main(string[] args)
    {
        Logger.Info("Starboard starting...");
        _animClock.Start();

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

        _orbiRegFont = imguiRenderer.AddFontFromFileTTF("Assets/Fonts/Orbitron/static/Orbitron-Regular.ttf", 20f * _dpiScale);

        _orbiBoldFont = imguiRenderer.AddFontFromFileTTF("Assets/Fonts/Orbitron/static/Orbitron-Bold.ttf", 32f * _dpiScale);

        _orbiRegFontSmall = imguiRenderer.AddFontFromFileTTF("Assets/Fonts/Orbitron/static/Orbitron-Regular.ttf", 16f * _dpiScale);

        // Init playground
        Playground.Initialize(_cassioTex, _dpiScale, _mobiFramePx);
        FirstStartWindow.Initialize(_cassioTex, _dpiScale, _mobiFramePx, _orbiBoldFont, _orbiRegFont, _orbiRegFontSmall);

        // Load Settings
        StarboardSettingsStore.Load();

        _openMobiglassVk = StarboardSettingsStore.Current.OpenMobiglassKeybindVk;
        _openMobiglassImGui = StarboardSettingsStore.Current.OpenMobiglassKeybind;

        _openMobiMapVk = StarboardSettingsStore.Current.OpenMobimapKeybindVk;
        _openMobiMapImGui = StarboardSettingsStore.Current.OpenMobiMapKeybind;

        _openMobiCommsVk = StarboardSettingsStore.Current.OpenMobiCommsKeybindVk;
        _openMobiCommsImGui = StarboardSettingsStore.Current.OpenMobiCommsKeybind;

        _usesJoypad = StarboardSettingsStore.Current.UsesJoyPad;
        _firstRunComplete = StarboardSettingsStore.Current.FirstRunCompleted;

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
                Playground.SetMobiFrame(_mobiFramePx);
                FirstStartWindow.SetMobiFrame(_mobiFramePx);

                if (firstSize)
                {
                    firstSize = false;
                    overlay.Visible = true;
                }
            });

        // Close overlay when Star Citizen exits
        TryHookProcessExit(targetHwnd, overlay);

        StarboardSettingsStore.Load();

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
            ImGuiInput.UseOsCursor(true);

            HitTestRegions.BeginFrame();

            if (AppState.ShowPlayground)
            {
                Playground.Draw(_dt);
            }
            else
            {
                if (_firstRunComplete)
                {
                    AppState.ShowPlayground = true;
                }
                FirstStartWindow.Draw();
            }

                HitTestRegions.ApplyToOverlay(overlay);

            imguiRenderer.Render(d3dHost.SwapChain);
            d3dHost.Present();

            Thread.Sleep(16);
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

using ImGuiNET;
using Overlay_Renderer;
using Overlay_Renderer.Helpers;
using Overlay_Renderer.ImGuiRenderer;
using Overlay_Renderer.Methods;
using Starboard.Guis;
using System.Diagnostics;
using System.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using System.Windows.Forms;
using System.IO;
using Svg;
using System.Text;


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

    private static HWND _targetHwnd;

    //Tray
    private static NotifyIcon? _trayIcon;
    private static CancellationTokenSource? _ctsForTray;

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
        _targetHwnd = targetHwnd;
        unsafe
        {
            Logger.Info($"Attached to StarCitizen window 0x{(nuint)targetHwnd.Value:X}");
        }

        using var overlay = new OverlayWindow(targetHwnd);
        using var d3dHost = new D3DHost(overlay.Hwnd);
        using var imguiRenderer = new ImGuiRendererD3D11(d3dHost.Device, d3dHost.Context);

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

        // after _cassioTex load, before you Initialize(...) the windows
        try
        {
            FaviconManager.TextureUploader = bytes =>
            {
                try
                {
                    var size = Math.Max(16, Math.Min(256, FaviconManager.IconSizePx));

                    // If it's an SVG, rasterize to 256x256 in-memory and upload directly.
                    if (LooksLikeSvg(bytes))
                    {
                        using var ms = new MemoryStream(bytes);
                        var svgDoc = SvgDocument.Open<SvgDocument>(ms);

                        // Invert black → white for fills and strokes
                        foreach (var elem in svgDoc.Descendants().OfType<SvgVisualElement>())
                        {
                            if (elem.Fill is SvgColourServer fill)
                            {
                                var c = fill.Colour;
                                if (c.R == 0 && c.G == 0 && c.B == 0)        // ONLY pure black
                                    elem.Fill = new SvgColourServer(Color.White);
                            }
                            if (elem.Stroke is SvgColourServer stroke)
                            {
                                var c = stroke.Colour;
                                if (c.R == 0 && c.G == 0 && c.B == 0)        // ONLY pure black
                                    elem.Stroke = new SvgColourServer(Color.White);
                            }
                        }

                        using var bmp = svgDoc.Draw(32, 32);
                        return imguiRenderer.CreateTextureFromBitmap(bmp, out _, out _);
                    }
                    else
                    {
                        using var ms2 = new MemoryStream(bytes);
                        using var src = new Bitmap(ms2);
                        using var dst = new Bitmap(size, size);
                        using (var g = Graphics.FromImage(dst))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                            g.DrawImage(src, 0, 0, size, size);
                        }
                        return imguiRenderer.CreateTextureFromBitmap(dst, out _, out _, pointSampling: true);
                    }


                        // Non-SVG path (ICO/PNG/JPEG etc.) — load directly from bytes
                        using var nonSvgMs = new MemoryStream(bytes);
                    using var bmp2 = new Bitmap(nonSvgMs);
                    return imguiRenderer.CreateTextureFromBitmap(bmp2, out _, out _);
                }
                catch
                {
                    // Fallback to your previous temp-file route only if needed
                    try
                    {
                        var hash = System.Security.Cryptography.SHA1.HashData(bytes);
                        var name = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                        // Pick an extension purely for debugging; loader reads headers anyway
                        var dir = Path.Combine(Path.GetTempPath(), "Starboard", "favicons");
                        Directory.CreateDirectory(dir);
                        var file = Path.Combine(dir, name + ".bin");
                        if (!File.Exists(file)) File.WriteAllBytes(file, bytes);

                        return imguiRenderer.CreateTextureFromFile(file, out _, out _);
                    }
                    catch
                    {
                        return IntPtr.Zero;
                    }
                }
            };
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to set favicon uploader: {ex.Message}");
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

        FaviconManager.IconSizePx = (int)MathF.Round(32f * _dpiScale);


        // Init playground
        Playground.Initialize(_cassioTex, _dpiScale, _mobiFramePx, _orbiBoldFont, _orbiRegFont, _orbiRegFontSmall);
        FirstStartWindow.Initialize(_cassioTex, _dpiScale, _mobiFramePx, _orbiBoldFont, _orbiRegFont, _orbiRegFontSmall);
        StarboardMain.Initialize(_cassioTex, _dpiScale, _mobiFramePx, _orbiBoldFont, _orbiRegFont, _orbiRegFontSmall);
        TextureService.Initialize(imguiRenderer);
        WebBrowserManager.Initialize(overlay.Hwnd, _mobiFramePx.Width, _mobiFramePx.Height);

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

        AppState.FirstRunCompleted = _firstRunComplete;
        AppState.ShowPlayground = AppState.FirstRunCompleted;

        var cts = new CancellationTokenSource();
        _ctsForTray = cts;

        CreateTrayIcon();

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
                StarboardMain.SetMobiFrame(_mobiFramePx);

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

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

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

            // Inside RunMessageAndRenderLoop, each frame BEFORE you start drawing windows
            FaviconManager.ProcessPendingUploads();


            // Render frame
            d3dHost.BeginFrame();
            imguiRenderer.NewFrame(overlay.ClientWidth, overlay.ClientHeight);

            ImGuiInput.UpdateMouse(overlay);
            ImGuiInput.UpdateKeyboard();
            //ImGuiInput.UseOsCursor(true);

            ControllerInput.Update();
            WebBrowserManager.BeginFrame();

            HWND fg = PInvoke.GetForegroundWindow();

            HitTestRegions.BeginFrame();

            if (AppState.ShowFirstStart || !AppState.FirstRunCompleted)
            {
                FirstStartWindow.Draw();
            }
            else
            {
                Playground.Draw(_dt);
            }

            HitTestRegions.ApplyToOverlay(overlay);

            imguiRenderer.Render(d3dHost.SwapChain);
            d3dHost.Present();

            if (fg == overlay.Hwnd && !_targetHwnd.IsNull && !WebBrowserManager.MouseOverWebRegion)
            {
                // Only steal focus back to Star Citizen if the mouse
                // is *not* currently over any web region.
                PInvoke.SetForegroundWindow(_targetHwnd);
            }

            if (WebBrowserManager.MouseOverWebRegion)
            {
                ImGuiInput.ForceHideOsCursor();
            }

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

    private static void CreateTrayIcon()
    {
        try
        {
            var icon = new Icon(SystemIcons.Application, 16, 16);

            try
            {
                string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "cassiopia.ico");

                if (File.Exists(iconPath))
                {
                    icon = new Icon(iconPath);
                }
            }
            catch { }

            var menu = new ContextMenuStrip();

            var restartItem = new ToolStripMenuItem("Restart Starboard");
            restartItem.Click += (_, _) => RestartApplication();

            var quitItem = new ToolStripMenuItem("Quit Starboard");
            quitItem.Click += (_, _) => QuitApplication();

            menu.Items.Add(restartItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(quitItem);

            _trayIcon = new NotifyIcon()
            {
                Text = "Starboard",
                Icon = icon,
                ContextMenuStrip = menu,
                Visible = true
            };
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to create tray icon: {ex.Message}");
        }
    }

    static bool LooksLikeSvg(byte[] data)
    {
        // Peek first ~512 bytes as text, ignore BOM/whitespace
        int probe = Math.Min(512, data.Length);
        var head = Encoding.UTF8.GetString(data, 0, probe).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');

        // Quick checks: <svg ...> tag or the SVG namespace
        return head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
            || head.Contains("http://www.w3.org/2000/svg", StringComparison.OrdinalIgnoreCase);
    }

    private static void RestartApplication()
    {
        try
        {
            string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath))
            {
                Process.Start(new ProcessStartInfo(exePath)
                {
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to restart Starboard from tray: {ex.Message}");
        }

        QuitApplication();
    }

    private static void QuitApplication()
    {
        try
        {
            _ctsForTray?.Cancel();
        }
        catch { }

        try
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
        }
        catch {}

        Environment.Exit(0);
    }
}

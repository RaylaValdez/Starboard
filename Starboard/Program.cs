using ImGuiNET;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.System;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using Starboard.Capture;
using Starboard.Detection;
using OpenCvSharp;

namespace Starboard;

/// <summary>
/// Entry point. Creates the overlay window, D3D host, and ImGui renderer.
/// Tracks the Star Citizen client window, mirrors its client-rect, and renders ImGui
/// only when the game is focused or when interactive mode is toggled.
/// </summary>
internal static class Program
{
    private static void Log(string msg)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
        Console.WriteLine(line);
        Debug.WriteLine(line);
    }

    // --- Constants / "magic numbers" ---
    private const int HotkeyId = 1;
    private const ushort VkF10 = 0x79;                // Toggle interact mode
    private const int MouseWheelDeltaPerNotch = 120;  // Standard Windows wheel delta
    private const int SleepWhenIdleMs = 50;
    private const int TrackerIntervalMs = 16;

    // --- Diagnostics-only state (no logic changes) ---
    private static bool _lastRenderEligible = false;
    private static bool _lastVisible = false;
    private static bool _lastFocus = false;


    private static bool _isInteractive = false;
    private static float _accumulatedWheel = 0;

    private static HWND _starCitizenHwnd;

    private static bool IsGameFocused() => PInvoke.GetForegroundWindow() == _starCitizenHwnd;

    private static CaptureService? _capture;
    private static MobiGlassDetector? _detector;
    private static volatile bool _mobiglassOpen;

    private static int _debugDumpedFrames = 0;

    private static System.Drawing.Rectangle _roiPixels; // single source of truth for ROI in client pixels
    private static System.Drawing.Rectangle _lastMobiFramePx; // if you already track a mobi frame, reuse it




    // relative to the centered MobiGlass frame (not the whole window)
    private const float relX = 0.14f;   // 3% from left of the frame
    private const float relY = 0.898f;   // bottom band
    private const float relW = 0.075f;   // width
    private const float relH = 0.10f;   // covers the bar height

    [STAThread]
    private static void Main()
    {
        unsafe
        {
            // 1) Resolve the Star Citizen HWND first.
            if (!TryFindStarCitizenWindow(out _starCitizenHwnd))
            {
                Logger.Warn("Star Citizen window not found. Retrying for a few seconds...");
                var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (DateTime.UtcNow < until && !TryFindStarCitizenWindow(out _starCitizenHwnd))
                    Thread.Sleep(250);
                if (_starCitizenHwnd == HWND.Null)
                {
                    Logger.Error("Could not find Star Citizen window", new InvalidOperationException("HWND null"));
                    return; // bail out cleanly instead of crashing
                }
            }
            Logger.Log($"[Init] Found Star Citizen");
        }
        


        using var overlay = new OverlayWindow("Starboard Overlay", _starCitizenHwnd);
        using var d3d = new D3DHost(overlay.Hwnd);
        using var imgui = new ImGuiRendererD3D11(d3d.Device, d3d.Context);

        // --- MobiGlass visual detection wiring (centered-frame + pixel ROI) ---

        // Get client size of the SC window
        PInvoke.GetClientRect(_starCitizenHwnd, out RECT cr);
        int clientW = cr.right;
        int clientH = cr.bottom;

        // Build the centered 16:9 MobiGlass frame and our pixel ROI inside it
        var mobiFrame = ComputeMobiFrame(clientW, clientH);
        _lastMobiFramePx = mobiFrame;              // <— add this

        var roiPx = RoiFromMobiRelative(mobiFrame, relX, relY, relW, relH);
        _roiPixels = roiPx;

        _capture = new CaptureService(
            hwnd: _starCitizenHwnd,
            roi: new RectangleF(roiPx.X, roiPx.Y, roiPx.Width, roiPx.Height),
            roiIsNormalized: false,
            frequencyHz: 15);


        // Start capture using ABSOLUTE PIXELS
        _capture = new CaptureService(
            hwnd: _starCitizenHwnd,
            roi: new RectangleF(roiPx.X, roiPx.Y, roiPx.Width, roiPx.Height),
            roiIsNormalized: false,
            frequencyHz: 15);
        _capture.Start();

        // Detector (point at your bottom-left bar template)
        _detector = new MobiGlassDetector(
            templatePath: "Assets/Templates/mobiglass_logo.png",
            threshold: 0.625,     // start a hair stricter for bold icon
            confirmFrames: 3,
            downscale: 1);

        // Bridge frames and react
        _capture.FrameReady += (mat, tsUtc) => { using (mat) _detector.FeedFrame(mat); };
        _detector.StateChanged += (isOpen, score) =>
        {
                _mobiglassOpen = isOpen;
            overlay.Visible = isOpen && !PInvoke.IsIconic(_starCitizenHwnd); // optional
            //Logger.Log($"[MobiGlass] Overlay.Visible={overlay.Visible} (IsIconic={PInvoke.IsIconic(_starCitizenHwnd)})");

            //Debug.WriteLine($"[MobiGlass] {(isOpen ? "OPEN" : "CLOSED")}  score={score:F3}");
            

        };




        // Initial placement — client area in screen coordinates so we avoid title bar/borders.
        PInvoke.GetClientRect(_starCitizenHwnd, out RECT clientRect);
        System.Drawing.Point clientTopLeftScreen = new System.Drawing.Point(0, 0);
        PInvoke.ClientToScreen(_starCitizenHwnd, ref clientTopLeftScreen);

        RECT initialRect = new RECT
        {
            left = clientTopLeftScreen.X,
            top = clientTopLeftScreen.Y,
            right = clientTopLeftScreen.X + clientRect.right,
            bottom = clientTopLeftScreen.Y + clientRect.bottom
        };
        overlay.UpdateBounds(initialRect);

        // Track SC window changes (size/pos/minimize) on a light polling thread
        var cancellation = new CancellationTokenSource();
        var trackerThread = new Thread(() => TrackStarCitizen(cancellation.Token, overlay))
        {
            IsBackground = true
        };
        trackerThread.Start();

        // Receive raw keyboard input even when we don't have focus (so F10 works while SC is focused).
        RegisterKeyboardRawInput(overlay.Hwnd);

        // Fallback global hotkey in case raw input fails (or when focused).
        PInvoke.RegisterHotKey(HWND.Null, HotkeyId, 0, VkF10);

        // Close overlay when Star Citizen exits.
        unsafe
        {
            uint pid = 0;
            PInvoke.GetWindowThreadProcessId(_starCitizenHwnd, &pid);
            var scProc = Process.GetProcessById((int)pid);
            scProc.EnableRaisingEvents = true;
            scProc.Exited += (_, __) =>
            {
                PInvoke.PostMessage(overlay.Hwnd, PInvoke.WM_CLOSE, default, default);
            };
        }

        // ---------------------------
        // Message pump + rendering
        // ---------------------------
        MSG msg;
        bool running = true;
        while (running)
        {
            // Track focus/visible/eligibility transitions (emit once per change).
            bool focusNow = IsGameFocused();
            if (focusNow != _lastFocus)
            {
                //Logger.Log($"[Focus] Star Citizen focused={focusNow}");
                _lastFocus = focusNow;
            }

            bool visibleNow = overlay.Visible;
            if (visibleNow != _lastVisible)
            {
                //Logger.Log($"[Overlay] Visible={visibleNow}");
                _lastVisible = visibleNow;
            }

            bool eligibleNow = visibleNow && (focusNow || _isInteractive);
            if (eligibleNow != _lastRenderEligible)
            {
                //Logger.Log($"[Render] Eligibility → {eligibleNow} (Visible={visibleNow}, Focused={focusNow}, Interactive={_isInteractive})");
                _lastRenderEligible = eligibleNow;
            }

            while (PInvoke.PeekMessage(out msg, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
            {
                switch (msg.message)
                {
                    case var m when m == PInvoke.WM_QUIT:
                        running = false;
                        break;

                    case var m when m == PInvoke.WM_HOTKEY:
                        if (msg.wParam == HotkeyId)
                        {
                            overlay.ToggleClickThrough();
                            _isInteractive = !_isInteractive;
                            //Logger.Log($"[Render] Post-toggle eligibility check: {(overlay.Visible && (IsGameFocused() || _isInteractive))}");

                        }
                        break;

                    case var m when m == PInvoke.WM_MOUSEWHEEL:
                    {
                        short delta = (short)((msg.wParam.Value >> 16) & 0xFFFF);
                        _accumulatedWheel += delta / (float)MouseWheelDeltaPerNotch;
                        //Logger.Log($"[Input] MouseWheel delta={delta} accum={_accumulatedWheel:F2}");

                        break;
                    }

                    case var m when m == PInvoke.WM_INPUT:
                        //Logger.Log("[Input] WM_INPUT received");

                        HandleRawInput(msg.lParam, overlay);
                        break;

                    case var m when m == PInvoke.WM_CHAR:
                        if (_isInteractive)
                            ImGui.GetIO().AddInputCharacter((uint)msg.wParam.Value);
                        break;

                    case var m when m == PInvoke.WM_KEYDOWN || m == PInvoke.WM_SYSKEYDOWN ||
                                    m == PInvoke.WM_KEYUP   || m == PInvoke.WM_SYSKEYUP:
                        if (_isInteractive)
                        {
                            bool isDown = (m == PInvoke.WM_KEYDOWN || m == PInvoke.WM_SYSKEYDOWN);
                            var key = VkToImGuiKey((int)msg.wParam.Value);
                            if (key != ImGuiKey.None)
                                ImGui.GetIO().AddKeyEvent(key, isDown);

                            // Modifiers
                            var io = ImGui.GetIO();
                            io.KeyCtrl  = (PInvoke.GetKeyState(0x11) & 0x8000) != 0; // VK_CONTROL
                            io.KeyShift = (PInvoke.GetKeyState(0x10) & 0x8000) != 0; // VK_SHIFT
                            io.KeyAlt   = (PInvoke.GetKeyState(0x12) & 0x8000) != 0; // VK_MENU
                            io.KeySuper = (PInvoke.GetKeyState(0x5B) & 0x8000) != 0   // VK_LWIN
                                       || (PInvoke.GetKeyState(0x5C) & 0x8000) != 0;  // VK_RWIN
                        }
                        break;
                }

                PInvoke.TranslateMessage(msg);
                PInvoke.DispatchMessage(msg);
            }

            if (overlay.Visible && (IsGameFocused() || _isInteractive))
            {
                d3d.EnsureSize(overlay.ClientWidth, overlay.ClientHeight);

                d3d.BeginFrame();
                //Logger.Log($"[ImGui] Update mouse state (interactive={_isInteractive})");

                UpdateImGuiMouseState(overlay.Hwnd, _isInteractive);
                imgui.NewFrame(overlay.ClientWidth, overlay.ClientHeight);

                // --- Example UI ---
                ImGui.SetNextWindowBgAlpha(0.9f);
                ImGui.Begin("Starboard", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoBackground);
                ImGui.Text("F10 toggles click-through");
                var roi = _roiPixels;
                ImGui.Text($"ROI: ({roi.X}, {roi.Y})  {roi.Width}x{roi.Height}");
                ImGui.End();

                var dl = ImGui.GetForegroundDrawList();
                dl.AddRect(
                    new Vector2(roi.X, roi.Y),
                    new Vector2(roi.X + roi.Width, roi.Y + roi.Height),
                    0xFFFFFFFF, 0f, 0, 2f);

                // Use your last known mobi frame (whatever you compute in TrackStarCitizen).
                // If you don't have one handy, set mobi = new Rectangle(0,0,(int)io.DisplaySize.X,(int)io.DisplaySize.Y)
                var mobi = _lastMobiFramePx;

                // Size/position tuned to your screenshot: bottom-right of the app bar
                var io = ImGui.GetIO();
                float s = Math.Max(io.DisplayFramebufferScale.X, 1.0f); // simple DPI-ish scale
                var size = new Vector2(200 * s, 84 * s);                // width, height
                var margin = 24 * s;

                var pos = new Vector2(
                    mobi.Right - size.X - margin,
                    mobi.Bottom - size.Y - margin
                );

                DrawMobiPillButton(pos, size, "APPLETS", "SB", active: false);



                imgui.Render(d3d.SwapChain);
                d3d.Present();
            }
            else
            {
                Thread.Sleep(SleepWhenIdleMs);
            }
        }
        try { _capture?.Stop(); } catch { }
        try { _capture?.Dispose(); } catch { }
        try { _detector?.Dispose(); } catch { }


        cancellation.Cancel();
        PInvoke.UnregisterHotKey(HWND.Null, HotkeyId);
    }

    /// <summary>
    /// Polls the Star Citizen window to keep the overlay aligned to the client area.
    /// </summary>
    private static void TrackStarCitizen(CancellationToken token, OverlayWindow overlay)
    {
        RECT lastRect = default;
        bool lastMinimized = false;
        while (!token.IsCancellationRequested)
        {
            // Compute client rect in screen coordinates (client size + top-left in screen space).
            PInvoke.GetClientRect(_starCitizenHwnd, out RECT client);
            System.Drawing.Point topLeft = new System.Drawing.Point(0, 0);
            PInvoke.ClientToScreen(_starCitizenHwnd, ref topLeft);

            RECT currentRect = new RECT
            {
                left = topLeft.X,
                top = topLeft.Y,
                right = topLeft.X + client.right,
                bottom = topLeft.Y + client.bottom
            };
            bool isMinimized = PInvoke.IsIconic(_starCitizenHwnd);

            if (currentRect.left != lastRect.left || currentRect.top != lastRect.top ||
                currentRect.right != lastRect.right || currentRect.bottom != lastRect.bottom)
            {
                overlay.UpdateBounds(currentRect);
                lastRect = currentRect;

                int w = currentRect.right - currentRect.left;
                int h = currentRect.bottom - currentRect.top;
                var mobiNow = ComputeMobiFrame(w, h);
                _lastMobiFramePx = mobiNow;                 // <— add this
                var roiNow = RoiFromMobiRelative(mobiNow, relX, relY, relW, rh: relH);
                _roiPixels = roiNow;
                _capture?.UpdateRoi(new RectangleF(roiNow.X, roiNow.Y, roiNow.Width, roiNow.Height), isNormalized: false);

            }

            if (isMinimized != lastMinimized)
            {
                overlay.Visible = !isMinimized;
                lastMinimized = isMinimized;
            }

            Thread.Sleep(TrackerIntervalMs);
        }
    }

    /// <summary>
    /// Find the Star Citizen main window by enumerating top-level windows and checking process names.
    /// </summary>
    private static bool TryFindStarCitizenWindow(out HWND hwnd)
    {
        hwnd = HWND.Null;
        HWND found = HWND.Null;

        PInvoke.EnumWindows(new WNDENUMPROC((candidate, _) =>
        {
            // Get PID (CsWin32 projects the pointer overload).
            unsafe
            {
                uint pid = 0;
                PInvoke.GetWindowThreadProcessId(candidate, &pid);

                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    if (!proc.ProcessName.Equals("StarCitizen", StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (!PInvoke.IsWindowVisible(candidate)) return true;

                    found = candidate;  // capture to local, not the out param
                    return false;       // stop enumerating
                }
                catch
                {
                    return true;        // process might have exited; continue enumeration
                }
            }
        }), default);

        hwnd = found;
        return hwnd != HWND.Null;
    }

    /// <summary>
    /// Update ImGui mouse state from the OS. When not interactive, clear state so ImGui ignores the mouse.
    /// </summary>
    private static void UpdateImGuiMouseState(HWND hwnd, bool interactive)
    {
        var io = ImGui.GetIO();

        if (!interactive)
        {
            io.MousePos = new Vector2(-1, -1);
            io.MouseDown[0] = io.MouseDown[1] = io.MouseDown[2] = false;
            io.MouseWheel = 0;
            return;
        }

        // Position
        PInvoke.GetCursorPos(out System.Drawing.Point cursorScreen);
        PInvoke.ScreenToClient(hwnd, ref cursorScreen);
        io.MousePos = new Vector2(cursorScreen.X, cursorScreen.Y);

        // Buttons (high bit means down). Using WinRT VirtualKey is fine here.
        io.MouseDown[0] = PInvoke.GetAsyncKeyState((int)VirtualKey.LeftButton) < 0;
        io.MouseDown[1] = PInvoke.GetAsyncKeyState((int)VirtualKey.RightButton) < 0;
        io.MouseDown[2] = PInvoke.GetAsyncKeyState((int)VirtualKey.MiddleButton) < 0;

        // Wheel (accumulated in the message loop)
        io.MouseWheel = _accumulatedWheel;
        _accumulatedWheel = 0;
    }

    /// <summary>
    /// Register to receive raw keyboard input even when this window is not focused.
    /// </summary>
    private static unsafe void RegisterKeyboardRawInput(HWND hwnd)
    {
        var rid = new RAWINPUTDEVICE
        {
            usUsagePage = 0x01,  // Generic
            usUsage = 0x06,      // Keyboard
            dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK,
            hwndTarget = hwnd
        };
        _ = PInvoke.RegisterRawInputDevices(&rid, 1, (uint)sizeof(RAWINPUTDEVICE));
    }

    /// <summary>
    /// Handle WM_INPUT and toggle interactive mode when F10 is pressed.
    /// </summary>
    private static unsafe void HandleRawInput(LPARAM lParam, OverlayWindow overlay)
    {
        uint size = 0;
        var hraw = new HRAWINPUT(lParam.Value);

        PInvoke.GetRawInputData(hraw, RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, null, &size, (uint)sizeof(RAWINPUTHEADER));
        if (size == 0) return;

        byte* buffer = stackalloc byte[(int)size];
        if (PInvoke.GetRawInputData(hraw, RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, buffer, &size, (uint)sizeof(RAWINPUTHEADER)) != size)
            return;

        var raw = *(RAWINPUT*)buffer;
        if (raw.header.dwType != 1) return; // 1 = keyboard

        var kb = raw.data.keyboard;
        const ushort RiKeyBreak = 0x0001;

        if (kb.VKey == VkF10 && (kb.Flags & RiKeyBreak) == 0)
        {
            overlay.ToggleClickThrough();
            _isInteractive = !_isInteractive;
        }
    }

    /// <summary>
    /// Map Win32 virtual-key codes to ImGui keys for navigation.
    /// </summary>
    private static ImGuiKey VkToImGuiKey(int vk) => vk switch
    {
        0x08 => ImGuiKey.Backspace,     // VK_BACK
        0x09 => ImGuiKey.Tab,           // VK_TAB
        0x0D => ImGuiKey.Enter,         // VK_RETURN
        0x1B => ImGuiKey.Escape,        // VK_ESCAPE
        0x21 => ImGuiKey.PageUp,        // VK_PRIOR
        0x22 => ImGuiKey.PageDown,      // VK_NEXT
        0x23 => ImGuiKey.End,           // VK_END
        0x24 => ImGuiKey.Home,          // VK_HOME
        0x25 => ImGuiKey.LeftArrow,     // VK_LEFT
        0x26 => ImGuiKey.UpArrow,       // VK_UP
        0x27 => ImGuiKey.RightArrow,    // VK_RIGHT
        0x28 => ImGuiKey.DownArrow,     // VK_DOWN
        0x2D => ImGuiKey.Insert,        // VK_INSERT
        0x2E => ImGuiKey.Delete,        // VK_DELETE
        0x20 => ImGuiKey.Space,         // VK_SPACE
        // function keys (F1..F12)
        int n when n >= 0x70 && n <= 0x7B => ImGuiKey.F1 + (n - 0x70),
        _ => ImGuiKey.None
    };

    // The in-game MobiGlass surface is centered and ~16:9.
    // Build that frame in client pixels from the current window size.
    private static Rectangle ComputeMobiFrame(int clientW, int clientH)
    {
        const float aspect = 16f / 9f;

        // Fit a 16:9 rect inside the client, centered
        int targetW = Math.Min(clientW, (int)Math.Round(clientH * aspect));
        int targetH = (int)Math.Round(targetW / aspect);

        int x = (clientW - targetW) / 2;
        int y = (clientH - targetH) / 2;

        return new Rectangle(x, y, targetW, targetH);
    }

    // Convert a relative rect inside the MobiGlass frame (0..1) to client pixel ROI.
    private static Rectangle RoiFromMobiRelative(Rectangle mobi, float rx, float ry, float rw, float rh)
    {
        int x = mobi.X + (int)Math.Round(rx * mobi.Width);
        int y = mobi.Y + (int)Math.Round(ry * mobi.Height);
        int w = (int)Math.Round(rw * mobi.Width);
        int h = (int)Math.Round(rh * mobi.Height);
        return new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h));
    }

    private static void DrawMobiPillButton(
    Vector2 pos, Vector2 size, string label, string mono, bool active, float borderThickness = 2f)
    {
        var dl = ImGui.GetForegroundDrawList();
        var io = ImGui.GetIO();

        Vector2 min = pos;
        Vector2 max = pos + size;
        float rounding = size.Y * 0.5f; // capsule

        // Colors (tweak to taste). Using ImGui to pack RGBA correctly.
        uint colBg = ImGui.GetColorU32(new Vector4(0.05f, 0.12f, 0.18f, active ? 0.88f : 0.65f)); // deep bluish
        uint colBorder = ImGui.GetColorU32(new Vector4(0.75f, 0.90f, 1.00f, 0.90f));
        uint colGlow = ImGui.GetColorU32(new Vector4(0.20f, 0.65f, 1.00f, 0.20f));
        uint colText = ImGui.GetColorU32(new Vector4(0.90f, 0.98f, 1.00f, 0.95f));
        uint colSubText = ImGui.GetColorU32(new Vector4(0.80f, 0.95f, 1.00f, 0.80f));

        // Shadow/glow (soft offset)
        dl.AddRectFilled(min + new Vector2(0, 3), max + new Vector2(0, 3), colGlow, rounding);

        // Background + border
        dl.AddRectFilled(min, max, colBg, rounding);
        dl.AddRect(min, max, colBorder, rounding, ImDrawFlags.None, borderThickness);

        // Optional inner hairline to match SC style
        var inset = 3f;
        dl.AddRect(min + new Vector2(inset, inset), max - new Vector2(inset, inset),
            ImGui.GetColorU32(new Vector4(1, 1, 1, 0.15f)), rounding - inset, ImDrawFlags.None, 1f);

        // Left/right “notches” (purely decorative — tweak length/thickness)
        float notchLen = MathF.Min(size.Y * 0.40f, 26f);
        float notchXOff = size.X * 0.08f;
        float notchThk = 2f;
        var notchCol = ImGui.GetColorU32(new Vector4(0.85f, 0.95f, 1f, 0.70f));
        // Left
        dl.AddLine(new Vector2(min.X + notchXOff, min.Y + size.Y * 0.22f),
                   new Vector2(min.X + notchXOff, min.Y + size.Y * 0.22f + notchLen),
                   notchCol, notchThk);
        // Right
        dl.AddLine(new Vector2(max.X - notchXOff, min.Y + size.Y * 0.22f),
                   new Vector2(max.X - notchXOff, min.Y + size.Y * 0.22f + notchLen),
                   notchCol, notchThk);

        // Click handling (keeps draw order; geometry above still shows)
        ImGui.SetCursorScreenPos(min);
        ImGui.InvisibleButton("##mobi_pill_" + label, size);
        bool hovered = ImGui.IsItemHovered();
        bool pressed = ImGui.IsItemClicked();

        if (hovered)
        {
            // Subtle hover overlay
            dl.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(1, 1, 1, 0.06f)), rounding);
        }

        // Text layout: monogram big on top-left, label small under it
        float padX = size.X * 0.14f;
        float padY = size.Y * 0.18f;

        // Monogram (“SB”)
        {
            float monoScale = MathF.Min(size.Y * 0.50f, 36f);
            var monoSize = ImGui.CalcTextSize(mono);
            // scale “visually” by drawing at a bigger font size via ImGui::GetFont()->Scale would be global;
            // instead just offset to look right.
            var monoPos = new Vector2(min.X + padX, min.Y + padY);
            dl.AddText(monoPos, colText, mono);
        }

        // Label (“APPLETS”), aligned bottom-left inside the pill
        {
            var textSize = ImGui.CalcTextSize(label);
            var textPos = new Vector2(min.X + padX, max.Y - padY - textSize.Y);
            dl.AddText(textPos, colSubText, label);
        }

        // If you want this to do something:
        if (pressed)
        {
            // TODO: trigger your applet drawer or toggle
            // Logger.Log("Mobi pill clicked.");
        }
    }


}
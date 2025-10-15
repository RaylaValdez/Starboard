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

namespace Starboard;

/// <summary>
/// Entry point. Creates the overlay window, D3D host, and ImGui renderer.
/// Tracks the Star Citizen client window, mirrors its client-rect, and renders ImGui
/// only when the game is focused or when interactive mode is toggled.
/// </summary>
internal static class Program
{
    // --- Constants / "magic numbers" ---
    private const int HotkeyId = 1;
    private const ushort VkF10 = 0x79;                // Toggle interact mode
    private const int MouseWheelDeltaPerNotch = 120;  // Standard Windows wheel delta
    private const int SleepWhenIdleMs = 50;
    private const int TrackerIntervalMs = 16;

    private static bool _isInteractive = false;
    private static float _accumulatedWheel = 0;

    private static HWND _starCitizenHwnd;

    private static bool IsGameFocused() => PInvoke.GetForegroundWindow() == _starCitizenHwnd;

    [STAThread]
    private static void Main()
    {
        if (!TryFindStarCitizenWindow(out _starCitizenHwnd))
        {
            Console.WriteLine("Star Citizen not running (or no main window found).");
            return;
        }

        using var overlay = new OverlayWindow("Starboard Overlay", _starCitizenHwnd);
        using var d3d = new D3DHost(overlay.Hwnd);
        using var imgui = new ImGuiRendererD3D11(d3d.Device, d3d.Context);

        // Initial placement — client area in screen coordinates so we avoid title bar/borders.
        PInvoke.GetClientRect(_starCitizenHwnd, out RECT clientRect);
        Point clientTopLeftScreen = new Point(0, 0);
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
                        }
                        break;

                    case var m when m == PInvoke.WM_MOUSEWHEEL:
                    {
                        short delta = (short)((msg.wParam.Value >> 16) & 0xFFFF);
                        _accumulatedWheel += delta / (float)MouseWheelDeltaPerNotch;
                        break;
                    }

                    case var m when m == PInvoke.WM_INPUT:
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
                UpdateImGuiMouseState(overlay.Hwnd, _isInteractive);
                imgui.NewFrame(overlay.ClientWidth, overlay.ClientHeight);

                // --- Example UI ---
                ImGui.ShowDemoWindow();
                ImGui.SetNextWindowBgAlpha(0.9f);
                ImGui.Begin("Starboard", ImGuiWindowFlags.AlwaysAutoResize);
                ImGui.Text("F10 toggles click-through");
                unsafe { ImGui.Text($"Attached to StarCitizen window 0x{(nuint)_starCitizenHwnd.Value:X}"); }
                ImGui.End();

                imgui.Render(d3d.SwapChain);
                d3d.Present();
            }
            else
            {
                Thread.Sleep(SleepWhenIdleMs);
            }
        }

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
            Point topLeft = new Point(0, 0);
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
        PInvoke.GetCursorPos(out Point cursorScreen);
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
}
using System;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;
using Windows.Win32.Graphics.Gdi; // HRGN

namespace Starboard;

/// <summary>
/// Creates a top-level transparent, click-through overlay window that follows an owner window.
/// Uses WS_EX_LAYERED + DirectComposition for per-pixel alpha, and toggles WS_EX_TRANSPARENT /
/// WS_EX_NOACTIVATE to switch between pass-through and interactive modes.
/// </summary>
internal sealed class OverlayWindow : IDisposable
{
    // --- Constants / "magic numbers" ---
    private const string WindowClassName = "StarboardOverlayWindowClass";
    private const int DefaultClientWidth = 1280;
    private const int DefaultClientHeight = 720;
    private const byte LayeredWindowAlpha = 255; // We render our own alpha; keep the base window fully opaque
    private const uint DwmBlurEnableFlag = 0x1u; // DWM_BB_ENABLE (kept here for clarity even if fEnable=false)

    private readonly string _windowTitle;
    private readonly HWND _ownerHwnd;
    private WNDPROC _wndProcThunk; // keep delegate alive

    public readonly HWND Hwnd;

    public int ClientWidth { get; private set; } = DefaultClientWidth;
    public int ClientHeight { get; private set; } = DefaultClientHeight;

    public bool Visible
    {
        get => _isVisible;
        set
        {
            _isVisible = value;
            PInvoke.ShowWindow(Hwnd, value ? SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE : SHOW_WINDOW_CMD.SW_HIDE);
        }
    }
    private bool _isVisible = true;
    private bool _isClickThrough = true;

    public OverlayWindow(string title, HWND owner)
    {
        _windowTitle = title;
        _ownerHwnd = owner;
        _wndProcThunk = WndProc;

        unsafe
        {
            fixed (char* pClass = WindowClassName)
            {
                var windowClass = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    lpfnWndProc = _wndProcThunk,
                    hInstance = PInvoke.GetModuleHandle((PCWSTR)null),
                    lpszClassName = new PCWSTR(pClass)
                };
                PInvoke.RegisterClassEx(windowClass);

                // Popup layered tool window, initially click-through and non-activating.
                var extendedStyle = WINDOW_EX_STYLE.WS_EX_LAYERED
                                  | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW
                                  | WINDOW_EX_STYLE.WS_EX_TRANSPARENT
                                  | WINDOW_EX_STYLE.WS_EX_NOACTIVATE;

                var style = WINDOW_STYLE.WS_POPUP;

                Hwnd = PInvoke.CreateWindowEx(
                    extendedStyle,
                    windowClass.lpszClassName,
                    (PCWSTR)null,
                    style,
                    0, 0, 100, 100,            // Position/size adjusted later
                    _ownerHwnd,
                    HMENU.Null,
                    PInvoke.GetModuleHandle((PCWSTR)null),
                    null);
            }
        }

        // Fully opaque layered alpha; the swapchain provides per-pixel transparency.
        PInvoke.SetLayeredWindowAttributes(Hwnd, (COLORREF)0, LayeredWindowAlpha, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);

        // Optional blur — disabled by default now (fEnable=false) to avoid tint.
        unsafe
        {
            var blurBehind = new DWM_BLURBEHIND
            {
                fEnable = false,
                dwFlags = DwmBlurEnableFlag,
                hRgnBlur = HRGN.Null,
                fTransitionOnMaximized = false
            };
            PInvoke.DwmEnableBlurBehindWindow(Hwnd, blurBehind);
        }

        PInvoke.ShowWindow(Hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        PInvoke.UpdateWindow(Hwnd);
    }

    /// <summary>
    /// Toggle between click-through (pass input to the game) and interactive (capture input).
    /// </summary>
    public void ToggleClickThrough()
    {
        _isClickThrough = !_isClickThrough;
        var extendedStyle = (WINDOW_EX_STYLE)PInvoke.GetWindowLongPtr(Hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

        if (_isClickThrough)
        {
            // Pass mouse/keyboard through and avoid stealing focus.
            extendedStyle |= WINDOW_EX_STYLE.WS_EX_TRANSPARENT | WINDOW_EX_STYLE.WS_EX_NOACTIVATE;
            PInvoke.SetForegroundWindow(_ownerHwnd);
        }
        else
        {
            // Allow interaction: remove transparency and actively take focus.
            extendedStyle &= ~(WINDOW_EX_STYLE.WS_EX_TRANSPARENT | WINDOW_EX_STYLE.WS_EX_NOACTIVATE);
            PInvoke.SetForegroundWindow(Hwnd);
            PInvoke.SetActiveWindow(Hwnd);
            PInvoke.SetFocus(Hwnd);
        }

        PInvoke.SetWindowLongPtr(Hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (IntPtr)extendedStyle);
    }

    /// <summary>
    /// Position and size the overlay to match a client-rectangle (already in screen coordinates).
    /// </summary>
    public void UpdateBounds(RECT screenRect)
    {
        int width = screenRect.right - screenRect.left;
        int height = screenRect.bottom - screenRect.top;
        ClientWidth = Math.Max(1, width);
        ClientHeight = Math.Max(1, height);

        PInvoke.SetWindowPos(Hwnd, HWND.Null, screenRect.left, screenRect.top, width, height,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
    }

    private LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        switch (msg)
        {
            case PInvoke.WM_NCHITTEST:
                // When click-through is enabled, let the hit-test say "transparent" so mouse goes to the game.
                if (_isClickThrough) return (LRESULT)(-1); // HTTRANSPARENT
                break;

            case PInvoke.WM_DESTROY:
                PInvoke.PostQuitMessage(0);
                break;
        }
        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (Hwnd != HWND.Null) PInvoke.DestroyWindow(Hwnd);
    }
}
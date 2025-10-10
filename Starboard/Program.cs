using System;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

static class Program
{
    // -------- Constants --------
    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_PAINT = 0x000F;
    private const uint WM_HOTKEY = 0x0312;
    private const int HTTRANSPARENT = -1;
    private const int VK_F9 = 0x78;
    private const uint PATCOPY = 0x00F00021; // raster op for PatBlt


    // Alpha flag for SetLayeredWindowAttributes
    private const int LWA_ALPHA = 0x2;

    // -------- State --------
    private static HWND overlayWindowHandle;
    private static HWND gameWindowHandle;
    private static bool showDebugTint = true; // Toggle with F9

    // Root the delegate so the GC doesn't collect it.
    private static readonly WNDPROC WindowProcDelegate = WindowProc;

    public static unsafe void Main()
    {
        // 1) Find Star Citizen window; exit quietly if not found.
        fixed (char* title = "STAR CITIZEN")
        {
            gameWindowHandle = PInvoke.FindWindow(default, new PCWSTR(title));
        }
        if (gameWindowHandle == default)
            return;

        // 2) Register our window class (no icon, no cursor changes).
        HINSTANCE hInstance = PInvoke.GetModuleHandle((PCWSTR)null);
        var windowClass = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = WindowProcDelegate,
            hInstance = hInstance
        };
        fixed (char* className = "StarboardOverlay")
        {
            windowClass.lpszClassName = new PCWSTR(className);
            PInvoke.RegisterClassEx(windowClass);
        }

        // 3) Get Star Citizen’s window bounds (screen space).
        PInvoke.GetWindowRect(gameWindowHandle, out RECT gameRect);
        int gameWidth = gameRect.right - gameRect.left;
        int gameHeight = gameRect.bottom - gameRect.top;

        // 4) Create the overlay window:
        //    - WS_EX_TOOLWINDOW    : no taskbar button
        //    - WS_EX_TOPMOST       : always on top
        //    - WS_EX_LAYERED       : we control global opacity
        //    - WS_EX_TRANSPARENT   : click-through
        fixed (char* className = "StarboardOverlay")
        fixed (char* empty = "")
        {
            overlayWindowHandle = PInvoke.CreateWindowEx(
                WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TRANSPARENT | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
                new PCWSTR(className),
                new PCWSTR(empty),
                WINDOW_STYLE.WS_POPUP,
                gameRect.left, gameRect.top, gameWidth, gameHeight,
                default, default, hInstance, null
            );
        }

        // 5) Set initial opacity (slight tint); show the window.
        SetOverlayOpacity((byte)(showDebugTint ? 32 : 0)); // 0..255 (32 ~ 12.5% opaque)
        PInvoke.ShowWindow(overlayWindowHandle, SHOW_WINDOW_CMD.SW_SHOW);

        // 6) Register F9 hotkey to toggle the debug tint.
        PInvoke.RegisterHotKey(default, 1, 0, VK_F9);

        // 7) Message/align/exit loop.
        MSG message;
        while (true)
        {
            // Pump messages.
            while (PInvoke.PeekMessage(out message, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
            {
                if (message.message == WM_HOTKEY && (int)message.wParam.Value == 1)
                {
                    showDebugTint = !showDebugTint;
                    SetOverlayOpacity((byte)(showDebugTint ? 32 : 0));
                    // Force a repaint.
                    PInvoke.InvalidateRect(overlayWindowHandle, (RECT?)null, new BOOL(true));

                }

                PInvoke.TranslateMessage(in message);
                PInvoke.DispatchMessage(in message);
            }

            // If the game window vanished, exit the overlay.
            fixed (char* title = "STAR CITIZEN")
            {
                HWND probe = PInvoke.FindWindow(default, new PCWSTR(title));
                if (probe == default)
                {
                    PInvoke.DestroyWindow(overlayWindowHandle);
                    return;
                }
                gameWindowHandle = probe;
            }

            // Keep overlay aligned to the game window.
            PInvoke.GetWindowRect(gameWindowHandle, out gameRect);
            PInvoke.MoveWindow(
                overlayWindowHandle,
                gameRect.left, gameRect.top,
                gameRect.right - gameRect.left,
                gameRect.bottom - gameRect.top,
                false);
        }
    }

    // Set uniform opacity on the layered window.
    private static void SetOverlayOpacity(byte alpha0to255)
    {
        // COLORREF(0) + LWA_ALPHA → uniform alpha for the whole window.
        PInvoke.SetLayeredWindowAttributes(
            overlayWindowHandle,
            new COLORREF(0),
            alpha0to255,
            (LAYERED_WINDOW_ATTRIBUTES_FLAGS)LWA_ALPHA);
    }

    // -------- Window procedure --------
    private static LRESULT WindowProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                // Make the overlay click-through.
                return new LRESULT(HTTRANSPARENT);

            case WM_PAINT:
                // Paint a flat color so the alpha actually shows up (DWM blends the pixels).
                DrawDebugTint(hwnd);
                return new LRESULT(0);
        }

        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    // GDI paint routine: fill the client area with a solid color.
    // The layered window alpha controls the final visible intensity.
    unsafe private static void DrawDebugTint(HWND hwnd)
    {
        // Begin paint
        PAINTSTRUCT paintStruct;
        HDC hdc = PInvoke.BeginPaint(hwnd, out paintStruct);

        // Create a solid brush (soft bluish-gray dev cue)
        COLORREF fillColor = new COLORREF(0x30_38_40); // 0x00BBGGRR
        HBRUSH hBrush = PInvoke.CreateSolidBrush(fillColor);

        // Select brush into DC
        var brushObj = new Windows.Win32.Graphics.Gdi.HGDIOBJ(hBrush.Value);
        HGDIOBJ oldObj = PInvoke.SelectObject(hdc, brushObj);

        // Fill the invalidated area with the brush using PatBlt
        int left = paintStruct.rcPaint.left;
        int top = paintStruct.rcPaint.top;
        int width = paintStruct.rcPaint.right - paintStruct.rcPaint.left;
        int height = paintStruct.rcPaint.bottom - paintStruct.rcPaint.top;
        PInvoke.PatBlt(hdc, left, top, width, height, (ROP_CODE)PATCOPY);

        // Restore old GDI object
        PInvoke.SelectObject(hdc, oldObj);

        // Cleanup GDI brush
        PInvoke.DeleteObject(brushObj);

        // End paint
        PInvoke.EndPaint(hwnd, paintStruct);
    }


}

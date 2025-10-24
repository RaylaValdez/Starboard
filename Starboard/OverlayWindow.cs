using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Starboard;

internal sealed class OverlayWindow : IDisposable
{
    private const string WindowClassName = "StarboardOverlayWindowClass";
    private const int DefaultClientWidth = 1280;
    private const int DefaultClientHeight = 720;
    private const uint DwmBlurEnableFlag = 0x1u;

    private readonly HWND _ownerHwnd;
    private WNDPROC _wndProcThunk;
    public readonly HWND Hwnd;

    public int ClientWidth { get; private set; } = DefaultClientWidth;
    public int ClientHeight { get; private set; } = DefaultClientHeight;

    private bool _isVisible = false;
    private bool _forcePassThrough = false; // F10
    private RECT[] _hitRegions = Array.Empty<RECT>(); // client-space rects

    public bool Visible
    {
        get => _isVisible;
        set
        {
            _isVisible = value;
            PInvoke.ShowWindow(Hwnd, value ? SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE : SHOW_WINDOW_CMD.SW_HIDE);
        }
    }

    public OverlayWindow(string title, HWND owner)
    {
        _ownerHwnd = owner;
        _wndProcThunk = new WNDPROC(WndProc);

        unsafe
        {
            fixed (char* pClass = WindowClassName)
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    lpfnWndProc = _wndProcThunk,
                    hInstance = PInvoke.GetModuleHandle((PCWSTR)null),
                    lpszClassName = new PCWSTR(pClass)
                };
                PInvoke.RegisterClassEx(wc);

                // IMPORTANT: no WS_EX_LAYERED, no WS_EX_TRANSPARENT
                var ex = WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE;
                var style = WINDOW_STYLE.WS_POPUP;

                Hwnd = PInvoke.CreateWindowEx(
                    ex, wc.lpszClassName, (PCWSTR)null, style,
                    0, 0, 100, 100,
                    _ownerHwnd, HMENU.Null, PInvoke.GetModuleHandle((PCWSTR)null), null);
            }
        }

        unsafe
        {
            var bb = new DWM_BLURBEHIND
            {
                fEnable = false,
                dwFlags = DwmBlurEnableFlag,
                hRgnBlur = default,
                fTransitionOnMaximized = false
            };
            PInvoke.DwmEnableBlurBehindWindow(Hwnd, bb);
        }

        PInvoke.ShowWindow(Hwnd, SHOW_WINDOW_CMD.SW_HIDE);
        PInvoke.UpdateWindow(Hwnd);
    }

    /// Update the interactive regions (client coords). Call this every frame.
    public void SetHitTestRegions(ReadOnlySpan<RECT> regions)
    {
        _hitRegions = regions.ToArray();
    }

    /// Force everything to pass-through (F10 toggles this).
    public void ToggleClickThrough() => _forcePassThrough = !_forcePassThrough;

    /// Mirror the owner’s client rect (screen coords in).
    public void UpdateBounds(RECT screenRect)
    {
        int w = screenRect.right - screenRect.left;
        int h = screenRect.bottom - screenRect.top;
        ClientWidth = Math.Max(1, w);
        ClientHeight = Math.Max(1, h);

        PInvoke.SetWindowPos(Hwnd, HWND.Null, screenRect.left, screenRect.top, w, h,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOSENDCHANGING);
    }

    private LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        switch (msg)
        {
            case PInvoke.WM_NCHITTEST:
            {
                if (_forcePassThrough || !_isVisible) return (LRESULT)(-1); // HTTRANSPARENT

                // Use the current cursor to avoid sign/HIWORD fun
                PInvoke.GetCursorPos(out System.Drawing.Point pt);
                PInvoke.ScreenToClient(Hwnd, ref pt);

                for (int i = 0; i < _hitRegions.Length; i++)
                {
                    var r = _hitRegions[i];
                    if (pt.X >= r.left && pt.X < r.right && pt.Y >= r.top && pt.Y < r.bottom)
                        return (LRESULT)1; // HTCLIENT
                }
                return (LRESULT)(-1); // HTTRANSPARENT
            }

            case PInvoke.WM_DESTROY:
                PInvoke.PostQuitMessage(0);
                break;
        }
        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (!Hwnd.IsNull) PInvoke.DestroyWindow(Hwnd);
    }
}

using OpenCvSharp;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Starboard.Capture
{
    public sealed class CaptureService : IDisposable
    {
        public event Action<Mat, DateTime>? FrameReady;
        public IntPtr Hwnd { get; }
        public int FrequencyHz { get; }
        public bool RoiIsNormalized { get; }
        public RectangleF Roi { get; private set; }

        private IntPtr _windowDc = IntPtr.Zero;
        private IntPtr _memoryDc = IntPtr.Zero;
        private IntPtr _bitmap = IntPtr.Zero;
        private IntPtr _bitmapOld = IntPtr.Zero;
        private System.Drawing.Size _backingSize = System.Drawing.Size.Empty;
        private CancellationTokenSource? _cts;
        private Task? _loop;
        private readonly object _sync = new();

        private const int DefaultFrequencyHz = 5;
        private const int PrintWindowFlag = 0x00000002;

        public CaptureService(IntPtr hwnd, RectangleF roi, bool roiIsNormalized = true, int frequencyHz = DefaultFrequencyHz)
        {
            if (hwnd == IntPtr.Zero) throw new ArgumentException("HWND cannot be zero.", nameof(hwnd));
            if (frequencyHz <= 0) throw new ArgumentOutOfRangeException(nameof(frequencyHz));

            Hwnd = hwnd;
            Roi = roi;
            RoiIsNormalized = roiIsNormalized;
            FrequencyHz = frequencyHz;
        }

        public void UpdateRoi(RectangleF newRoi, bool isNormalized)
        {
            lock (_sync)
            {
                Roi = newRoi;
                Logger.Log($"[Capture] ROI updated: {Roi} normalized={isNormalized}");
            }
        }

        public void Start()
        {
            if (_loop != null) return;

            Logger.Log($"[Capture] Starting loop for HWND 0x{Hwnd.ToInt64():X}");
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => RunLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            Logger.Log("[Capture] Stopping loop");
            _cts?.Cancel();
            try { _loop?.Wait(); } catch { }
            _loop = null;
            _cts?.Dispose();
            _cts = null;
        }

        private async Task RunLoopAsync(CancellationToken token)
        {
            var delayMs = (int)Math.Round(1000.0 / FrequencyHz);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var client = GetClientSize(Hwnd);
                    if (!IsWindowVisible(Hwnd) || client.Width <= 0 || client.Height <= 0)
                    {
                        //Logger.Log("[Capture] Window not visible or invalid client rect");
                        await Task.Delay(delayMs, token).ConfigureAwait(false);
                        continue;
                    }

                    EnsureBackbuffers(client);
                    bool ok = PrintWindow(Hwnd, _memoryDc, PrintWindowFlag);
                    if (!ok) Logger.Log("[Capture] PrintWindow failed; falling back to BitBlt");

                    using (var bmp = System.Drawing.Image.FromHbitmap(_bitmap))
                    {
                        var roiPx = ComputePixelRoi(bmp.Width, bmp.Height);
                        using var cropped = new Bitmap(roiPx.Width, roiPx.Height, PixelFormat.Format24bppRgb);
                        using (var g = Graphics.FromImage(cropped))
                        {
                            g.DrawImage(bmp, new Rectangle(0, 0, roiPx.Width, roiPx.Height), roiPx, GraphicsUnit.Pixel);
                        }

                        var mat = BitmapToMat(cropped);
                        //Logger.Log($"[Capture] Emitting frame {mat.Width}x{mat.Height}");
                        FrameReady?.Invoke(mat, DateTime.UtcNow);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Logger.Error("[Capture] Exception during capture", ex);
                }
                finally
                {
                    try { await Task.Delay(delayMs, token).ConfigureAwait(false); } catch { }
                }
            }
        }

        private void EnsureBackbuffers(System.Drawing.Size client)
        {
            if (client == _backingSize && _memoryDc != IntPtr.Zero && _bitmap != IntPtr.Zero) return;
            //Logger.Log($"[Capture] Reallocating buffers for {client.Width}x{client.Height}");

            if (_bitmapOld != IntPtr.Zero && _memoryDc != IntPtr.Zero)
            {
                SelectObject(_memoryDc, _bitmapOld);
                _bitmapOld = IntPtr.Zero;
            }
            if (_bitmap != IntPtr.Zero) { DeleteObject(_bitmap); _bitmap = IntPtr.Zero; }
            if (_memoryDc != IntPtr.Zero) { DeleteDC(_memoryDc); _memoryDc = IntPtr.Zero; }
            if (_windowDc != IntPtr.Zero) { ReleaseDC(Hwnd, _windowDc); _windowDc = IntPtr.Zero; }

            _windowDc = GetDC(Hwnd);
            _memoryDc = CreateCompatibleDC(_windowDc);
            _bitmap = CreateCompatibleBitmap(_windowDc, client.Width, client.Height);
            _bitmapOld = SelectObject(_memoryDc, _bitmap);
            _backingSize = client;
        }

        private static Mat BitmapToMat(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                var mat = Mat.FromPixelData(bmp.Height, bmp.Width, MatType.CV_8UC3, data.Scan0, data.Stride);
                return mat.Clone();
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private Rectangle ComputePixelRoi(int clientW, int clientH)
        {
            RectangleF roi;
            lock (_sync) { roi = Roi; }

            int x = (int)Math.Round(roi.X);
            int y = (int)Math.Round(roi.Y);
            int w = (int)Math.Round(roi.Width);
            int h = (int)Math.Round(roi.Height);
            x = Math.Clamp(x, 0, Math.Max(0, clientW - 1));
            y = Math.Clamp(y, 0, Math.Max(0, clientH - 1));
            w = Math.Clamp(w, 1, clientW - x);
            h = Math.Clamp(h, 1, clientH - y);
            return new Rectangle(x, y, w, h);
        }

        private static System.Drawing.Size GetClientSize(IntPtr hwnd)
        {
            if (!GetClientRect(hwnd, out RECT rc)) return System.Drawing.Size.Empty;
            return new System.Drawing.Size(Math.Max(0, rc.Right - rc.Left), Math.Max(0, rc.Bottom - rc.Top));
        }

        public void Dispose()
        {
            Stop();
            if (_bitmapOld != IntPtr.Zero && _memoryDc != IntPtr.Zero)
            {
                SelectObject(_memoryDc, _bitmapOld);
                _bitmapOld = IntPtr.Zero;
            }
            if (_bitmap != IntPtr.Zero) { DeleteObject(_bitmap); _bitmap = IntPtr.Zero; }
            if (_memoryDc != IntPtr.Zero) { DeleteDC(_memoryDc); _memoryDc = IntPtr.Zero; }
            if (_windowDc != IntPtr.Zero) { ReleaseDC(Hwnd, _windowDc); _windowDc = IntPtr.Zero; }
            Logger.Log("[Capture] Disposed.");
        }

        [DllImport("user32.dll", SetLastError = true)] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int nFlags);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll", SetLastError = true)] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    }
}

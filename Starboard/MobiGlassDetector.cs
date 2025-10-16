using System;
using OpenCvSharp;

namespace Starboard.Detection
{
    public sealed class MobiGlassDetector : IDisposable
    {
        public event Action<bool, double>? StateChanged;
        public bool IsOpen { get; private set; }
        public double LastScore { get; private set; }
        public double Threshold { get; private set; }
        public int ConfirmFrames { get; private set; }
        public double Downscale { get; private set; }

        private readonly object _sync = new();
        private Mat _templateGray;
        private Mat? _templateScaled;
        private int _streak;
        private int _debugDumpedFrames = 0;
        private int _debugDumped = 0;


        public MobiGlassDetector(string templatePath, double threshold = 0.88, int confirmFrames = 3, double downscale = 0.5)
        {
            Threshold = threshold;
            ConfirmFrames = confirmFrames;
            Downscale = downscale;

            using var src = Cv2.ImRead(templatePath, ImreadModes.Unchanged);
            if (src.Empty()) throw new InvalidOperationException($"Failed to load template: {templatePath}");

            _templateGray = ToGray(src);
            BuildScaledTemplate();
            Logger.Log($"[Detector] Loaded template {templatePath} ({_templateGray.Width}x{_templateGray.Height})");
        }

        public void FeedFrame(Mat roiBgr)
        {
            if (roiBgr == null || roiBgr.Empty()) return;

            try
            {
                //Logger.Log($"[Detector] FeedFrame {roiBgr.Width}x{roiBgr.Height}");
                using var gray = ToGray(roiBgr);
                using var scaled = (Downscale < 1.0) ? gray.Resize(new Size(), Downscale, Downscale, InterpolationFlags.Area) : gray.Clone();
                using var roiEdges = new Mat();
                Cv2.Canny(scaled, roiEdges, 50, 150);
                Cv2.Dilate(roiEdges, roiEdges, new Mat(), iterations: 1);
                // Clone the shared template ONCE so we never dispose the field accidentally
                Mat tplBase;
                lock (_sync)
                {
                    if (_templateGray == null || _templateGray.Empty())
                    {
                        Logger.Log("[Detector] Template not ready (null/empty).");
                        LastScore = 0;
                        Debounce(false, 0);
                        return;
                    }
                    tplBase = (_templateScaled ?? _templateGray).Clone();
                }

                // Multi-scale sweep to tolerate UI scaling
                double best = 0.0;
                double[] scales = { 0.60, 0.70, 0.80, 0.90, 1.00, 1.10, 1.25, 1.40, 1.60 };

                foreach (var s in scales)
                {
                    Mat tplScaledIter = tplBase;
                    if (Math.Abs(s - 1.0) > 1e-6)
                        tplScaledIter = tplBase.Resize(new Size(0,0), s, s, InterpolationFlags.Area);

                    if (roiEdges.Width >= tplScaledIter.Width && roiEdges.Height >= tplScaledIter.Height)
                    {
                        using var tplEdges = new Mat();
                        //Cv2.GaussianBlur(tplScaledIter, tplScaledIter, new Size(3, 3), 0);
                        Cv2.Canny(tplScaledIter, tplEdges, 50, 150);
                        Cv2.Dilate(tplEdges, tplEdges, new Mat(), iterations: 1);
                        using var result = new Mat();
                        Cv2.MatchTemplate(roiEdges, tplEdges, result, TemplateMatchModes.CCoeffNormed);
                        Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out _);
                        if (maxVal > best) best = maxVal;
                    }

                    if (!ReferenceEquals(tplScaledIter, tplBase))
                        tplScaledIter.Dispose();
                }

                tplBase.Dispose();

                LastScore = best;
                bool detected = best >= Threshold;
                Logger.Log($"[Detector] MaxScore={best:F3}, Threshold={Threshold:F3}, Detected={detected}");

                // Optional: dump a couple ROI frames for sanity when score looks promising
                if (LastScore > 0.30 && _debugDumpedFrames < 3)
                {
                    var path = $"debug_roi_{DateTime.Now:HHmmss_fff}_{(int)(LastScore * 1000)}.png";
                    Cv2.ImWrite(path, roiBgr);
                    Logger.Log($"[DebugDump] Saved ROI frame to {path} (score={LastScore:F3})");
                    _debugDumpedFrames++;
                }

                Debounce(detected, best);
            }
            catch (Exception ex)
            {
                Logger.Error("[Detector] Exception in FeedFrame", ex);
            }
        }

        private void Debounce(bool detected, double score)
        {
            if (detected == IsOpen)
            {
                _streak = 0;
                return;
            }

            _streak++;
            Logger.Log($"[Detector] Debounce: detected={detected}, streak={_streak}/{ConfirmFrames}");

            if (_streak >= ConfirmFrames)
            {
                IsOpen = detected;
                _streak = 0;
                Logger.Log($"[Detector] StateChanged: {(IsOpen ? "OPEN" : "CLOSED")} score={score:F3}");
                StateChanged?.Invoke(IsOpen, score);
            }
        }

        private static Mat ToGray(Mat src)
        {
            if (src.Empty()) return new Mat();
            int ch = src.Channels();
            if (ch == 1) return src.Clone();
            var gray = new Mat();
            var code = (ch == 4) ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY;
            Cv2.CvtColor(src, gray, code);
            return gray;
        }

        private void BuildScaledTemplate()
        {
            _templateScaled?.Dispose();
            _templateScaled = (Downscale < 1.0)
                ? _templateGray.Resize(new Size(), Downscale, Downscale, InterpolationFlags.Area)
                : null;
        }

        public void Dispose()
        {
            _templateGray?.Dispose();
            _templateScaled?.Dispose();
            Logger.Log("[Detector] Disposed.");
        }
    }
}

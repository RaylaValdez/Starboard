using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Overlay_Renderer;
using Overlay_Renderer.Helpers;
using Windows.Win32.Foundation;

namespace Starboard
{
    internal static class FileDropManager
    {
        private static readonly string _externAppletDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Starboard",
            "ExternApplets");

        internal static List<string> ProcessExternalAppletDrops(RectangleF dropZone)
        {
            var result = new List<string>();

            var drops = OverlayWindow.TakePendingFileDrops();
            if (drops == null || drops.Count == 0)
                return result;

            Directory.CreateDirectory(_externAppletDir);

            foreach (var (path, pt) in drops)
            {
                if (pt.X < dropZone.Left || pt.X > dropZone.Right ||
                    pt.Y < dropZone.Top || pt.Y > dropZone.Bottom)
                {
                    continue;
                }

                if (!File.Exists(path))
                    continue;

                if (!string.Equals(Path.GetExtension(path), ".lua", StringComparison.OrdinalIgnoreCase))
                    continue;

                var destPath = Path.Combine(_externAppletDir, Path.GetFileName(path));

                try
                {
                    File.Copy(path, destPath, overwrite: true);
                    result.Add(destPath);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to copy dropped applet '{path}' -> '{destPath}': {ex.Message}");
                }
            }

            return result;
        }
    }
}

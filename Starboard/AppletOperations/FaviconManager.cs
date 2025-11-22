using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading.Tasks;

namespace Starboard
{
    /// <summary>
    /// Downloads favicons in the background, then asks the render thread
    /// to create textures via TextureUploader. Call ProcessPendingUploads()
    /// once per frame (main/render thread).
    /// </summary>
    internal static class FaviconManager
    {
        // Set this from Program.cs. It MUST run on the render thread.
        public static Func<byte[], IntPtr>? TextureUploader { get; set; }

        public static int IconSizePx = 32;

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        // appletId -> ImGui texture handle
        private static readonly ConcurrentDictionary<string, IntPtr> _cache = new();

        // appletId -> requested once flag
        private static readonly ConcurrentDictionary<string, bool> _requested = new();

        // queue of completed downloads to upload on render thread
        private static readonly ConcurrentQueue<(string id, byte[] bytes)> _pending = new();

        /// <summary>Get a cached texture or IntPtr.Zero if not ready. Starts download once.</summary>
        public static IntPtr GetOrRequest(string appletId, string? faviconUrl)
        {
            if (string.IsNullOrWhiteSpace(appletId))
                return IntPtr.Zero;

            if (_cache.TryGetValue(appletId, out var tex) && tex != IntPtr.Zero)
                return tex;

            if (!string.IsNullOrWhiteSpace(faviconUrl) && _requested.TryAdd(appletId, true))
            {
                _ = DownloadAsync(appletId, faviconUrl);
            }

            return IntPtr.Zero;
        }

        /// <summary>Call from the render thread once per frame.</summary>
        public static void ProcessPendingUploads()
        {
            var uploader = TextureUploader;
            if (uploader == null) return;

            while (_pending.TryDequeue(out var item))
            {
                try
                {
                    var tex = uploader(item.bytes);
                    if (tex != IntPtr.Zero)
                        _cache[item.id] = tex;
                }
                catch
                {
                    // ignore upload failures (fallback will render)
                }
            }
        }

        private static async Task DownloadAsync(string appletId, string url)
        {
            try
            {
                var bytes = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
                if (bytes is { Length: > 0 })
                    _pending.Enqueue((appletId, bytes));
            }
            catch
            {
                // swallow – we just never get an icon for this applet
            }
        }
    }
}

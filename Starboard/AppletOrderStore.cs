using Newtonsoft.Json;
using Overlay_Renderer.Helpers;
using Starboard.DistributedApplets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Starboard
{
    internal static class AppletOrderStore
    {
        private static readonly string OrderFilePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Starboard",
                    "AppletOrder.json");

        public static List<string>? LoadOrder()
        {
            try
            {
                if (!File.Exists(OrderFilePath))
                    return null;

                var json = File.ReadAllText(OrderFilePath);
                var ids = JsonConvert.DeserializeObject<List<string>>(json);
                return ids ?? new List<string>();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[AppletOrderStore] LoadOrder failed: {ex.Message}");
                return null;
            }
        }

        public static void SaveOrder(IEnumerable<IStarboardApplet> applets)
        {
            try
            {
                var ids = applets
                    .Select(a => a.Id)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToList();

                var dir = Path.GetDirectoryName(OrderFilePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonConvert.SerializeObject(ids, Formatting.Indented);
                File.WriteAllText(OrderFilePath, json);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[AppletOrderStore] SaveOrder failed: {ex.Message}");
            }
        }

        public static void ApplySavedOrder(List<IStarboardApplet> applets)
        {
            var order = LoadOrder();
            if (order == null || order.Count == 0)
                return;

            // Map id → applet
            var lookup = applets
                .GroupBy(a => a.Id)
                .ToDictionary(g => g.Key, g => g.First());

            var ordered = new List<IStarboardApplet>();

            // First: all applets in the saved order (that still exist)
            foreach (var id in order)
            {
                if (lookup.TryGetValue(id, out var app))
                {
                    ordered.Add(app);
                    lookup.Remove(id);
                }
            }

            // Then: any new/unknown applets, sorted by DisplayName
            var remaining = lookup.Values
                .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase);

            ordered.AddRange(remaining);

            applets.Clear();
            applets.AddRange(ordered);
        }
    }
}
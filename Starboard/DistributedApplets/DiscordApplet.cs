using System.Numerics;
using ImGuiNET;
using Overlay_Renderer.Methods;

namespace Starboard.DistributedApplets
{
    internal sealed class DiscordApplet : IStarboardApplet
    {
        public string Id => "starboard.discord";
        public string DisplayName => "Discord";
        public bool UsesWebView => true;

        public string _url = "https://discord.com/";
        private string _status = "Idle";

        public string? FaviconUrl => _url + "/favicon.ico";

        public void Initialize()
        {
        }

        public void Draw(float dt, Vector2 availableSize)
        {
            Vector2 viewportSize = ImGui.GetContentRegionAvail();
            WebBrowserManager.DrawWebPage(Id, _url, viewportSize);
        }
    }
}

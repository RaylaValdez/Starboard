using ImGuiNET;
using Overlay_Renderer.Methods;
using System.Numerics;

namespace Starboard.DistributedApplets
{
    internal sealed class SCMarketApplet : IStarboardApplet
    {
        public string Id => "starboard.scmarket";
        public string DisplayName => "SCMarket";
        public bool UsesWebView => true;

        public string _url = "https://sc-market.space/";
        private string _status = "Idle";
        public string Status => _status;

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

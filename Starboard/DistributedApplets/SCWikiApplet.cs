using System.Numerics;
using ImGuiNET;
using Overlay_Renderer.Methods;

namespace Starboard.DistributedApplets
{
    internal sealed class SCWikiApplet : IStarboardApplet
    {
        public string Id => "starboard.starcitizen.wiki";
        public string DisplayName => "Star Citizen Wiki";
        public bool UsesWebView => true;

        public string _url = "https://starcitizen.tools/";
        private string _status = "Idle";

        public string? FaviconUrl => "https://starcitizen.tools/resources/assets/sitelogo.svg";

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

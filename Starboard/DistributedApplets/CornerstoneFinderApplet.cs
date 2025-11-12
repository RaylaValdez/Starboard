using System.Numerics;
using ImGuiNET;
using Overlay_Renderer.Methods;

namespace Starboard.DistributedApplets
{
    internal sealed class CornerstoneFinderApplet : IStarboardApplet
    {
        public string Id => "starboard.cstone.finder";
        public string DisplayName => "Cornerstone Finder";
        public bool UsesWebView => true;

        public string _url = "https://finder.cstone.space/";
        private string _status = "Idle";

        public string? FaviconUrl => _url + "/ico.png";

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

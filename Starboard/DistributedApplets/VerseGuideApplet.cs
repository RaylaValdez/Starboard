using System.Numerics;
using ImGuiNET;
using Overlay_Renderer.Methods;

namespace Starboard.DistributedApplets
{
    internal sealed class VerseGuideApplet : IStarboardApplet
    {
        public string Id => "starboard.verseguide";
        public string DisplayName => "Verseguide";
        public bool UsesWebView => true;

        public string _url = "https://verseguide.com/";
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

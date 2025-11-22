using System.Numerics;
using ImGuiNET;
using Overlay_Renderer.Methods;

namespace Starboard.DistributedApplets
{
    internal sealed class DPSCalculatorApplet : IStarboardApplet
    {
        public string Id => "starboard.dpscalculator";
        public string DisplayName => "DPSCalculator";
        public bool UsesWebView => true;

        public string _url = "https://www.erkul.games/live/calculator";
        private string _status = "Idle";
        public string Status => _status;

        public string? FaviconUrl => "https://www.erkul.games/favicon.ico";

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

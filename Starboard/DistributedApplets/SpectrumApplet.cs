using ImGuiNET;
using Overlay_Renderer.Methods;
using System.Numerics;

namespace Starboard.DistributedApplets
{
    internal sealed class SpectrumApplet : IStarboardApplet
    {
        public string Id => "starboard.rsi.spectrum";
        public string DisplayName => "Spectrum";
        public bool UsesWebView => true;

        public string _url = "https://robertsspaceindustries.com/spectrum/community/SC";
        private string _status = "Idle";
        public string Status => _status;

        public string? FaviconUrl => "https://cdn.robertsspaceindustries.com/static/spectrum/images/android.png";

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

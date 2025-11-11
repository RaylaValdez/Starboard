using ImGuiNET;
using Starboard.DistributedApplets;
using System.Numerics;

namespace Starboard.ExampleApplets
{
    /// <summary>
    /// Simple example applet to ship with Starboard.
    /// </summary>
    internal sealed class ExampleApplet : IStarboardApplet
    {
        public string Id => "starboard.example";
        public string DisplayName => "Example Applet";
        public bool UsesWebView => false;

        //string IStarboardApplet.FaviconUrl => "";

        public void Initialize()
        {
            // One-time setup here if needed
        }

        public void Draw(float dt, Vector2 availableSize)
        {
            // We are already inside the right-hand child.
            // Just use ImGui as normal.
            ImGui.Text("Hello from Example Applet!");
            ImGui.Separator();
            ImGui.Text($"Delta time: {dt:F3} s");
            ImGui.Text($"Available size: {availableSize.X:F0} x {availableSize.Y:F0}");
        }
    }
}

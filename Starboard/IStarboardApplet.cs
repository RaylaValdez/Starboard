using System.Numerics;

namespace Starboard.DistributedApplets
{
    /// <summary>
    /// Contract for a Starboard applet.
    /// Each applet should live in its own .cs file and implement this.
    /// </summary>
    internal interface IStarboardApplet
    {
        /// <summary>A stable ID (e.g. "com.yourname.cargo_planner").</summary>
        string Id { get; }

        /// <summary>Name shown in the left applet list.</summary>
        string DisplayName { get; }

        string FaviconUrl { get; }

        bool UsesWebView { get; }

        /// <summary>Called once when app is starting / applets are registered.</summary>
        void Initialize();

        /// <summary>
        /// Draw the applet inside the right-hand panel.
        /// availableSize is the inner size of that panel.
        /// </summary>
        void Draw(float dt, Vector2 availableSize);
    }
}

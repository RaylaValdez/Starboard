// Starboard project
using Overlay_Renderer.Methods;

namespace Starboard
{
    internal sealed class ControllerBinding
    {
        public Guid DeviceInstanceGuid { get; set; }
        public int ButtonIndex { get; set; }
        public string DeviceName { get; set; } = "";

        public override string ToString()
        {
            if (DeviceInstanceGuid == Guid.Empty)
                return "Not set";

            return $"{DeviceName} [Button {ButtonIndex + 1}]";
        }

        // Helper to copy from ControllerButton
        public void SetFrom(ControllerButton src)
        {
            DeviceInstanceGuid = src.DeviceInstanceGuid;
            ButtonIndex = src.ButtonIndex;
            DeviceName = src.DeviceName;
        }
    }
}

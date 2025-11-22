using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;
using Overlay_Renderer.Helpers;

namespace Starboard.Lua
{
    internal static class LuaEngine
    {
        static LuaEngine()
        {
            UserData.RegistrationPolicy = InteropRegistrationPolicy.Default;
        }

        public static Script CreateScript()
        {
            var script = new Script(CoreModules.Preset_SoftSandbox);

            script.Options.DebugPrint = s => Logger.Info($"[Lua] {s}");

            return script;
        }
    }
}

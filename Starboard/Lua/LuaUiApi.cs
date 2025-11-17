using System;
using ImGuiNET;
using MoonSharp.Interpreter;

namespace Starboard.Lua
{
    /// <summary>
    /// Static UI API exposed to Lua scripts as `ui`.
    ///
    /// In C# we do:
    ///     UserData.RegisterType<LuaUiApi>();
    ///     script.Globals["ui"] = UserData.CreateStatic<LuaUiApi>();
    ///
    /// In Lua you then call:
    ///     ui.text("Hello")
    ///     local r = ui.slider_float("Speed", speed, 0, 100)
    ///     if r.changed then speed = r.value end
    /// </summary>
    [MoonSharpUserData]
    internal class LuaUiApi
    {
        // -----------------------
        // Basic text / layout
        // -----------------------

        /// <summary>ui.text(str)</summary>
        public static void text(string? str)
        {
            ImGui.TextUnformatted(str ?? string.Empty);
        }

        /// <summary>ui.text_wrapped(str)</summary>
        public static void text_wrapped(string? str)
        {
            ImGui.TextWrapped(str ?? string.Empty);
        }

        /// <summary>ui.separator()</summary>
        public static void separator()
        {
            ImGui.Separator();
        }

        /// <summary>ui.same_line()</summary>
        public static void same_line()
        {
            ImGui.SameLine();
        }

        /// <summary>ui.spacing()</summary>
        public static void spacing()
        {
            ImGui.Spacing();
        }

        /// <summary>ui.indent(amount)</summary>
        public static void indent(double amount)
        {
            ImGui.Indent((float)amount);
        }

        /// <summary>ui.unindent(amount)</summary>
        public static void unindent(double amount)
        {
            ImGui.Unindent((float)amount);
        }

        // -----------------------
        // Buttons
        // -----------------------

        /// <summary>
        /// ui.button(label) -> bool
        /// returns true when pressed.
        /// </summary>
        public static bool button(string? label)
        {
            return ImGui.Button(label ?? string.Empty);
        }

        // -----------------------
        // Checkbox
        // -----------------------
        // Lua:
        //   local r = ui.checkbox("Enabled", state.enabled)
        //   if r.changed then state.enabled = r.value end
        // -----------------------

        public static DynValue checkbox(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String
                ? args[0].String
                : "##checkbox";

            bool value = args.Count > 1 && args[1].Type == DataType.Boolean
                ? args[1].Boolean
                : false;

            bool v = value;
            bool changed = ImGui.Checkbox(label, ref v);

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("changed", DynValue.NewBoolean(changed));
            tbl.Table.Set("value", DynValue.NewBoolean(v));
            return tbl;
        }

        // -----------------------
        // Slider (float)
        // -----------------------
        // Lua:
        //   local r = ui.slider_float("Speed", state.speed, 0, 100)
        //   if r.changed then state.speed = r.value end
        // -----------------------

        public static DynValue slider_float(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String
                ? args[0].String
                : "##slider";

            float value = args.Count > 1 && args[1].Type == DataType.Number
                ? (float)args[1].Number
                : 0f;

            float min = args.Count > 2 && args[2].Type == DataType.Number
                ? (float)args[2].Number
                : 0f;

            float max = args.Count > 3 && args[3].Type == DataType.Number
                ? (float)args[3].Number
                : 1f;

            string format = "%.3f";
            if (args.Count > 4 && args[4].Type == DataType.String)
                format = args[4].String;

            float v = value;
            bool changed = ImGui.SliderFloat(label, ref v, min, max, format);

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("changed", DynValue.NewBoolean(changed));
            tbl.Table.Set("value", DynValue.NewNumber(v));
            return tbl;
        }

        // -----------------------
        // Slider (int)
        // -----------------------
        // Lua:
        //   local r = ui.slider_int("Count", state.count, 0, 10)
        //   if r.changed then state.count = r.value end
        // -----------------------

        public static DynValue slider_int(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String
                ? args[0].String
                : "##slider_int";

            int value = args.Count > 1 && args[1].Type == DataType.Number
                ? (int)args[1].Number
                : 0;

            int min = args.Count > 2 && args[2].Type == DataType.Number
                ? (int)args[2].Number
                : 0;

            int max = args.Count > 3 && args[3].Type == DataType.Number
                ? (int)args[3].Number
                : 100;

            int v = value;
            bool changed = ImGui.SliderInt(label, ref v, min, max);

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("changed", DynValue.NewBoolean(changed));
            tbl.Table.Set("value", DynValue.NewNumber(v));
            return tbl;
        }

        // -----------------------
        // DragFloat
        // -----------------------
        // Lua:
        //   local r = ui.drag_float("X", state.x, 0.1, -10, 10)
        //   if r.changed then state.x = r.value end
        // -----------------------

        public static DynValue drag_float(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String
                ? args[0].String
                : "##drag_float";

            float value = args.Count > 1 && args[1].Type == DataType.Number
                ? (float)args[1].Number
                : 0f;

            float speed = args.Count > 2 && args[2].Type == DataType.Number
                ? (float)args[2].Number
                : 1.0f;

            float min = args.Count > 3 && args[3].Type == DataType.Number
                ? (float)args[3].Number
                : 0f;

            float max = args.Count > 4 && args[4].Type == DataType.Number
                ? (float)args[4].Number
                : 0f; // 0,0 means "no clamp" in ImGui

            string format = "%.3f";
            if (args.Count > 5 && args[5].Type == DataType.String)
                format = args[5].String;

            float v = value;
            bool changed = ImGui.DragFloat(label, ref v, speed, min, max, format);

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("changed", DynValue.NewBoolean(changed));
            tbl.Table.Set("value", DynValue.NewNumber(v));
            return tbl;
        }

        // -----------------------
        // DragInt
        // -----------------------
        // Lua:
        //   local r = ui.drag_int("Steps", state.steps, 1, 0, 100)
        //   if r.changed then state.steps = r.value end
        // -----------------------

        public static DynValue drag_int(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String
                ? args[0].String
                : "##drag_int";

            int value = args.Count > 1 && args[1].Type == DataType.Number
                ? (int)args[1].Number
                : 0;

            float speed = args.Count > 2 && args[2].Type == DataType.Number
                ? (float)args[2].Number
                : 1.0f;

            int min = args.Count > 3 && args[3].Type == DataType.Number
                ? (int)args[3].Number
                : 0;

            int max = args.Count > 4 && args[4].Type == DataType.Number
                ? (int)args[4].Number
                : 0; // 0,0 = no clamp

            int v = value;
            bool changed = ImGui.DragInt(label, ref v, speed, min, max);

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("changed", DynValue.NewBoolean(changed));
            tbl.Table.Set("value", DynValue.NewNumber(v));
            return tbl;
        }

        // -----------------------
        // Help marker (tooltip)
        // -----------------------
        // Lua:
        //   ui.same_line()
        //   ui.help_marker("This does X, Y, Z")
        // -----------------------

        public static void help_marker(string? text)
        {
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
                ImGui.TextUnformatted(text ?? string.Empty);
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
        }
    }
}

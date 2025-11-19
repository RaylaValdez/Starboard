using ImGuiNET;
using MoonSharp.Interpreter;
using Overlay_Renderer.Methods;
using System;
using System.Numerics;

namespace Starboard.Lua
{
    /// <summary>
    /// UI API exposed to Lua scripts as `ui`.
    /// </summary>
    [MoonSharpUserData]
    internal class LuaUiApi
    {
        #region Basic Text / Layout
        // =====================================================================
        // BASIC TEXT / LAYOUT
        // =====================================================================

        /// <summary>ui.text(str) – Draw a single line of text without wrapping.</summary>
        public void text(string? str)
        {
            ImGui.TextUnformatted(str ?? string.Empty);
        }

        /// <summary>ui.text_wrapped(str) – Draw text that automatically wraps at the window edge.</summary>
        public void text_wrapped(string? str)
        {
            ImGui.TextWrapped(str ?? string.Empty);
        }

        /// <summary>ui.bullet_text(str) – Draw a bullet followed by text (useful for lists).</summary>
        public void bullet_text(string? str)
        {
            ImGui.BulletText(str ?? string.Empty);
        }

        /// <summary>ui.separator() – Draw a horizontal separator line.</summary>
        public void separator()
        {
            ImGui.Separator();
        }

        /// <summary>ui.same_line() – Place the next item on the same line.</summary>
        public void same_line()
        {
            ImGui.SameLine();
        }

        /// <summary>ui.spacing() – Add a vertical gap (empty space) between items.</summary>
        public void spacing()
        {
            ImGui.Spacing();
        }

        /// <summary>ui.new_line() – Force a new line (similar to pressing Enter).</summary>
        public void new_line()
        {
            ImGui.NewLine();
        }

        /// <summary>ui.bullet() – Draw a bullet on the current line without text.</summary>
        public void bullet()
        {
            ImGui.Bullet();
        }

        /// <summary>ui.indent(amount) – Indent subsequent items to the right by `amount` pixels.</summary>
        public void indent(double amount)
        {
            ImGui.Indent((float)amount);
        }

        /// <summary>ui.unindent(amount) – Remove indentation to the left by `amount` pixels.</summary>
        public void unindent(double amount)
        {
            ImGui.Unindent((float)amount);
        }

        /// <summary>ui.help_marker(text) – Draw a small "(?)" that shows a wrapped tooltip when hovered.</summary>
        public void help_marker(string? text)
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
        #endregion

        #region Windows, Children, Groups and Positioning
        // =====================================================================
        // WINDOWS, CHILDREN, GROUPS & POSITIONING
        // =====================================================================

        /// <summary>ui.begin_window(title) – Begin a window; returns true if the window is visible (not collapsed).</summary>
        public bool begin_window(string? title)
        {
            return ImGui.Begin(title ?? string.Empty);
        }

        /// <summary>ui.end_window() – End the current window started by ui.begin_window().</summary>
        public void end_window()
        {
            ImGui.End();
        }

        /// <summary>ui.begin_child(id, width, height, border) – Begin a child region with size and optional border; returns true if visible.</summary>
        public bool begin_child(string? id, double width, double height, bool border)
        {
            var size = new Vector2((float)width, (float)height);
            var flags = ImGuiChildFlags.None;

            if (border)
                flags |= ImGuiChildFlags.Borders;

            return ImGui.BeginChild(id ?? "##child", size, flags);
        }

        /// <summary>ui.end_child() – End the current child region started by ui.begin_child().</summary>
        public void end_child()
        {
            ImGui.EndChild();
        }

        /// <summary>ui.begin_group() – Begin a group so widgets are visually and logically grouped.</summary>
        public void begin_group()
        {
            ImGui.BeginGroup();
        }

        /// <summary>ui.end_group() – End the current group started by ui.begin_group().</summary>
        public void end_group()
        {
            ImGui.EndGroup();
        }

        /// <summary>ui.set_next_window_pos(x, y) – Set the position for the next window created by ui.begin_window().</summary>
        public void set_next_window_pos(double x, double y)
        {
            ImGui.SetNextWindowPos(new Vector2((float)x, (float)y));
        }

        /// <summary>ui.set_next_window_size(w, h) – Set the size for the next window created by ui.begin_window().</summary>
        public void set_next_window_size(double w, double h)
        {
            ImGui.SetNextWindowSize(new Vector2((float)w, (float)h));
        }

        /// <summary>ui.set_cursor_pos(x, y) – Move the cursor within the current window (top-left is 0,0).</summary>
        public void set_cursor_pos(double x, double y)
        {
            ImGui.SetCursorPos(new Vector2((float)x, (float)y));
        }
        #endregion

        #region Buttons
        // =====================================================================
        // BUTTONS
        // =====================================================================

        /// <summary>ui.button(label) – Draw a standard button; returns true when pressed.</summary>
        public bool button(string? label)
        {
            return ImGui.Button(label ?? string.Empty);
        }

        /// <summary>ui.small_button(label) – Draw a smaller button; returns true when pressed.</summary>
        public bool small_button(string? label)
        {
            return ImGui.SmallButton(label ?? string.Empty);
        }
        #endregion

        #region Checkboxes & basic toggle widgets
        // =====================================================================
        // CHECKBOXES & BASIC TOGGLE WIDGETS
        // =====================================================================

        /// <summary>ui.checkbox(label, value_bool) -> { changed, value } – Checkbox that returns a table with fields `changed` and `value`.</summary>
        public DynValue checkbox(ScriptExecutionContext ctx, CallbackArguments args)
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

        /// <summary>ui.radio_button(label, active) -> bool – Radio-style toggle; returns true when clicked.</summary>
        public bool radio_button(string? label, bool active)
        {
            return ImGui.RadioButton(label ?? string.Empty, active);
        }

        /// <summary>ui.radio_button_value(label, current, this_value) -> { changed, value } – Radio group helper returning new value and changed flag.</summary>
        public DynValue radio_button_value(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String
                ? args[0].String
                : "##radio";

            int current = args.Count > 1 && args[1].Type == DataType.Number
                ? (int)args[1].Number
                : 0;

            int thisValue = args.Count > 2 && args[2].Type == DataType.Number
                ? (int)args[2].Number
                : 0;

            bool clicked = ImGui.RadioButton(label, current == thisValue);
            if (clicked)
                current = thisValue;

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("changed", DynValue.NewBoolean(clicked));
            tbl.Table.Set("value", DynValue.NewNumber(current));
            return tbl;
        }
        #endregion

        #region Sliders
        // =====================================================================
        // SLIDERS
        // =====================================================================

        /// <summary>ui.slider_float(label, value, min, max, format?) -> { changed, value } – Float slider returning new value and changed flag.</summary>
        public DynValue slider_float(ScriptExecutionContext ctx, CallbackArguments args)
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

        /// <summary>ui.slider_int(label, value, min, max) -> { changed, value } – Integer slider returning new value and changed flag.</summary>
        public DynValue slider_int(ScriptExecutionContext ctx, CallbackArguments args)
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
        #endregion

        #region Drag Float / Int
        // =====================================================================
        // DRAG FLOAT / INT
        // =====================================================================

        /// <summary>ui.drag_float(label, value, speed, min, max, format?) -> { changed, value } – Drag float widget with optional limits and format.</summary>
        public DynValue drag_float(ScriptExecutionContext ctx, CallbackArguments args)
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

        /// <summary>ui.drag_int(label, value, speed, min, max) -> { changed, value } – Drag integer widget with optional limits.</summary>
        public DynValue drag_int(ScriptExecutionContext ctx, CallbackArguments args)
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
        #endregion

        #region Input Text
        // =====================================================================
        // INPUT TEXT
        // =====================================================================

        /// <summary>ui.input_text(label, text, max_length) -> { changed, value } – Text input with max length, returning new value and changed flag.</summary>
        public DynValue input_text(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String
                ? args[0].String
                : "##input_text";

            string text = args.Count > 1 && args[1].Type == DataType.String
                ? args[1].String
                : string.Empty;

            int maxLength = args.Count > 2 && args[2].Type == DataType.Number
                ? (int)args[2].Number
                : 64;

            if (maxLength < 1)
                maxLength = 1;

            uint len = (uint)maxLength;
            if (len > int.MaxValue)
                len = int.MaxValue;

            string buffer = text;
            bool changed = ImGui.InputText(label, ref buffer, len);

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("changed", DynValue.NewBoolean(changed));
            tbl.Table.Set("value", DynValue.NewString(buffer));
            return tbl;
        }
        #endregion

        #region Combo / Selectable
        // =====================================================================
        // COMBO / SELECTABLE
        // =====================================================================

        /// <summary>ui.combo(label, current_index, items_table) -> { changed, index } – Combo box from a Lua string array, returning selected index.</summary>
        public DynValue combo(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String
                ? args[0].String
                : "##combo";

            int current = args.Count > 1 && args[1].Type == DataType.Number
                ? (int)args[1].Number
                : 0;

            string[] items = Array.Empty<string>();
            if (args.Count > 2 && args[2].Type == DataType.Table)
            {
                var t = args[2].Table;
                items = new string[t.Length];
                int i = 0;
                foreach (var v in t.Values)
                {
                    items[i++] = v.Type == DataType.String ? v.String : string.Empty;
                }
            }

            bool changed = false;
            if (items.Length > 0)
            {
                changed = ImGui.Combo(label, ref current, items, items.Length);
            }

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("changed", DynValue.NewBoolean(changed));
            tbl.Table.Set("index", DynValue.NewNumber(current));
            return tbl;
        }

        /// <summary>ui.selectable(label, selected) -> { clicked, selected } – Selectable item that returns click state and resulting selection.</summary>
        public DynValue selectable(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String
                ? args[0].String
                : "##selectable";

            bool selected = args.Count > 1 && args[1].Type == DataType.Boolean
                ? args[1].Boolean
                : false;

            bool clicked = ImGui.Selectable(label, selected);
            bool newSelected = selected || clicked;

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("clicked", DynValue.NewBoolean(clicked));
            tbl.Table.Set("selected", DynValue.NewBoolean(newSelected));
            return tbl;
        }
        #endregion

        #region Tree / Collapsing Headers
        // =====================================================================
        // TREE / COLLAPSING HEADERS
        // =====================================================================

        /// <summary>ui.collapsing_header(label) – Collapsible header; returns true while the section is open.</summary>
        public bool collapsing_header(string? label)
        {
            return ImGui.CollapsingHeader(label ?? string.Empty);
        }

        /// <summary>ui.tree_node(label) – Tree node; returns true if open (then call ui.tree_pop()).</summary>
        public bool tree_node(string? label)
        {
            return ImGui.TreeNode(label ?? string.Empty);
        }

        /// <summary>ui.tree_pop() – Close the most recent open tree node.</summary>
        public void tree_pop()
        {
            ImGui.TreePop();
        }
        #endregion

        #region Tab Bars
        // =====================================================================
        // TAB BARS
        // =====================================================================

        /// <summary>ui.begin_tab_bar(id) – Begin a tab bar; returns true while visible.</summary>
        public bool begin_tab_bar(string? id)
        {
            return ImGui.BeginTabBar(id ?? "##tabs");
        }

        /// <summary>ui.end_tab_bar() – End the current tab bar.</summary>
        public void end_tab_bar()
        {
            ImGui.EndTabBar();
        }

        /// <summary>ui.begin_tab_item(label) – Begin a tab item; returns true if its contents should be drawn.</summary>
        public bool begin_tab_item(string? label)
        {
            return ImGui.BeginTabItem(label ?? string.Empty);
        }

        /// <summary>ui.end_tab_item() – End the current tab item.</summary>
        public void end_tab_item()
        {
            ImGui.EndTabItem();
        }
        #endregion

        #region Popups
        // =====================================================================
        // POPUPS
        // =====================================================================

        /// <summary>ui.open_popup(id) – Request opening a popup with the given id.</summary>
        public void open_popup(string? id)
        {
            ImGui.OpenPopup(id ?? "##popup");
        }

        /// <summary>ui.begin_popup(id) – Begin a popup; returns true while open (must be ended with ui.end_popup()).</summary>
        public bool begin_popup(string? id)
        {
            return ImGui.BeginPopup(id ?? "##popup");
        }

        /// <summary>ui.end_popup() – End the current popup.</summary>
        public void end_popup()
        {
            ImGui.EndPopup();
        }
        #endregion

        #region Progress / Image
        // =====================================================================
        // PROGRESS / IMAGE
        // =====================================================================

        /// <summary>ui.progress_bar(fraction, width, height, overlay_text?) – Draw a progress bar with value in [0,1].</summary>
        public void progress_bar(double fraction, double width, double height, string? overlay = null)
        {
            float f = (float)fraction;
            if (f < 0f) f = 0f;
            if (f > 1f) f = 1f;

            ImGui.ProgressBar(
                f,
                new Vector2((float)width, (float)height),
                overlay
            );
        }

        /// <summary>ui.image(texture_id, width, height) – Draw an image using a host-provided texture id.</summary>
        public void image(double textureId, double width, double height)
        {
            IntPtr id = new IntPtr(unchecked((long)textureId));
            ImGui.Image(id, new Vector2((float)width, (float)height));
        }
        #endregion

        #region Style Helpers
        // =====================================================================
        // STYLE HELPERS
        // =====================================================================

        /// <summary>ui.push_style_color(idx, r, g, b, a) – Push a style color (ImGuiCol) onto the stack.</summary>
        public void push_style_color(int idx, double r, double g, double b, double a = 1.0)
        {
            ImGui.PushStyleColor(
                (ImGuiCol)idx,
                new Vector4((float)r, (float)g, (float)b, (float)a)
            );
        }

        /// <summary>ui.pop_style_color(count) – Pop one or more style colors from the stack.</summary>
        public void pop_style_color(int count = 1)
        {
            ImGui.PopStyleColor(count);
        }

        /// <summary>ui.begin_disabled(disabled) – Begin a disabled block where widgets are greyed and non-interactive.</summary>
        public void begin_disabled(bool disabled = true)
        {
            ImGui.BeginDisabled(disabled);
        }

        /// <summary>ui.end_disabled() – End a disabled block started with ui.begin_disabled().</summary>
        public void end_disabled()
        {
            ImGui.EndDisabled();
        }
        #endregion

        #region HitTestRegion Update
        /// <summary>ui.add_hitregions() – Add the current ImGui window’s hit regions for this applet.</summary>
        public void add_hitregions()
        {
            HitTestRegions.AddCurrentWindow();
        }
        #endregion
    }
}

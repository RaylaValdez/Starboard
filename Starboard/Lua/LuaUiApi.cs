using System;
using System.Numerics;
using System.Text;
using ImGuiNET;
using MoonSharp.Interpreter;
using Overlay_Renderer.Methods;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.VoiceCommands;


namespace Starboard.Lua
{
    /// <summary> UI API exposed to Lua scripts as `ui`.</summary>
    [MoonSharpUserData]
    internal class LuaUiApi
    {

        /// <summary>ui.add_hitregions() – Add the current ImGui window’s hit regions for this applet.</summary>
        public static void add_hitregions() => HitTestRegions.AddCurrentWindow();

        /// <summary>ui.accept_dragdrop_payload(context, args) - Accepts an active drag-and-drop payload for the given type and returns its data, size, and delivery status to Lua.</summary>
        public static DynValue accept_dragdrop_payload(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string type = (args.Count > 0 && args[0].Type == DataType.String) ? args[0].String : "ITEM";
            ImGuiDragDropFlags flags = ImGuiDragDropFlags.None;
            if (args.Count > 1 && args[1].Type == DataType.Number)
                flags = (ImGuiDragDropFlags)(int)args[1].Number;

            var payload = ImGui.AcceptDragDropPayload(type, flags);

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);

            bool accepted = payload.Data != IntPtr.Zero;
            tbl.Table.Set("accepted", DynValue.NewBoolean(accepted));

            if (!accepted)
                return tbl;

            bool delivery = payload.IsDelivery();
            int size = payload.DataSize;

            tbl.Table.Set("delivery", DynValue.NewBoolean(delivery));
            tbl.Table.Set("size", DynValue.NewNumber(size));

            if (size > 0 && payload.Data != IntPtr.Zero)
            {
                byte[] bytes = new byte[size];
                Marshal.Copy(payload.Data, bytes, 0, size);

                tbl.Table.Set("base64", DynValue.NewString(Convert.ToBase64String(bytes)));

                try
                {
                    string s = Encoding.UTF8.GetString(bytes);
                    if (Encoding.UTF8.GetByteCount(s) == bytes.Length)
                        tbl.Table.Set("text", DynValue.NewString(s));
                }
                catch { }
            }
            return tbl;
        }

        /// <summary>ui.align_text_to_frame_padding() – Align text baseline to match frame padding (use before text next to widgets).</summary>
        public static void align_text_to_frame_padding()
        {
            ImGui.AlignTextToFramePadding();
        }

        /// <summary>ui.arrow_button(id, direction) – Draw an arrow button pointing in a given direction; returns true when clicked.</summary>
        public static bool arrow_button(string? id, string direction)
        {
            ImGuiDir dir = direction?.ToLowerInvariant() switch
            {
                "left" => ImGuiDir.Left,
                "right" => ImGuiDir.Right,
                "up" => ImGuiDir.Up,
                "down" => ImGuiDir.Down,
                _ => ImGuiDir.None
            };

            return ImGui.ArrowButton(id ?? "##arrow", dir);
        }

        /// <summary>ui.begin_child(id, width, height, border) – Begin a child region with size and optional border; returns true if visible.</summary>
        public static bool begin_child(string? id, double width, double height, bool border)
        {
            var size = new Vector2((float)width, (float)height);
            var flags = ImGuiChildFlags.None;

            if (border)
                flags |= ImGuiChildFlags.Borders;

            return ImGui.BeginChild(id ?? "##child", size, flags);
        }

        /// <summary>ui.begin_combo(label, preview_value) – Begin a combo box; returns true if it’s open (must be followed by ui.end_combo()).</summary>
        public static bool begin_combo(string? label, string? previewValue)
        {
            return ImGui.BeginCombo(label ?? "##combo", previewValue ?? string.Empty);
        }

        /// <summary>ui.begin_disabled(disabled) – Begin a disabled block where widgets are greyed and non-interactive.</summary>
        public static void begin_disabled(bool disabled = true)
        {
            ImGui.BeginDisabled(disabled);
        }

        /// <summary>ui.begin_drag_drop_source(flags?) – Begin a drag-drop source; returns true while active (must be followed by ui.end_drag_drop_source()).</summary>
        public static bool begin_drag_drop_source(ScriptExecutionContext ctx, CallbackArguments args)
        {
            ImGuiDragDropFlags flags = ImGuiDragDropFlags.None;

            if (args.Count > 0 && args[0].Type == DataType.Number)
                flags = (ImGuiDragDropFlags)(int)args[0].Number;

            return ImGui.BeginDragDropSource(flags);
        }

        /// <summary>ui.begin_drag_drop_target() – Begin a drag-drop target region; returns true if a payload can be accepted (must be followed by ui.end_drag_drop_target()).</summary>
        public static bool begin_drag_drop_target()
        {
            return ImGui.BeginDragDropTarget();
        }

        /// <summary>ui.begin_group() – Begin a group so widgets are visually and logically grouped.</summary>
        public static void begin_group()
        {
            ImGui.BeginGroup();
        }

        /// <summary>ui.begin_item_tooltip() – Begin a tooltip for the last item; returns true if drawing the tooltip (must be followed by ui.end_tooltip()).</summary>
        public static bool begin_item_tooltip()
        {
            return ImGui.BeginItemTooltip();
        }

        /// <summary>ui.begin_list_box(label, width?, height?) – Begin a scrollable list box; returns true while active (must be followed by ui.end_list_box()).</summary>
        public static bool begin_list_box(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String
                ? args[0].String
                : "##listbox";

            float width = args.Count > 1 && args[1].Type == DataType.Number
                ? (float)args[1].Number
                : 0f;

            float height = args.Count > 2 && args[2].Type == DataType.Number
                ? (float)args[2].Number
                : 0f;

            Vector2 size = new(width, height);
            return ImGui.BeginListBox(label, size);
        }

        /// <summary>ui.begin_main_menu_bar() – Begin the main menu bar at the top of the window; returns true while active (must be followed by ui.end_main_menu_bar()).</summary>
        public static bool begin_main_menu_bar()
        {
            return ImGui.BeginMainMenuBar();
        }

        /// <summary>ui.begin_menu(label, enabled?) – Begin a menu or submenu; returns true while open (must be followed by ui.end_menu()).</summary>
        public static bool begin_menu(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String
                ? args[0].String
                : "##menu";

            bool enabled = args.Count > 1 && args[1].Type == DataType.Boolean
                ? args[1].Boolean
                : true;

            return ImGui.BeginMenu(label, enabled);
        }

        /// <summary>ui.begin_menu_bar() – Begin a menu bar inside the current window; returns true while active (must be followed by ui.end_menu_bar()).</summary>
        public static bool begin_menu_bar()
        {
            return ImGui.BeginMenuBar();
        }

        /// <summary>ui.begin_multi_select(flags?, selection_count, items_count) – Begin a multi-selection block; returns a table with IO + requests. Must pair with ui.end_multi_select().</summary>
        public static DynValue begin_multi_select(ScriptExecutionContext ctx, CallbackArguments args)
        {
            ImGuiMultiSelectFlags flags = ImGuiMultiSelectFlags.None;
            if (args.Count > 0 && args[0].Type == DataType.Number)
                flags = (ImGuiMultiSelectFlags)(int)args[0].Number;

            int selectionCount = (args.Count > 1 && args[1].Type == DataType.Number) ? (int)args[1].Number : 0;
            int itemsCount = (args.Count > 2 && args[2].Type == DataType.Number) ? (int)args[2].Number : 0;

            var io = ImGui.BeginMultiSelect(flags, selectionCount, itemsCount);

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);

            // Scalars / simple fields
            tbl.Table.Set("itemsCount", DynValue.NewNumber(io.ItemsCount));
            tbl.Table.Set("navIdSelected", DynValue.NewBoolean(io.NavIdSelected));
            tbl.Table.Set("rangeSrcReset", DynValue.NewBoolean(io.RangeSrcReset));
            tbl.Table.Set("rangeSrcItem", DynValue.NewNumber((long)io.RangeSrcItem));
            tbl.Table.Set("navIdItem", DynValue.NewNumber((long)io.NavIdItem));

            // Requests vector -> Lua array of tables
            var reqArr = DynValue.NewTable(script);

            int n = io.Requests.Size;
            for (int i = 0; i < n; i++)
            {
                var r = io.Requests[i]; // or io.Requests.Get(i) depending on generator
                var rt = DynValue.NewTable(script);
                rt.Table.Set("type", DynValue.NewNumber((int)r.Type));
                rt.Table.Set("selected", DynValue.NewBoolean(r.Selected));
                rt.Table.Set("rangeDirection", DynValue.NewNumber(r.RangeDirection));
                rt.Table.Set("rangeFirstItem", DynValue.NewNumber((long)r.RangeFirstItem));
                rt.Table.Set("rangeLastItem", DynValue.NewNumber((long)r.RangeLastItem));
                reqArr.Table.Append(rt);
            }



            tbl.Table.Set("requests", reqArr);
            return tbl;
        }

        /// <summary>ui.begin_popup(id) – Begin a popup; returns true while open (must be ended with ui.end_popup()).</summary>
        public static bool begin_popup(string? id)
        {
            return ImGui.BeginPopup(id ?? "##popup");
        }

        /// <summary>ui.begin_popup_context_item(id?, popup_flags?) – Begin a right-click context popup for the last item; returns true while open (must be followed by ui.end_popup()).</summary>
        public static bool begin_popup_context_item(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string id = args.Count > 0 && args[0].Type == DataType.String
                ? args[0].String
                : string.Empty;

            ImGuiPopupFlags flags = ImGuiPopupFlags.MouseButtonRight;
            if (args.Count > 1 && args[1].Type == DataType.Number)
                flags = (ImGuiPopupFlags)(int)args[1].Number;

            return ImGui.BeginPopupContextItem(id, flags);
        }

        /// <summary>ui.begin_popup_context_void(id?, popup_flags?) – Begin a right-click context popup when clicking on empty space; returns true while open (must be followed by ui.end_popup()).</summary>
        public static bool begin_popup_context_void(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string id = args.Count > 0 && args[0].Type == DataType.String
                ? args[0].String
                : string.Empty;

            ImGuiPopupFlags flags = ImGuiPopupFlags.MouseButtonRight;
            if (args.Count > 1 && args[1].Type == DataType.Number)
                flags = (ImGuiPopupFlags)(int)args[1].Number;

            return ImGui.BeginPopupContextVoid(id, flags);
        }

        /// <summary>ui.begin_popup_context_window(id?, popup_flags?) – Begin a right-click context popup for the current window; returns true while open (must be followed by ui.end_popup()).</summary>
        public static bool begin_popup_context_window(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string id = args.Count > 0 && args[0].Type == DataType.String
                ? args[0].String
                : string.Empty;

            ImGuiPopupFlags flags = ImGuiPopupFlags.MouseButtonRight;
            if (args.Count > 1 && args[1].Type == DataType.Number)
                flags = (ImGuiPopupFlags)(int)args[1].Number;

            return ImGui.BeginPopupContextWindow(id, flags);
        }

        /// <summary>ui.begin_popup_modal(name, open?, flags?) – Begin a modal popup; returns true while open (must be followed by ui.end_popup()).</summary>
        public static bool begin_popup_modal(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string name = args.Count > 0 && args[0].Type == DataType.String
                ? args[0].String
                : "##popup_modal";

            bool open = args.Count > 1 && args[1].Type == DataType.Boolean
                ? args[1].Boolean
                : true;

            ImGuiWindowFlags flags = ImGuiWindowFlags.None;
            if (args.Count > 2 && args[2].Type == DataType.Number)
                flags = (ImGuiWindowFlags)(int)args[2].Number;

            return ImGui.BeginPopupModal(name, ref open, flags);
        }

        /// <summary>ui.begin_tab_bar(id) – Begin a tab bar; returns true while visible.</summary>
        public static bool begin_tab_bar(string? id)
        {
            return ImGui.BeginTabBar(id ?? "##tabs");
        }

        /// <summary>ui.begin_tab_item(label) – Begin a tab item; returns true if its contents should be drawn.</summary>
        public static bool begin_tab_item(string? label)
        {
            return ImGui.BeginTabItem(label ?? string.Empty);
        }

        /// <summary>ui.begin_table(id, columns, flags?, outer_width?, inner_width?) – Begin a table; returns true while active (must be followed by ui.end_table()).</summary>
        public static bool begin_table(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string id = args.Count > 0 && args[0].Type == DataType.String
                ? args[0].String
                : "##table";

            int columns = args.Count > 1 && args[1].Type == DataType.Number
                ? (int)args[1].Number
                : 1;

            ImGuiTableFlags flags = ImGuiTableFlags.None;
            if (args.Count > 2 && args[2].Type == DataType.Number)
                flags = (ImGuiTableFlags)(int)args[2].Number;

            float outerWidth = args.Count > 3 && args[3].Type == DataType.Number
                ? (float)args[3].Number
                : 0f;

            float innerWidth = args.Count > 4 && args[4].Type == DataType.Number
                ? (float)args[4].Number
                : 0f;

            return ImGui.BeginTable(id, columns, flags, new Vector2(outerWidth, 0f), innerWidth);
        }

        /// <summary>ui.begin_tooltip() – Begin a tooltip window; returns true while active (must be followed by ui.end_tooltip()).</summary>
        public static bool begin_tooltip()
        {
            return ImGui.BeginTooltip();
        }

        /// <summary>ui.begin_window(title) – Begin a window; returns true if the window is visible (not collapsed).</summary>
        public static bool begin_window(string? title)
        {
            return ImGui.Begin(title ?? string.Empty);
        }

        /// <summary>ui.bullet() – Draw a bullet on the current line without text.</summary>
        public static void bullet()
        {
            ImGui.Bullet();
        }

        /// <summary>ui.bullet_text(str) – Draw a bullet followed by text (useful for lists).</summary>
        public static void bullet_text(string? str)
        {
            ImGui.BulletText(str ?? string.Empty);
        }

        /// <summary>ui.button(label) – Draw a standard button; returns true when pressed.</summary>
        public static bool button(string? label)
        {
            return ImGui.Button(label ?? string.Empty);
        }

        /// <summary>ui.calc_item_width() – Return the width of the last item.</summary>
        public static double calc_item_width()
        {
            return ImGui.CalcItemWidth();
        }

        /// <summary>ui.calc_text_size(text) – Return the size of the given text.</summary>
        public static DynValue calc_text_size(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string text = args.Count > 0 && args[0].Type == DataType.String
                ? args[0].String
                : string.Empty;

            Vector2 size = ImGui.CalcTextSize(text);

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("x", DynValue.NewNumber(size.X));
            tbl.Table.Set("y", DynValue.NewNumber(size.Y));
            return tbl;
        }

        /// <summary>ui.checkbox(label, value_bool) -> { changed, value } – Checkbox that returns a table with fields `changed` and `value`.</summary>
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

        /// <summary>ui.checkbox_flags(label, flags, flag_value) -> { changed, flags } – Checkbox for a bit flag; returns updated flags and change state.</summary>
        public static DynValue checkbox_flags(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String ? args[0].String : "##checkbox_flags";
            int flags = args.Count > 1 && args[1].Type == DataType.Number ? (int)args[1].Number : 0;
            int flagValue = args.Count > 2 && args[2].Type == DataType.Number ? (int)args[2].Number : 1;

            bool v = (flags & flagValue) != 0;
            bool changed = ImGui.CheckboxFlags(label, ref flags, flagValue);

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("changed", DynValue.NewBoolean(changed));
            tbl.Table.Set("flags", DynValue.NewNumber(flags));
            return tbl;
        }

        /// <summary>ui.close_current_popup() – Close the currently open popup.</summary>
        public static void close_current_popup()
        {
            ImGui.CloseCurrentPopup();
        }

        /// <summary>ui.collapsing_header(label) – Collapsible header; returns true while the section is open.</summary>
        public static bool collapsing_header(string? label)
        {
            return ImGui.CollapsingHeader(label ?? string.Empty);
        }

        /// <summary>ui.color_button(id, r, g, b, a, flags?, size_x?, size_y?) – Display a color button; returns true when clicked.</summary>
        public static bool color_button(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string id = args.Count > 0 && args[0].Type == DataType.String ? args[0].String : "##colorbtn";
            float r = args.Count > 1 && args[1].Type == DataType.Number ? (float)args[1].Number : 1f;
            float g = args.Count > 2 && args[2].Type == DataType.Number ? (float)args[2].Number : 1f;
            float b = args.Count > 3 && args[3].Type == DataType.Number ? (float)args[3].Number : 1f;
            float a = args.Count > 4 && args[4].Type == DataType.Number ? (float)args[4].Number : 1f;
            ImGuiColorEditFlags flags = args.Count > 5 && args[5].Type == DataType.Number ? (ImGuiColorEditFlags)(int)args[5].Number : ImGuiColorEditFlags.None;
            float sx = args.Count > 6 && args[6].Type == DataType.Number ? (float)args[6].Number : 0f;
            float sy = args.Count > 7 && args[7].Type == DataType.Number ? (float)args[7].Number : 0f;

            return ImGui.ColorButton(id, new Vector4(r, g, b, a), flags, new Vector2(sx, sy));
        }

        /// <summary>ui.color_convert_float4_to_u32(r, g, b, a) – Convert RGBA floats to packed U32 color.</summary>
        public static uint color_convert_float4_to_u32(double r, double g, double b, double a)
        {
            return ImGui.ColorConvertFloat4ToU32(new Vector4((float)r, (float)g, (float)b, (float)a));
        }

        /// <summary>ui.color_convert_hsv_to_rgb(h, s, v) -> { r, g, b } – Convert HSV to RGB floats.</summary>
        public static DynValue color_convert_hsv_to_rgb(ScriptExecutionContext ctx, CallbackArguments args)
        {
            float h = args.Count > 0 && args[0].Type == DataType.Number ? (float)args[0].Number : 0f;
            float s = args.Count > 1 && args[1].Type == DataType.Number ? (float)args[1].Number : 0f;
            float v = args.Count > 2 && args[2].Type == DataType.Number ? (float)args[2].Number : 0f;

            ImGui.ColorConvertHSVtoRGB(h, s, v, out float r, out float g, out float b);
            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("r", DynValue.NewNumber(r));
            tbl.Table.Set("g", DynValue.NewNumber(g));
            tbl.Table.Set("b", DynValue.NewNumber(b));
            return tbl;
        }

        /// <summary>ui.color_convert_rgb_to_hsv(r, g, b) -> { h, s, v } – Convert RGB to HSV floats.</summary>
        public static DynValue color_convert_rgb_to_hsv(ScriptExecutionContext ctx, CallbackArguments args)
        {
            float r = args.Count > 0 && args[0].Type == DataType.Number ? (float)args[0].Number : 0f;
            float g = args.Count > 1 && args[1].Type == DataType.Number ? (float)args[1].Number : 0f;
            float b = args.Count > 2 && args[2].Type == DataType.Number ? (float)args[2].Number : 0f;

            ImGui.ColorConvertRGBtoHSV(r, g, b, out float h, out float s, out float v);
            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("h", DynValue.NewNumber(h));
            tbl.Table.Set("s", DynValue.NewNumber(s));
            tbl.Table.Set("v", DynValue.NewNumber(v));
            return tbl;
        }

        /// <summary>ui.color_convert_u32_to_float4(color_u32) -> { r, g, b, a } – Convert packed U32 color to RGBA floats.</summary>
        public static DynValue color_convert_u32_to_float4(ScriptExecutionContext ctx, CallbackArguments args)
        {
            uint col = args.Count > 0 && args[0].Type == DataType.Number ? (uint)args[0].Number : 0u;
            Vector4 c = ImGui.ColorConvertU32ToFloat4(col);

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("r", DynValue.NewNumber(c.X));
            tbl.Table.Set("g", DynValue.NewNumber(c.Y));
            tbl.Table.Set("b", DynValue.NewNumber(c.Z));
            tbl.Table.Set("a", DynValue.NewNumber(c.W));
            return tbl;
        }

        /// <summary>ui.color_edit3(label, r, g, b, flags?) -> { changed, r, g, b } – Color edit widget for RGB floats.</summary>
        public static DynValue color_edit3(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String ? args[0].String : "##col3";
            float r = args.Count > 1 && args[1].Type == DataType.Number ? (float)args[1].Number : 1f;
            float g = args.Count > 2 && args[2].Type == DataType.Number ? (float)args[2].Number : 1f;
            float b = args.Count > 3 && args[3].Type == DataType.Number ? (float)args[3].Number : 1f;
            ImGuiColorEditFlags flags = args.Count > 4 && args[4].Type == DataType.Number ? (ImGuiColorEditFlags)(int)args[4].Number : ImGuiColorEditFlags.None;

            Vector3 col = new(r, g, b);
            bool changed = ImGui.ColorEdit3(label, ref col, flags);

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("changed", DynValue.NewBoolean(changed));
            tbl.Table.Set("r", DynValue.NewNumber(col.X));
            tbl.Table.Set("g", DynValue.NewNumber(col.Y));
            tbl.Table.Set("b", DynValue.NewNumber(col.Z));
            return tbl;
        }

        /// <summary>ui.color_edit4(label, r, g, b, a, flags?) -> { changed, r, g, b, a } – Color edit widget for RGBA floats.</summary>
        public static DynValue color_edit4(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String ? args[0].String : "##col4";
            float r = args.Count > 1 && args[1].Type == DataType.Number ? (float)args[1].Number : 1f;
            float g = args.Count > 2 && args[2].Type == DataType.Number ? (float)args[2].Number : 1f;
            float b = args.Count > 3 && args[3].Type == DataType.Number ? (float)args[3].Number : 1f;
            float a = args.Count > 4 && args[4].Type == DataType.Number ? (float)args[4].Number : 1f;
            ImGuiColorEditFlags flags = args.Count > 5 && args[5].Type == DataType.Number ? (ImGuiColorEditFlags)(int)args[5].Number : ImGuiColorEditFlags.None;

            Vector4 col = new(r, g, b, a);
            bool changed = ImGui.ColorEdit4(label, ref col, flags);

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("changed", DynValue.NewBoolean(changed));
            tbl.Table.Set("r", DynValue.NewNumber(col.X));
            tbl.Table.Set("g", DynValue.NewNumber(col.Y));
            tbl.Table.Set("b", DynValue.NewNumber(col.Z));
            tbl.Table.Set("a", DynValue.NewNumber(col.W));
            return tbl;
        }

        /// <summary>ui.color_picker3(label, r, g, b, flags?) -> { changed, r, g, b } – Color picker widget for RGB floats.</summary>
        public static DynValue color_picker3(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String ? args[0].String : "##picker3";
            float r = args.Count > 1 && args[1].Type == DataType.Number ? (float)args[1].Number : 1f;
            float g = args.Count > 2 && args[2].Type == DataType.Number ? (float)args[2].Number : 1f;
            float b = args.Count > 3 && args[3].Type == DataType.Number ? (float)args[3].Number : 1f;
            ImGuiColorEditFlags flags = args.Count > 4 && args[4].Type == DataType.Number ? (ImGuiColorEditFlags)(int)args[4].Number : ImGuiColorEditFlags.None;

            Vector3 col = new(r, g, b);
            bool changed = ImGui.ColorPicker3(label, ref col, flags);

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("changed", DynValue.NewBoolean(changed));
            tbl.Table.Set("r", DynValue.NewNumber(col.X));
            tbl.Table.Set("g", DynValue.NewNumber(col.Y));
            tbl.Table.Set("b", DynValue.NewNumber(col.Z));
            return tbl;
        }

        /// <summary>ui.color_picker4(label, r, g, b, a, flags?) -> { changed, r, g, b, a } – Color picker widget for RGBA floats.</summary>
        public static DynValue color_picker4(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String ? args[0].String : "##picker4";
            float r = args.Count > 1 && args[1].Type == DataType.Number ? (float)args[1].Number : 1f;
            float g = args.Count > 2 && args[2].Type == DataType.Number ? (float)args[2].Number : 1f;
            float b = args.Count > 3 && args[3].Type == DataType.Number ? (float)args[3].Number : 1f;
            float a = args.Count > 4 && args[4].Type == DataType.Number ? (float)args[4].Number : 1f;
            ImGuiColorEditFlags flags = args.Count > 5 && args[5].Type == DataType.Number ? (ImGuiColorEditFlags)(int)args[5].Number : ImGuiColorEditFlags.None;

            Vector4 col = new(r, g, b, a);
            bool changed = ImGui.ColorPicker4(label, ref col, flags);

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("changed", DynValue.NewBoolean(changed));
            tbl.Table.Set("r", DynValue.NewNumber(col.X));
            tbl.Table.Set("g", DynValue.NewNumber(col.Y));
            tbl.Table.Set("b", DynValue.NewNumber(col.Z));
            tbl.Table.Set("a", DynValue.NewNumber(col.W));
            return tbl;
        }

        /// <summary>ui.columns(count?, id?, border?) – Switch to columns layout (deprecated API).</summary>
        public static void columns(ScriptExecutionContext ctx, CallbackArguments args)
        {
            int count = args.Count > 0 && args[0].Type == DataType.Number ? (int)args[0].Number : 1;
            string id = args.Count > 1 && args[1].Type == DataType.String ? args[1].String : string.Empty;
            bool border = args.Count > 2 && args[2].Type == DataType.Boolean ? args[2].Boolean : true;
            ImGui.Columns(count, id, border);
        }

        /// <summary>ui.combo(label, current_index, items_table) -> { changed, index } – Combo box from a Lua string array, returning selected index.</summary>
        public static DynValue combo(ScriptExecutionContext ctx, CallbackArguments args)
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

        /// <summary>ui.create_context() – Create an ImGui context and return its pointer as number.</summary>
        public static double create_context()
        {
            var ptr = ImGui.CreateContext();
            return (double)ptr;
        }

        /// <summary>ui.debug_check_version_and_data_layout() – Run Dear ImGui internal version/layout check (if available in your build).</summary>
        public static void debug_check_version_and_data_layout()
        {
            // Many ImGui.NET builds run this internally; no-op here to avoid signature mismatches.
            // Intentionally left empty to keep API parity without risking compile errors.
        }

        /// <summary>ui.debug_flash_style_color(idx, duration?) – Flash a style color for debugging.</summary>
        public static void debug_flash_style_color(ScriptExecutionContext ctx, CallbackArguments args)
        {
            int idx = args.Count > 0 && args[0].Type == DataType.Number ? (int)args[0].Number : 0;
            // Duration overload may not exist in all builds; stick to the safest one-arg call.
            ImGui.DebugFlashStyleColor((ImGuiCol)idx);
        }

        /// <summary>ui.debug_log(text) – Append text to Dear ImGui debug log.</summary>
        public static void debug_log(string? text)
        {
            ImGui.DebugLog(text ?? string.Empty);
        }

        /// <summary>ui.debug_start_item_picker() – Start the item picker (press key to select an item).</summary>
        public static void debug_start_item_picker()
        {
            ImGui.DebugStartItemPicker();
        }

        /// <summary>ui.debug_text_encoding(text) – Debug print string encoding diagnostics.</summary>
        public static void debug_text_encoding(string? text)
        {
            ImGui.DebugTextEncoding(text ?? string.Empty);
        }

        /// <summary>ui.destroy_context(ctx_ptr?) – Destroy the current or specified ImGui context.</summary>
        public static void destroy_context(ScriptExecutionContext ctx, CallbackArguments args)
        {
            if (args.Count > 0 && args[0].Type == DataType.Number)
                ImGui.DestroyContext((nint)(long)args[0].Number);
            else
                ImGui.DestroyContext();
        }

        /// <summary>ui.destroy_platform_windows() – Destroy platform windows (multi-viewport).</summary>
        public static void destroy_platform_windows()
        {
            ImGui.DestroyPlatformWindows();
        }

        /// <summary>ui.dock_space(id, w?, h?, flags?) – Create a dockspace node and return its id.</summary>
        public static double dock_space(ScriptExecutionContext ctx, CallbackArguments args)
        {
            uint id = args.Count > 0 && args[0].Type == DataType.Number ? (uint)args[0].Number : 0u;
            float w = args.Count > 1 && args[1].Type == DataType.Number ? (float)args[1].Number : 0f;
            float h = args.Count > 2 && args[2].Type == DataType.Number ? (float)args[2].Number : 0f;
            ImGuiDockNodeFlags flags = args.Count > 3 && args[3].Type == DataType.Number ? (ImGuiDockNodeFlags)(int)args[3].Number : ImGuiDockNodeFlags.None;
            var outId = ImGui.DockSpace(id, new Vector2(w, h), flags);
            return outId;
        }

        /// <summary>ui.dock_space_over_viewport(flags?) – Create a dockspace over the main viewport and return its id.</summary>
        public static double dock_space_over_viewport(ScriptExecutionContext ctx, CallbackArguments args, uint dsId)
        {
            ImGuiDockNodeFlags flags = args.Count > 0 && args[0].Type == DataType.Number ? (ImGuiDockNodeFlags)(int)args[0].Number : ImGuiDockNodeFlags.None;
            var vp = ImGui.GetMainViewport();
            var id = ImGui.DockSpaceOverViewport(dsId, vp, flags);
            return id;
        }

        /// <summary>ui.drag_float(label, value, speed, min, max, format?) -> { changed, value } – Drag float widget with optional limits and format.</summary>
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

        /// <summary>ui.drag_float2(label, x, y, speed?, min?, max?, format?) -> { changed, x, y }.</summary>
        public static DynValue drag_float2(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String ? args[0].String : "##df2";
            Vector2 v = new(
                args.Count > 1 && args[1].Type == DataType.Number ? (float)args[1].Number : 0f,
                args.Count > 2 && args[2].Type == DataType.Number ? (float)args[2].Number : 0f
            );
            float speed = args.Count > 3 && args[3].Type == DataType.Number ? (float)args[3].Number : 1f;
            float min = args.Count > 4 && args[4].Type == DataType.Number ? (float)args[4].Number : 0f;
            float max = args.Count > 5 && args[5].Type == DataType.Number ? (float)args[5].Number : 0f;
            string fmt = args.Count > 6 && args[6].Type == DataType.String ? args[6].String : "%.3f";
            bool changed = ImGui.DragFloat2(label, ref v, speed, min, max, fmt);
            var script = ctx.GetScript(); var t = DynValue.NewTable(script);
            t.Table.Set("changed", DynValue.NewBoolean(changed)); t.Table.Set("x", DynValue.NewNumber(v.X)); t.Table.Set("y", DynValue.NewNumber(v.Y)); return t;
        }

        /// <summary>ui.drag_float3(label, x, y, z, speed?, min?, max?, format?) -> { changed, x, y, z }.</summary>
        public static DynValue drag_float3(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String ? args[0].String : "##df3";
            Vector3 v = new(
                args.Count > 1 && args[1].Type == DataType.Number ? (float)args[1].Number : 0f,
                args.Count > 2 && args[2].Type == DataType.Number ? (float)args[2].Number : 0f,
                args.Count > 3 && args[3].Type == DataType.Number ? (float)args[3].Number : 0f
            );
            float speed = args.Count > 4 && args[4].Type == DataType.Number ? (float)args[4].Number : 1f;
            float min = args.Count > 5 && args[5].Type == DataType.Number ? (float)args[5].Number : 0f;
            float max = args.Count > 6 && args[6].Type == DataType.Number ? (float)args[6].Number : 0f;
            string fmt = args.Count > 7 && args[7].Type == DataType.String ? args[7].String : "%.3f";
            bool changed = ImGui.DragFloat3(label, ref v, speed, min, max, fmt);
            var script = ctx.GetScript(); var t = DynValue.NewTable(script);
            t.Table.Set("changed", DynValue.NewBoolean(changed)); t.Table.Set("x", DynValue.NewNumber(v.X)); t.Table.Set("y", DynValue.NewNumber(v.Y)); t.Table.Set("z", DynValue.NewNumber(v.Z)); return t;
        }

        /// <summary>ui.drag_float4(label, x, y, z, w, speed?, min?, max?, format?) -> { changed, x, y, z, w }.</summary>
        public static DynValue drag_float4(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String ? args[0].String : "##df4";
            Vector4 v = new(
                args.Count > 1 && args[1].Type == DataType.Number ? (float)args[1].Number : 0f,
                args.Count > 2 && args[2].Type == DataType.Number ? (float)args[2].Number : 0f,
                args.Count > 3 && args[3].Type == DataType.Number ? (float)args[3].Number : 0f,
                args.Count > 4 && args[4].Type == DataType.Number ? (float)args[4].Number : 0f
            );
            float speed = args.Count > 5 && args[5].Type == DataType.Number ? (float)args[5].Number : 1f;
            float min = args.Count > 6 && args[6].Type == DataType.Number ? (float)args[6].Number : 0f;
            float max = args.Count > 7 && args[7].Type == DataType.Number ? (float)args[7].Number : 0f;
            string fmt = args.Count > 8 && args[8].Type == DataType.String ? args[8].String : "%.3f";
            bool changed = ImGui.DragFloat4(label, ref v, speed, min, max, fmt);
            var script = ctx.GetScript(); var t = DynValue.NewTable(script);
            t.Table.Set("changed", DynValue.NewBoolean(changed)); t.Table.Set("x", DynValue.NewNumber(v.X)); t.Table.Set("y", DynValue.NewNumber(v.Y)); t.Table.Set("z", DynValue.NewNumber(v.Z)); t.Table.Set("w", DynValue.NewNumber(v.W)); return t;
        }

        /// <summary>ui.drag_float_range2(label, v_min, v_max, speed?, min?, max?, fmt_min?, fmt_max?, flags?) -> { changed, v_min, v_max }.</summary>
        public static DynValue drag_float_range2(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String ? args[0].String : "##dfr2";
            float vMin = args.Count > 1 && args[1].Type == DataType.Number ? (float)args[1].Number : 0f;
            float vMax = args.Count > 2 && args[2].Type == DataType.Number ? (float)args[2].Number : 0f;
            float speed = args.Count > 3 && args[3].Type == DataType.Number ? (float)args[3].Number : 1f;
            float min = args.Count > 4 && args[4].Type == DataType.Number ? (float)args[4].Number : 0f;
            float max = args.Count > 5 && args[5].Type == DataType.Number ? (float)args[5].Number : 0f;
            string fmtMin = args.Count > 6 && args[6].Type == DataType.String ? args[6].String : "%.3f";
            string fmtMax = args.Count > 7 && args[7].Type == DataType.String ? args[7].String : "%.3f";
            ImGuiSliderFlags flags = args.Count > 8 && args[8].Type == DataType.Number ? (ImGuiSliderFlags)(int)args[8].Number : ImGuiSliderFlags.None;
            bool changed = ImGui.DragFloatRange2(label, ref vMin, ref vMax, speed, min, max, fmtMin, fmtMax, flags);
            var script = ctx.GetScript(); var t = DynValue.NewTable(script);
            t.Table.Set("changed", DynValue.NewBoolean(changed)); t.Table.Set("v_min", DynValue.NewNumber(vMin)); t.Table.Set("v_max", DynValue.NewNumber(vMax)); return t;
        }

        /// <summary>ui.drag_int(label, value, speed, min, max) -> { changed, value } – Drag integer widget with optional limits.</summary>
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

        /// <summary>ui.drag_int_range2(label, v_min, v_max, speed?, min?, max?, fmt_min?, fmt_max?) -> { changed, v_min, v_max }.</summary>
        public static DynValue drag_int_range2(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String ? args[0].String : "##dir2";
            int vMin = args.Count > 1 && args[1].Type == DataType.Number ? (int)args[1].Number : 0;
            int vMax = args.Count > 2 && args[2].Type == DataType.Number ? (int)args[2].Number : 0;
            float speed = args.Count > 3 && args[3].Type == DataType.Number ? (float)args[3].Number : 1f;
            int min = args.Count > 4 && args[4].Type == DataType.Number ? (int)args[4].Number : 0;
            int max = args.Count > 5 && args[5].Type == DataType.Number ? (int)args[5].Number : 0;
            string fmtMin = args.Count > 6 && args[6].Type == DataType.String ? args[6].String : "%d";
            string fmtMax = args.Count > 7 && args[7].Type == DataType.String ? args[7].String : "%d";
            bool changed = ImGui.DragIntRange2(label, ref vMin, ref vMax, speed, min, max, fmtMin, fmtMax);
            var script = ctx.GetScript(); var t = DynValue.NewTable(script);
            t.Table.Set("changed", DynValue.NewBoolean(changed)); t.Table.Set("v_min", DynValue.NewNumber(vMin)); t.Table.Set("v_max", DynValue.NewNumber(vMax)); return t;
        }

        /// <summary>ui.drag_scalar_n_s32(label, values_table, speed?, min?, max?, format?, flags?) -> { changed, values }.</summary>
        public static DynValue drag_scalar_n_s32(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String ? args[0].String : "##dsn32";

            // Read Lua table -> int[]
            int[] vals = Array.Empty<int>();
            if (args.Count > 1 && args[1].Type == DataType.Table)
            {
                int n = args[1].Table.Length;
                vals = new int[n];
                for (int i = 0; i < n; i++)
                {
                    var v = args[1].Table.Get(i + 1);
                    vals[i] = (v.Type == DataType.Number) ? (int)v.Number : 0;
                }
            }
            int count = vals.Length;
            float speed = args.Count > 2 && args[2].Type == DataType.Number ? (float)args[2].Number : 1f;
            int min = args.Count > 3 && args[3].Type == DataType.Number ? (int)args[3].Number : 0;
            int max = args.Count > 4 && args[4].Type == DataType.Number ? (int)args[4].Number : 0;
            string fmt = args.Count > 5 && args[5].Type == DataType.String ? args[5].String : "%d";
            ImGuiSliderFlags flags = args.Count > 6 && args[6].Type == DataType.Number ? (ImGuiSliderFlags)(int)args[6].Number : ImGuiSliderFlags.None;

            bool changed = false;

            if (count > 0)
            {
                unsafe
                {
                    // locals on stack -> no fixed; only pin the managed array
                    int minLocal = min, maxLocal = max;
                    void* pMin = (min != 0 || max != 0) ? &minLocal : null;
                    void* pMax = (min != 0 || max != 0) ? &maxLocal : null;

                    fixed (int* pVals = vals)
                    {
                        changed = ImGui.DragScalarN(
                            label,
                            ImGuiDataType.S32,
                            (IntPtr)pVals,
                            count,
                            speed,
                            pMin == null ? IntPtr.Zero : (IntPtr)pMin,
                            pMax == null ? IntPtr.Zero : (IntPtr)pMax,
                            fmt,
                            flags
                        );
                    }
                }
            }

            var script = ctx.GetScript();
            var outTbl = DynValue.NewTable(script);
            var arr = DynValue.NewTable(script);
            for (int i = 0; i < vals.Length; i++)
                arr.Table.Append(DynValue.NewNumber(vals[i]));
            outTbl.Table.Set("changed", DynValue.NewBoolean(changed));
            outTbl.Table.Set("values", arr);
            return outTbl;
        }

        /// <summary>ui.drag_scalar_s32(label, value, speed?, min?, max?, format?, flags?) -> { changed, value }.</summary>
        public static DynValue drag_scalar_s32(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string label = args.Count > 0 && args[0].Type == DataType.String ? args[0].String : "##dss32";
            int value = args.Count > 1 && args[1].Type == DataType.Number ? (int)args[1].Number : 0;
            float speed = args.Count > 2 && args[2].Type == DataType.Number ? (float)args[2].Number : 1f;
            int min = args.Count > 3 && args[3].Type == DataType.Number ? (int)args[3].Number : 0;
            int max = args.Count > 4 && args[4].Type == DataType.Number ? (int)args[4].Number : 0;
            string fmt = args.Count > 5 && args[5].Type == DataType.String ? args[5].String : "%d";
            ImGuiSliderFlags flags = args.Count > 6 && args[6].Type == DataType.Number ? (ImGuiSliderFlags)(int)args[6].Number : ImGuiSliderFlags.None;

            unsafe
            {
                int v = value;
                int minLocal = min, maxLocal = max;

                void* p = &v;                         // local value → address OK
                void* pMin = (min != 0 || max != 0) ? &minLocal : null;
                void* pMax = (min != 0 || max != 0) ? &maxLocal : null;

                bool changed = ImGui.DragScalar(
                    label,
                    ImGuiDataType.S32,
                    (IntPtr)p,
                    speed,
                    pMin == null ? IntPtr.Zero : (IntPtr)pMin,
                    pMax == null ? IntPtr.Zero : (IntPtr)pMax,
                    fmt,
                    flags
                );

                var script = ctx.GetScript();
                var t = DynValue.NewTable(script);
                t.Table.Set("changed", DynValue.NewBoolean(changed));
                t.Table.Set("value", DynValue.NewNumber(v));
                return t;
            }
        }

        /// <summary>ui.dummy(w, h) – Add an invisible spacer of given size.</summary>
        public static void dummy(double w, double h)
        {
            ImGui.Dummy(new Vector2((float)w, (float)h));
        }

        /// <summary>ui.end_child() – End the current child region started by ui.begin_child().</summary>
        public static void end_child()
        {
            ImGui.EndChild();
        }

        /// <summary>ui.end_disabled() – End a disabled block started with ui.begin_disabled().</summary>
        public static void end_disabled()
        {
            ImGui.EndDisabled();
        }

        /// <summary>ui.end_group() – End the current group started by ui.begin_group().</summary>
        public static void end_group()
        {
            ImGui.EndGroup();
        }

        /// <summary>ui.end_multi_select() – End a multi-selection block; returns a table of final requests (same shape as begin).</summary>
        public static DynValue end_multi_select(ScriptExecutionContext ctx, CallbackArguments _)
        {
            var io = ImGui.EndMultiSelect();

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("itemsCount", DynValue.NewNumber(io.ItemsCount));
            tbl.Table.Set("navIdSelected", DynValue.NewBoolean(io.NavIdSelected));
            tbl.Table.Set("rangeSrcReset", DynValue.NewBoolean(io.RangeSrcReset));
            tbl.Table.Set("rangeSrcItem", DynValue.NewNumber((long)io.RangeSrcItem));
            tbl.Table.Set("navIdItem", DynValue.NewNumber((long)io.NavIdItem));

            var reqArr = DynValue.NewTable(script);

            int n = io.Requests.Size;
            for (int i = 0; i < n; i++)
            {
                var r = io.Requests[i];
                var rt = DynValue.NewTable(script);
                rt.Table.Set("type", DynValue.NewNumber((int)r.Type));
                rt.Table.Set("selected", DynValue.NewBoolean(r.Selected));
                rt.Table.Set("rangeDirection", DynValue.NewNumber(r.RangeDirection));
                rt.Table.Set("rangeFirstItem", DynValue.NewNumber((long)r.RangeFirstItem));
                rt.Table.Set("rangeLastItem", DynValue.NewNumber((long)r.RangeLastItem));
                reqArr.Table.Append(rt);
            }

            tbl.Table.Set("requests", reqArr);
            return tbl;
        }

        /// <summary>ui.end_popup() – End the current popup.</summary>
        public static void end_popup()
        {
            ImGui.EndPopup();
        }

        /// <summary>ui.end_tab_bar() – End the current tab bar.</summary>
        public static void end_tab_bar()
        {
            ImGui.EndTabBar();
        }

        /// <summary>ui.end_tab_item() – End the current tab item.</summary>
        public static void end_tab_item()
        {
            ImGui.EndTabItem();
        }

        /// <summary>ui.end_window() – End the current window started by ui.begin_window().</summary>
        public static void end_window()
        {
            ImGui.End();
        }

        /// <summary>ui.help_marker(text) – Draw a small "(?)" that shows a wrapped tooltip when hovered.</summary>
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

        /// <summary>ui.image(texture_id, width, height) – Draw an image using a host-provided texture id.</summary>
        public static void image(double textureId, double width, double height)
        {
            IntPtr id = new IntPtr(unchecked((long)textureId));
            ImGui.Image(id, new Vector2((float)width, (float)height));
        }

        /// <summary>ui.indent(amount) – Indent subsequent items to the right by `amount` pixels.</summary>
        public static void indent(double amount)
        {
            ImGui.Indent((float)amount);
        }

        /// <summary>ui.input_text(label, text, max_length) -> { changed, value } – Text input with max length, returning new value and changed flag.</summary>
        public static DynValue input_text(ScriptExecutionContext ctx, CallbackArguments args)
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

            uint len = (uint)Math.Min(maxLength, int.MaxValue);

            string buffer = text;
            bool changed = ImGui.InputText(label, ref buffer, len);

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("changed", DynValue.NewBoolean(changed));
            tbl.Table.Set("value", DynValue.NewString(buffer));
            return tbl;
        }

        /// <summary>ui.new_line() – Force a new line (similar to pressing Enter).</summary>
        public static void new_line()
        {
            ImGui.NewLine();
        }

        /// <summary>ui.open_popup(id) – Request opening a popup with the given id.</summary>
        public static void open_popup(string? id)
        {
            ImGui.OpenPopup(id ?? "##popup");
        }

        /// <summary>ui.pop_style_color(count) – Pop one or more style colors from the stack.</summary>
        public static void pop_style_color(int count = 1)
        {
            ImGui.PopStyleColor(count);
        }

        /// <summary>ui.progress_bar(fraction, width, height, overlay_text?) – Draw a progress bar with value in [0,1].</summary>
        public static void progress_bar(double fraction, double width, double height, string? overlay = null)
        {
            float f = Math.Clamp((float)fraction, 0f, 1f);
            ImGui.ProgressBar(
                f,
                new Vector2((float)width, (float)height),
                overlay
            );
        }

        /// <summary>ui.push_style_color(idx, r, g, b, a) – Push a style color (ImGuiCol) onto the stack.</summary>
        public static void push_style_color(int idx, double r, double g, double b, double a = 1.0)
        {
            ImGui.PushStyleColor(
                (ImGuiCol)idx,
                new Vector4((float)r, (float)g, (float)b, (float)a)
            );
        }

        /// <summary>ui.radio_button(label, active) -> bool – Radio-style toggle; returns true when clicked.</summary>
        public static bool radio_button(string? label, bool active)
        {
            return ImGui.RadioButton(label ?? string.Empty, active);
        }

        /// <summary>ui.radio_button_value(label, current, this_value) -> { changed, value } – Radio group helper returning new value and changed flag.</summary>
        public static DynValue radio_button_value(ScriptExecutionContext ctx, CallbackArguments args)
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

        /// <summary>ui.same_line() – Place the next item on the same line.</summary>
        public static void same_line()
        {
            ImGui.SameLine();
        }

        /// <summary>ui.selectable(label, selected) -> { clicked, selected } – Selectable item that returns click state and resulting selection.</summary>
        public static DynValue selectable(ScriptExecutionContext ctx, CallbackArguments args)
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

        /// <summary>ui.separator() – Draw a horizontal separator line.</summary>
        public static void separator()
        {
            ImGui.Separator();
        }

        /// <summary>ui.set_cursor_pos(x, y) – Move the cursor within the current window (top-left is 0,0).</summary>
        public static void set_cursor_pos(double x, double y)
        {
            ImGui.SetCursorPos(new Vector2((float)x, (float)y));
        }

        /// <summary>ui.set_next_window_pos(x, y) – Set the position for the next window created by ui.begin_window().</summary>
        public static void set_next_window_pos(double x, double y)
        {
            ImGui.SetNextWindowPos(new Vector2((float)x, (float)y));
        }

        /// <summary>ui.set_next_window_size(w, h) – Set the size for the next window created by ui.begin_window().</summary>
        public static void set_next_window_size(double w, double h)
        {
            ImGui.SetNextWindowSize(new Vector2((float)w, (float)h));
        }

        /// <summary>ui.slider_float(label, value, min, max, format?) -> { changed, value } – Float slider returning new value and changed flag.</summary>
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

            string format = args.Count > 4 && args[4].Type == DataType.String
                ? args[4].String
                : "%.3f";

            float v = value;
            bool changed = ImGui.SliderFloat(label, ref v, min, max, format);

            var script = ctx.GetScript();
            var tbl = DynValue.NewTable(script);
            tbl.Table.Set("changed", DynValue.NewBoolean(changed));
            tbl.Table.Set("value", DynValue.NewNumber(v));
            return tbl;
        }

        /// <summary>ui.slider_int(label, value, min, max) -> { changed, value } – Integer slider returning new value and changed flag.</summary>
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

        /// <summary>ui.small_button(label) – Draw a smaller button; returns true when pressed.</summary>
        public static bool small_button(string? label)
        {
            return ImGui.SmallButton(label ?? string.Empty);
        }

        /// <summary>ui.spacing() – Add a vertical gap (empty space) between items.</summary>
        public static void spacing()
        {
            ImGui.Spacing();
        }

        /// <summary>ui.text(str) – Draw a single line of text without wrapping.</summary>
        public static void text(string? str)
        {
            ImGui.TextUnformatted(str ?? string.Empty);
        }

        /// <summary>ui.text_wrapped(str) – Draw text that automatically wraps at the window edge.</summary>
        public static void text_wrapped(string? str)
        {
            ImGui.TextWrapped(str ?? string.Empty);
        }

        /// <summary>ui.tree_node(label) – Tree node; returns true if open (then call ui.tree_pop()).</summary>
        public static bool tree_node(string? label)
        {
            return ImGui.TreeNode(label ?? string.Empty);
        }

        /// <summary>ui.tree_pop() – Close the most recent open tree node.</summary>
        public static void tree_pop()
        {
            ImGui.TreePop();
        }

        /// <summary>ui.unindent(amount) – Remove indentation to the left by `amount` pixels.</summary>
        public static void unindent(double amount)
        {
            ImGui.Unindent((float)amount);
        }

    }
}

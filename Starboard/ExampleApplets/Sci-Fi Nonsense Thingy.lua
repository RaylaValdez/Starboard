-- Every Lua applet **must** define a global table named `app`.
app = {}

----------------------------------------------------------------
-- App identity
----------------------------------------------------------------
function app.id()
    return "user.scifi_nonsense"
end

function app.name()
    return "Sci-Fi Nonsense Thingy"
end

function app.favicon_url()
    return "https://creazilla-store.fra1.digitaloceanspaces.com/icons/3244501/terminal-icon-md.png"
end

----------------------------------------------------------------
-- Persistent state
----------------------------------------------------------------
function app.init()
    state.name           = state.name           or "Operator-01"
    state.counter        = state.counter        or 0
    state.power_level    = state.power_level    or 0.72
    state.shield_level   = state.shield_level   or 0.36
    state.heat_level     = state.heat_level     or 0.18
    state.main_tab       = state.main_tab       or 0          -- 0=Overview,1=Systems,2=Diagnostics
    state.theme_mode     = state.theme_mode     or 0          -- 0=Default,1=Night,2=Alert
    state.glitch_int     = state.glitch_int     or 0.15
    state.scan_depth     = state.scan_depth     or 42
    state.safety_lock    = state.safety_lock    or true
    state.combo_index    = state.combo_index    or 0
    state.tree_open      = state.tree_open      or false
    state.last_popup_time= state.last_popup_time or 0
end

----------------------------------------------------------------
-- Helpers
----------------------------------------------------------------
local function themed_header(text)
    ui.spacing()
    ui.separator()
    ui.text_wrapped(text)
    ui.separator()
end

local function apply_theme_colors()
    -- theme_mode: 0=default,1=night,2=alert
    if state.theme_mode == 1 then
        -- Night mode: teal / muted
        ui.push_style_color(0, 0.4, 1.0, 0.9, 1.0)  -- ImGuiCol_Text
        ui.push_style_color(2, 0.1, 0.1, 0.16, 1.0) -- ImGuiCol_WindowBg
    elseif state.theme_mode == 2 then
        -- Alert mode: red / dark
        ui.push_style_color(0, 1.0, 0.35, 0.35, 1.0) -- Text
        ui.push_style_color(2, 0.08, 0.02, 0.02, 1.0) -- WindowBg
    else
        -- Default: subtle cyan tint
        ui.push_style_color(0, 0.8, 0.95, 1.0, 1.0)
        ui.push_style_color(2, 0.06, 0.08, 0.10, 1.0)
    end
end

local function pop_theme_colors()
    -- Matching pushes in apply_theme_colors()
    ui.pop_style_color(2)
end

----------------------------------------------------------------
-- Main draw
----------------------------------------------------------------
function app.draw(dt, w, h)
    state.counter = (state.counter or 0) + dt

    ----------------------------------------------------------------
    -- Root child: full panel with a border
    ----------------------------------------------------------------
    if ui.begin_child("terminal_root", w, h, true) then
        apply_theme_colors()

        ----------------------------------------------------------------
        -- Header row
        ----------------------------------------------------------------
        ui.begin_group()
        ui.text("TRIQUETRA // STARBOARD TERMINAL")
        ui.text("Operator: " .. (state.name or "???"))
        ui.same_line()
        ui.help_marker("Lua/ImGui test applet.\nEverything here is just UI surface exercising.")
        ui.end_group()

        ui.new_line()

        -- Time / status mini header
        ui.begin_group()
        ui.text("Uptime: " .. string.format("%.1fs", state.counter or 0))
        ui.text("Panel size: " .. tostring(math.floor(w)) .. " x " .. tostring(math.floor(h)))
        ui.end_group()

        ui.new_line()

        ----------------------------------------------------------------
        -- Main layout: left & right columns as children
        ----------------------------------------------------------------
        local left_w  = w * 0.45
        local right_w = w - left_w - 8

        ui.begin_group()
        if ui.begin_child("left_column", left_w, h - 200, true) then
            ----------------------------------------------------------------
            -- Tabs: Overview / Systems / Diagnostics
            ----------------------------------------------------------------
            if ui.begin_tab_bar("main_tabs") then
                if ui.begin_tab_item("Overview") then
                    state.main_tab = 0
                    ----------------------------------------------------------------
                    -- OVERVIEW TAB
                    ----------------------------------------------------------------
                    themed_header("CORE TELEMETRY")

                    ui.text("Power envelope")
                    ui.progress_bar(state.power_level or 0.0, -1, 0, "POWER")
                    ui.text("Shield integrity")
                    ui.progress_bar(state.shield_level or 0.0, -1, 0, "SHIELDS")
                    ui.text("Thermal saturation")
                    ui.progress_bar(state.heat_level or 0.0, -1, 0, "HEAT")

                    ui.spacing()
                    ui.separator()
                    ui.text("Environment")
                    ui.bullet_text("Local time drift: synced")
                    ui.bullet_text("Subspace channel: stable")
                    ui.bullet_text("Quantum jitter: " .. string.format("%.3f", state.glitch_int or 0))

                    ui.end_tab_item()
                end

                if ui.begin_tab_item("Systems") then
                    state.main_tab = 1
                    ----------------------------------------------------------------
                    -- SYSTEMS TAB
                    ----------------------------------------------------------------
                    themed_header("SYSTEM ROUTING")

                    -- Sliders / drags / checkboxes
                    local r1 = ui.slider_float("Power Level", state.power_level or 0, 0.0, 1.0, "%.2f")
                    if r1.changed then state.power_level = r1.value end

                    local r2 = ui.slider_float("Shield Level", state.shield_level or 0, 0.0, 1.0, "%.2f")
                    if r2.changed then state.shield_level = r2.value end

                    local r3 = ui.slider_float("Thermal Load", state.heat_level or 0, 0.0, 1.0, "%.2f")
                    if r3.changed then state.heat_level = r3.value end

                    local r4 = ui.drag_float("Glitch Intensity", state.glitch_int or 0.15, 0.01, 0.0, 1.0, "%.2f")
                    if r4.changed then state.glitch_int = r4.value end

                    local r5 = ui.slider_int("Scan Depth", state.scan_depth or 42, 0, 100)
                    if r5.changed then state.scan_depth = math.floor(r5.value) end

                    ui.separator()
                    ui.text("Safety Protocols")
                    local chk = ui.checkbox("Safety Lock Engaged", state.safety_lock == true)
                    if chk.changed then state.safety_lock = chk.value end

                    -- Disabled block if safety lock is on
                    ui.begin_disabled(state.safety_lock == true)
                    if ui.button("Execute Risky Jump") then
                        -- does nothing, just visual
                        state.last_popup_time = state.counter
                        ui.open_popup("jump_denied_popup")
                    end
                    ui.end_disabled()

                    ui.same_line()
                    ui.help_marker("Disabled while Safety Lock is engaged.\nToggle if you dare.")

                    ui.end_tab_item()
                end

                if ui.begin_tab_item("Diagnostics") then
                    state.main_tab = 2
                    ----------------------------------------------------------------
                    -- DIAGNOSTICS TAB
                    ----------------------------------------------------------------
                    themed_header("DIAGNOSTIC CHANNEL")

                    -- Input text
                    local it = ui.input_text("Callsign", state.name or "", 32)
                    if it.changed then state.name = it.value end

                    ui.separator()

                    ui.text("Theme Mode")
                    local rb0 = ui.radio_button_value("Default", state.theme_mode or 0, 0)
                    if rb0.changed then state.theme_mode = rb0.value end

                    local rb1 = ui.radio_button_value("Night", state.theme_mode or 0, 1)
                    if rb1.changed then state.theme_mode = rb1.value end

                    local rb2 = ui.radio_button_value("Alert", state.theme_mode or 0, 2)
                    if rb2.changed then state.theme_mode = rb2.value end

                    ui.separator()

                    -- Combo of fake profiles
                    local profile_items = {
                        "Profile A: Patrol",
                        "Profile B: Escort",
                        "Profile C: Siege",
                        "Profile D: Recon"
                    }
                    local cr = ui.combo("Loadout Profile", state.combo_index or 0, profile_items)
                    if cr.changed then state.combo_index = math.floor(cr.index) end

                    ui.separator()

                    -- Tree / collapsing header
                    if ui.collapsing_header("Subsystem Tree") then
                        if ui.tree_node("Reactor") then
                            ui.bullet_text("Core: Online")
                            ui.bullet_text("Harmonics: Nominal")
                            ui.tree_pop()
                        end
                        if ui.tree_node("Sensors") then
                            ui.bullet_text("Long-range: Active")
                            ui.bullet_text("Short-range: Active")
                            ui.tree_pop()
                        end
                        if ui.tree_node("Communications") then
                            ui.bullet_text("Uplink: Stable")
                            ui.bullet_text("Downlink: Stable")
                            ui.tree_pop()
                        end
                    end

                    ui.end_tab_item()
                end

                ui.end_tab_bar()
            end
        end
        ui.end_child()  -- left_column
        ui.end_group()

        ui.same_line()

        ----------------------------------------------------------------
        -- Right column: smaller status + popup + miscellaneous tests
        ----------------------------------------------------------------
        ui.begin_group()
        if ui.begin_child("right_column", right_w - 17, h - 200, true) then
            themed_header("VISUAL CHANNEL")

            ui.text_wrapped("Real-time waveform slot (placeholder)")
            ui.spacing()

            -- Fake "oscilloscope" area using progress bars
            local osc1 = (math.sin(state.counter * 2.1) * 0.5 + 0.5)
            local osc2 = (math.sin(state.counter * 3.4 + 1.0) * 0.5 + 0.5)
            local osc3 = (math.sin(state.counter * 4.9 + 2.0) * 0.5 + 0.5)

            ui.progress_bar(osc1, -1, 0, "CH-1")
            ui.progress_bar(osc2, -1, 0, "CH-2")
            ui.progress_bar(osc3, -1, 0, "CH-3")

            ui.spacing()
            ui.separator()
            ui.text("Bitfield flags")
            ui.bullet_text("Locked: " .. (state.safety_lock and "YES" or "NO"))
            ui.bullet_text("Theme: " .. tostring(state.theme_mode or 0))
            ui.bullet_text("Profile index: " .. tostring(state.combo_index or 0))

            ui.spacing()
            ui.separator()

            -- Small buttons, popup, selectable
            ui.text("Operator Actions")
            if ui.small_button("Ping") then
                state.last_popup_time = state.counter
                ui.open_popup("ping_popup")
            end
            ui.same_line()
            if ui.small_button("Alert") then
                state.theme_mode = 2
                ui.open_popup("alert_popup")
            end

            ui.spacing()

            local sel = ui.selectable("Log: [DEBUG] Subsystem ping", false)
            -- we don't really use sel.clicked; it's just exercising the API

            ui.separator()

            -- Collapsing header with checkbox/drag inside
            if ui.collapsing_header("Advanced Tweaks") then
                local c = ui.checkbox("Simulate Drift", state.sim_drift == true)
                if c.changed then state.sim_drift = c.value end

                local d = ui.drag_int("Drift Magnitude", state.drift_mag or 5, 0.5, 0, 100)
                if d.changed then state.drift_mag = math.floor(d.value) end
            end

            ----------------------------------------------------------------
            -- Popups
            ----------------------------------------------------------------
            if ui.begin_popup("ping_popup") then
                ui.text("PING DISPATCHED")
                ui.text("t = " .. string.format("%.2f", state.counter or 0))
                ui.separator()
                ui.text_wrapped("This is a simple test of ui.open_popup / ui.begin_popup / ui.end_popup.")
                ui.end_popup()
            end

            if ui.begin_popup("alert_popup") then
                ui.text("ALERT MODE ENGAGED")
                ui.separator()
                ui.text_wrapped("Visual theme switched to ALERT.\nAll of this is purely cosmetic.")
                ui.end_popup()
            end

            if ui.begin_popup("jump_denied_popup") then
                ui.text("JUMP EXECUTION BLOCKED")
                ui.separator()
                ui.text_wrapped("Safety Lock is active.\nOverride at your own risk.")
                ui.end_popup()
            end
        end
        ui.end_child() -- right_column
        ui.end_group()

        ----------------------------------------------------------------
        -- Footer row
        ----------------------------------------------------------------
        ui.separator()
        ui.text("Status: ONLINE")
        ui.same_line()
        ui.text("  |  FPS-ish dt: " .. string.format("%.3f", dt or 0))
        ui.same_line()
        ui.text("  |  Test harness only; no real game logic.")

        pop_theme_colors()
    end

    ui.end_child() -- terminal_root

    ui.add_hitregions()
end

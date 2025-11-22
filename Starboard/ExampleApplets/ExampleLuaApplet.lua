-- Every Lua applet **must** define a global table named `app`.
-- Starboard reads functions from this table to understand your applet.
app = {}

-- app.id()
-- This must return a unique ID string for your applet.
-- Convention:
--   "user.<your_name>" or "user.<your_tool_name>"
-- This ID is also used to persist your applet's state.
function app.id()
    return "user.test_state"
end

-- app.name()
-- This is the friendly display name shown inside Starboard.
function app.name()
    return "State Demo"
end

-- Optional: Applets can have custom Icons, just set this link to a hosted image and It'll appear in the AppletList
function app.favicon_url()
    return "https://cdn.robertsspaceindustries.com/static/images/RSI-logo-fb.jpg"
end

-- app.init()
-- Optional.
-- Runs once when the applet loads (or reloads after you modify the file).
-- Great for setting default values the first time.
-- `state` is a persistent table: anything you put in here is
-- automatically saved & restored across sessions.
function app.init()
    state.counter = state.counter or 0
    state.name = state.name or "Unnamed"
end

-- app.draw(dt, w, h)
-- Called every frame while the applet is selected.
--   dt = seconds since last frame
--   w, h = the size of your applet's panel (if needed)
--
-- Use ImGui via the `ui` table (ui.text, ui.button, ui.slider_float, etc.) 
function app.draw(dt, w, h) 
    ui.text("Hello, " .. (state.name or "???"))
    ui.text("Counter: " .. tostring(state.counter or 0))  ((()))

    -- ui.button returns true on click
    if ui.button("Increment") then
        state.counter = (state.counter or 0) + 1
    end
end

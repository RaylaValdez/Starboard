-- Every Lua applet **must** define a global table named `app`.
-- Starboard reads functions from this table to understand your applet.
app = {}

-- app.id()
-- This must return a unique ID string for your applet.
-- Convention:
--   "user.<your_name>" or "user.<your_tool_name>"
-- This ID is also used to persist your applet's state.
function app.id()
    return "user.lua_rsi"
end

-- app.name()
-- This is the friendly display name shown inside Starboard.
function app.name()
    return "Lua RSI Web"
end

-- Tell Starboard you want a web browser panel.
-- If this returns true, Starboard will:
--   • Reserve the right panel for a WebView
--   • Load your URL into it
--   • Handle navigation, cookies, isolation, etc.
function app.uses_webview()
    return true
end

-- Optional: Applets can have custom Icons, just set this link to a hosted image and It'll appear in the AppletList
function app.favicon_url()
    return "https://cdn.robertsspaceindustries.com/static/images/RSI-logo-fb.jpg"
end

-- app.url()
-- REQUIRED for web applets.
-- Must return a URL string every frame.
-- You can return different URLs depending on state if you want.
function app.url()
    return "https://robertsspaceindustries.com"
end

-- app.draw(dt, w, h)
-- Optional for web applets.
-- Runs on top of the WebView and can draw extra UI.
-- Leave empty if you don’t need an overlay.
function app.draw(dt, w, h)
end

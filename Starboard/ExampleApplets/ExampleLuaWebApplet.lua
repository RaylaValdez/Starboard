-- Same as before: always define the `app` table.
app = {}

-- Unique applet identifier.
function app.id()
    return "user.lua_rsi"
end

-- Display name shown in Starboard.
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

-- Optional: a favicon URL for the applet list.
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
    -- ui.text("This draws over the webpage if you want!")
end

using ImGuiNET;
using MoonSharp.Interpreter;
using Overlay_Renderer.Helpers;
using Overlay_Renderer.Methods;
using Starboard.DistributedApplets;
using System.Numerics;

namespace Starboard.Lua
{
    /// <summary>
    /// Wraps a Lua script in an IStarboardApplet so it can appear in the applet list.
    /// 
    /// Expected lua shape:
    /// 
    /// app = {}
    /// 
    /// fucntion app.id() return "user.my_applet" end
    /// function app.name() return "My Lua Applet" end
    /// function app.uses_webview() return false end
    /// function app.favicon_url return nil end
    /// 
    /// -- For web applets (uses_webview() == true):
    /// function app.url() return "https://example.com" end
    /// 
    /// function app.Init() end
    /// function app.Draw(dt, w, h) end
    /// </summary>
    internal sealed class LuaApplet : IStarboardApplet
    {
        private readonly string _scriptPath;
        private Script _script;
        private DynValue _appTable;

        private readonly string _id;
        private string _displayName;
        private bool _usesWebView;
        private string? _faviconUrl;

        private DateTime _lastWriteTime;

        private string? _lastError;
        private DateTime _lastErrorTime;

        // persistent per-applet state
        private Dictionary<string, object?> _stateData = new();

        public string Id => _id;
        public string DisplayName => _displayName;
        public bool UsesWebView => _usesWebView;
        public string? FaviconUrl => _faviconUrl;
        public string ScriptPath => _scriptPath;

        public LuaApplet(string scriptPath)
        {
            _scriptPath = scriptPath;

            try
            {
                _script = LuaEngine.CreateScript();
                _lastWriteTime = File.GetLastWriteTimeUtc(_scriptPath);


                UserData.RegisterType<LuaUiApi>();
                _script.Globals["ui"] = UserData.Create(new LuaUiApi());

                _script.DoFile(_scriptPath);

                _appTable = _script.Globals.Get("app");
                if (_appTable.Type != DataType.Table)
                    throw new InvalidOperationException("Lua applet script must defin global 'app' table.");

                var appTable = _appTable.Table;

                _id = CallStringFunc(appTable, "id") ?? throw new InvalidOperationException("Lua applet must define app.id().");
                _displayName = CallStringFunc(appTable, "name") ?? _id;
                _usesWebView = CallBoolFunc(appTable, "uses_webview") ?? false;
                _faviconUrl = CallStringFunc(appTable, "favicon_url");

                _stateData = LuaStateStore.LoadState(_id);
                ApplyStateToScript(_script);

                Logger.Info($"[LuaApplet] Loaded '{_displayName}' ({_id}) from '{_scriptPath}'. UsesWebView={_usesWebView}.");
            }
            catch (Exception ex)
            {
                Logger.Error($"[LuaApplet] Failed to load lua applet '{scriptPath}': {ex.Message}");
                throw;
            }
        }

        public void Initialize()
        {
            try
            {
                CallVoid("init");
            }
            catch (Exception ex)
            {
                SetError("init", ex);
                Logger.Warn($"[LuaApplet:{_id}] Error in app.init(): {ex.Message}");
            }
        }

        public void Draw(float dt, Vector2 availablesize)
        {
            // hot reload check
            var t = File.GetLastWriteTimeUtc(_scriptPath);
            if (t > _lastWriteTime)
            {
                Reload();
            }

            try
            {
                var appTable = _appTable.Table;

                if (_usesWebView)
                {
                    string? url = CallStringFunc(appTable, "url");
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        SetError("url", new Exception("Web applet must define app.url() returning a non-empty string."));
                    }
                    else
                    {
                        // Use the right-panel content region as viewport
                        Vector2 viewportSize = ImGui.GetContentRegionAvail();
                        WebBrowserManager.DrawWebPage(_id, url, viewportSize);
                    }
                }


                var fn = appTable.Get("draw");
                if (fn.Type == DataType.Function)
                {
                    _script.Call(fn, dt, (double)availablesize.X, (double)availablesize.Y);
                }

                CaptureAndSaveState();
            }
            catch (Exception ex)
            {
                SetError("draw", ex);
                Logger.Warn($"[LuaApplet:{_id}] Error in app.draw(): {ex.Message}");
            }
        }

        private string? CallStringFunc(Table appTable, string name)
        {
            var fn = appTable.Get(name);
            if (fn.Type != DataType.Function)
                return null;

            var res = _script.Call(fn);
            return res.Type == DataType.String ? res.String : null;
        }

        private bool? CallBoolFunc(Table appTable, string name)
        {
            var fn = appTable.Get(name);
            if (fn.Type != DataType.Function)
                return null;

            var res = _script.Call(fn);
            return res.Type == DataType.Boolean ? res.Boolean : null;
        }

        private void CallVoid(string name)
        {
            var appTable = _appTable.Table;
            var fn = appTable.Get(name);
            if (fn.Type != DataType.Function)
                return;

            _script.Call(fn);
        }

        public void Reload()
        {
            try
            {
                Logger.Info($"[LuaApplet:{_id}] Reloading script '{_scriptPath}'...");

                var newScript = LuaEngine.CreateScript();

                UserData.RegisterType<LuaUiApi>();
                newScript.Globals["ui"] = UserData.Create(new LuaUiApi());

                newScript.DoFile(_scriptPath);

                var newAppTable = newScript.Globals.Get("app");
                if (newAppTable.Type != DataType.Table)
                    throw new Exception("Reloaded script is missing global 'app' table.");

                var appTable = newAppTable.Table;

                // local helpers that use newScript, not the old one
                string? GetString(string name)
                {
                    var fn = appTable.Get(name);
                    if (fn.Type != DataType.Function)
                        return null;

                    var res = newScript.Call(fn);
                    return res.Type == DataType.String ? res.String : null;
                }

                bool? GetBool(string name)
                {
                    var fn = appTable.Get(name);
                    if (fn.Type != DataType.Function)
                        return null;

                    var res = newScript.Call(fn);
                    return res.Type == DataType.Boolean ? res.Boolean : (bool?)null;
                }

                var newId = GetString("id");
                var newName = GetString("name");
                var newUsesWebView = GetBool("uses_webview") ?? _usesWebView;
                var newFaviconUrl = GetString("favicon_url");

                Logger.Info($"[LuaApplet:{_id}] Reload OK. New id={newId}, name={newName}");

                // Swap live state over to new script
                _script = newScript;
                _appTable = newAppTable;
                _displayName = newName ?? _displayName;
                _usesWebView = newUsesWebView;
                _faviconUrl = newFaviconUrl;

                ApplyStateToScript(_script);

                _lastWriteTime = File.GetLastWriteTimeUtc(_scriptPath);

                // re-run init() on the new script/table
                CallVoid("init");
            }
            catch (Exception ex)
            {
                SetError("reload", ex);
                Logger.Warn($"[LuaApplet:{_id}] Reload failed: {ex.Message}");
            }
        }

        private void SetError(string where, Exception ex)
        {
            _lastError = $"{where}: {ex.Message}";
            _lastErrorTime = DateTime.UtcNow;

            Logger.Warn($"[LuaApplet:{_id}] {where} error: {ex}");
        }

        public bool TryGetErrorForDisplay(out string message, out float alpha)
        {
            if (_lastError == null)
            {
                message = string.Empty;
                alpha = 0f;
                return false;
            }

            var age = (DateTime.UtcNow - _lastErrorTime).TotalSeconds;

            const double fullVisible = 4.0; // seconds at full opacity
            const double fadeOut = 3.0;     // seconds fading
            const double total = fullVisible + fadeOut;

            if (age > total)
            {
                message = string.Empty;
                alpha = 0f;
                return false;
            }

            if (age <= fullVisible)
            {
                alpha = 1f;
            }
            else
            {
                var t = (float)((age - fullVisible) / fadeOut);
                alpha = 1f - Math.Clamp(t, 0f, 1f);
            }

            message = _lastError;
            return true;
        }

        private void ApplyStateToScript(Script script)
        {
            try
            {
                // Create a fresh Lua table and populate from _stateData
                var table = new Table(script);

                foreach (var kv in _stateData)
                {
                    var key = kv.Key;
                    var value = kv.Value;

                    DynValue dv;

                    switch (value)
                    {
                        case null:
                            dv = DynValue.Nil;
                            break;

                        case bool b:
                            dv = DynValue.NewBoolean(b);
                            break;

                        case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                            dv = DynValue.NewNumber(Convert.ToDouble(value));
                            break;

                        case string s:
                            dv = DynValue.NewString(s);
                            break;

                        default:
                            // unsupported type, skip for now
                            continue;
                    }

                    table[key] = dv;
                }

                script.Globals["state"] = DynValue.NewTable(table);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[LuaApplet:{_id}] ApplyStateToScript failed: {ex.Message}");
            }
        }

        private void CaptureAndSaveState()
        {
            try
            {
                var dyn = _script.Globals.Get("state");
                if (dyn.Type != DataType.Table)
                    return;

                var table = dyn.Table;

                _stateData.Clear();

                foreach (var pair in table.Pairs)
                {
                    // Only care about string keys
                    if (pair.Key.Type != DataType.String)
                        continue;

                    string key = pair.Key.String;
                    var val = pair.Value;

                    object? obj = null;

                    switch (val.Type)
                    {
                        case DataType.Boolean:
                            obj = val.Boolean;
                            break;
                        case DataType.Number:
                            obj = val.Number;
                            break;
                        case DataType.String:
                            obj = val.String;
                            break;
                        default:
                            // ignore nested tables/functions for now
                            continue;
                    }

                    _stateData[key] = obj;
                }

                LuaStateStore.SaveState(_id, _stateData);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[LuaApplet:{_id}] CaptureAndSaveState failed: {ex.Message}");
            }
        }
    }
}

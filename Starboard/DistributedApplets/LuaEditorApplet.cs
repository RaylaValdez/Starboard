using ImGuiNET;
using Overlay_Renderer.Methods;
using System.Numerics;
using MoonSharp.Interpreter;
using Starboard.Lua;
using Starboard.Guis;
using Windows.Networking.Sockets;

namespace Starboard.DistributedApplets
{
    internal sealed class LuaEditorApplet : IStarboardApplet
    {
        public string Id => "starboard.lua_editor";
        public string DisplayName => "Applet Editor";
        public bool UsesWebView => false;

        public string? FaviconUrl => "https://cdn-icons-png.flaticon.com/512/5105/5105701.png";

        private Vector2 _lastAvailableSize;

        private readonly TextEditor _editor = new();

        private sealed class EditorTab
        {
            public readonly TextEditor Editor = new();
            public string Code = string.Empty;
            public string LastSavedCode = string.Empty;
            public string FilePath = string.Empty;
            public string FileName = "Untitled.lua";
            public string Status = "Idle";

            public bool IsOpen = true;

            public bool IsDirty =>
                !string.Equals(Editor.Text, LastSavedCode, StringComparison.Ordinal);
        }

        private readonly List<EditorTab> _tabs = new();
        private int _activeTabIndex = 0;

        private EditorTab ActiveTab
        {
            get
            {
                if (_tabs.Count == 0)
                    _tabs.Add(CreateDefaultTab());
                _activeTabIndex = Math.Clamp(_activeTabIndex, 0, _tabs.Count - 1);
                return _tabs[_activeTabIndex];
            }
        }

        private static EditorTab CreateDefaultTab()
        {
            var tab = new EditorTab();
            tab.Code = DefaultTemplate;
            tab.LastSavedCode = tab.Code;
            tab.FilePath = string.Empty;
            tab.FileName = "Untitled.lua";
            tab.Status = "Ready.";
            tab.Editor.Text = tab.Code;

            return tab;
        }

        private static EditorTab CreateWebTab()
        {
            var tab = new EditorTab();
            tab.Code = WebTemplate;
            tab.LastSavedCode = tab.Code;
            tab.FilePath = string.Empty;
            tab.FileName = "Untitled.lua";
            tab.Status = "Ready.";
            tab.Editor.Text = tab.Code;

            return tab;
        }

        // Current buffer in the editor
        private const string DefaultTemplate = @"-- Every Lua applet **must** define a global table named `app`.
-- Starboard reads functions from this table to understand your applet.
app = {}

-- app.id()
-- This must return a unique ID string for your applet.
-- Convention:
--   ""user.<your_name>"" or ""user.<your_tool_name>""
-- This ID is also used to persist your applet's state.
function app.id()
    return ""user.test_state""
end

-- app.name()
-- This is the friendly display name shown inside Starboard.
function app.name()
    return ""Example Applet""
end

-- app.init()
-- Optional.
-- Runs once when the applet loads (or reloads after you modify the file).
-- Great for setting default values the first time.
-- `state` is a persistent table: anything you put in here is
-- automatically saved & restored across sessions.
function app.init()
    state.counter = state.counter or 0
    state.name = state.name or ""Unnamed""
end

-- app.draw(dt, w, h)
-- Called every frame while the applet is selected.
--   dt = seconds since last frame
--   w, h = the size of your applet's panel (if needed)
--
-- Use ImGui via the `ui` table (ui.text, ui.button, ui.slider_float, etc.) 
function app.draw(dt, w, h) 
    ui.text(""Hello, "" .. (state.name or ""???""))
    ui.text(""Counter: "" .. tostring(state.counter or 0))

    -- ui.button returns true on click
    if ui.button(""Increment"") then
        state.counter = (state.counter or 0) + 1
    end
end
";

        private const string WebTemplate = @"-- Same as before: always define the `app` table.
app = {}

-- Unique applet identifier.
function app.id()
    return ""user.lua_rsi""
end

-- Display name shown in Starboard.
function app.name()
    return ""Lua RSI Web""
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
    return ""https://cdn.robertsspaceindustries.com/static/images/RSI-logo-fb.jpg""
end

-- app.url()
-- REQUIRED for web applets.
-- Must return a URL string every frame.
-- You can return different URLs depending on state if you want.
function app.url()
    return ""https://robertsspaceindustries.com""
end

-- app.draw(dt, w, h)
-- Optional for web applets.
-- Runs on top of the WebView and can draw extra UI.
-- Leave empty if you don’t need an overlay.
function app.draw(dt, w, h)
    -- ui.text(""This draws over the webpage if you want!"")
end
";

        private int _pendingCloseTabIndex = -1;
        private int _pendingImmediateCloseIndex = -1;
        private bool _popupOpenRequested = false;

        private static string GetExternDir()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Starboard",
                "ExternApplets");

            Directory.CreateDirectory(dir);
            return dir;
        }

        // ---------------------------------------------------------------------

        public void Initialize()
        {
            _tabs.Clear();
            _tabs.Add(CreateDefaultTab());
            _activeTabIndex = 0;
        }

        public void Draw(float dt, Vector2 availableSize)
        {
            _lastAvailableSize = availableSize;

            if (!ImGui.BeginChild("Editor", availableSize, ImGuiChildFlags.Borders, ImGuiWindowFlags.MenuBar))
                return;

            var io = ImGui.GetIO();
            var tab = ActiveTab;

            // Handle any clean-tab close requested last frame
            if (_pendingImmediateCloseIndex >= 0)
            {
                CloseTab(_pendingImmediateCloseIndex);
                _pendingImmediateCloseIndex = -1;
            }

            bool editorFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);

            if (editorFocused && io.KeyCtrl)
            {
                if (ImGui.IsKeyPressed(ImGuiKey.N, false))
                {
                    NewFileTab();
                }

                if (io.KeyShift && ImGui.IsKeyPressed(ImGuiKey.N, false))
                {
                    NewFileTab();
                }

                if (ImGui.IsKeyPressed(ImGuiKey.W, false))
                {
                    RequestCloseTab(_activeTabIndex);
                }

                if (ImGui.IsKeyPressed(ImGuiKey.S, false))
                {
                    SaveFile();
                }

                if (ImGui.IsKeyPressed(ImGuiKey.S, false) && io.KeyShift)
                {
                    SaveFileAs();
                }

                if (ImGui.IsKeyPressed(ImGuiKey.O, false))
                {
                    OpenFileIntoNewTab();
                }
            }

            // --- Menu bar -----------------------------------------------------
            if (ImGui.BeginMenuBar())
            {
                if (ImGui.BeginMenu("File"))
                {
                    bool hasTab = _tabs.Count > 0;

                    if (ImGui.BeginMenu("New"))
                    {
                        if (ImGui.MenuItem("New Applet", shortcut: "CTRL + N", selected: false, enabled: hasTab))
                        {
                            NewFileTab();
                        }

                        if (ImGui.MenuItem("New Web Applet", shortcut: "CTRL + SHIFT + N", selected: false, enabled: hasTab))
                        {
                            NewFileTab(true);
                        }

                        ImGui.EndMenu();
                    }



                    if (ImGui.MenuItem("Open", shortcut: "CTRL + O", selected: false, enabled: hasTab))
                    {
                        OpenFileIntoNewTab();
                    }


                    if (ImGui.MenuItem("Save", shortcut: "CTRL + S", selected: false, enabled: hasTab))
                    {
                        SaveFile();
                    }

                    if (ImGui.MenuItem("Save As", shortcut: "CTRL + SHIFT + S", selected: false, enabled: hasTab))
                    {
                        SaveFileAs();
                    }

                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Tools"))
                {
                    bool hasTab = _tabs.Count > 0;
                    if (ImGui.MenuItem("Method List", shortcut: "CTRL + SPACE", selected: false, enabled: hasTab))
                    {
                        ActiveTab.Editor.OpenCompletion();
                    }

                    if (ImGui.MenuItem("Validate", shortcut: null, selected: false, enabled: hasTab))
                    {
                        ValidateCurrentCode();
                    }

                    ImGui.EndMenu();
                }

                string status = ActiveTab.Status;

                if (ImGui.BeginMenu(status, false))
                {
                    ImGui.EndMenu();
                }

                ImGui.EndMenuBar();
            }

            // --- Editor tabs --------------------------------------------------
            if (ImGui.BeginTabBar("EditorTabs",
                ImGuiTabBarFlags.Reorderable | ImGuiTabBarFlags.AutoSelectNewTabs))
            {
                for (int i = 0; i < _tabs.Count; i++)
                {
                    var t = _tabs[i];

                    string visibleTitle = $"{t.FileName}{(t.IsDirty ? "*" : string.Empty)}";
                    string tabTitle = $"{visibleTitle}##tab_{i}";

                    // Per-tab open flag for ImGui
                    bool open = t.IsOpen;

                    // BeginTabItem will flip 'open' to false when the user clicks X or MMB
                    if (ImGui.BeginTabItem(tabTitle, ref open))
                    {
                        _activeTabIndex = i;

                        Vector2 curAvailRegion = ImGui.GetContentRegionAvail();
                        t.Editor.Render($"##LuaTextEditor_{i}", curAvailRegion);
                        t.Code = t.Editor.Text;

                        ImGui.EndTabItem();
                    }

                    // Detect a close request this frame (X or middle-click):
                    if (t.IsOpen && !open)
                    {
                        bool wasDirty = t.IsDirty;

                        // Route through our unified close logic
                        RequestCloseTab(i);

                        // If it was dirty, keep it open until the user answers the popup
                        if (wasDirty)
                        {
                            open = true;
                        }
                    }

                    // Persist updated state for next frame
                    t.IsOpen = open;
                }
                if (ImGui.TabItemButton("+", ImGuiTabItemFlags.Trailing))
                {
                    NewFileTab();
                }

                ImGui.EndTabBar();

                if (_pendingCloseTabIndex >= 0 && _popupOpenRequested)
                {
                    ImGui.OpenPopup("Unsaved Changes");
                    _popupOpenRequested = false;
                }
            }



            // Unsaved-changes modal
            if (_pendingCloseTabIndex >= 0)
            {
                ImGui.SetNextWindowSize(new Vector2(420, 0), ImGuiCond.FirstUseEver);
                bool dummyOpen = true; // ImGui needs a ref bool, we manage close ourselves

                Vector2 winPos = ImGui.GetWindowPos();
                Vector2 winSize = ImGui.GetWindowSize();
                Vector2 popupSize = new(420, 150);

                ImGui.SetNextWindowPos(
                    winPos + (winSize - popupSize) * 0.5f,
                    ImGuiCond.Always);

                ImGui.SetNextWindowSize(popupSize, ImGuiCond.Always);

                var style = ImGui.GetStyle();

                style.Colors[(int)ImGuiCol.PopupBg] = new Vector4(0.10f, 0.12f, 0.16f, 0.98f);
                style.Colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0f, 0f, 0f, 0f);


                if (ImGui.BeginPopupModal("Unsaved Changes", ref dummyOpen, ImGuiWindowFlags.AlwaysAutoResize))
                {
                    int idx = _pendingCloseTabIndex;
                    if (idx < 0 || idx >= _tabs.Count)
                    {
                        _pendingCloseTabIndex = -1;
                        ImGui.CloseCurrentPopup();
                    }
                    else
                    {
                        var t = _tabs[idx];

                        ImGui.TextWrapped($"You have unsaved changes in '{t.FileName}'.");
                        ImGui.Spacing();
                        ImGui.TextWrapped("What would you like to do?");
                        ImGui.Dummy(new Vector2(0, 8));

                        // Save
                        if (ImGui.Button("Save", new Vector2(100, 0)))
                        {
                            if (SaveTab(idx))
                            {
                                CloseTab(idx);
                                _pendingCloseTabIndex = -1;
                                ImGui.CloseCurrentPopup();
                            }
                        }

                        ImGui.SameLine();

                        // Discard
                        if (ImGui.Button("Discard", new Vector2(100, 0)))
                        {
                            CloseTab(idx);
                            _pendingCloseTabIndex = -1;
                            ImGui.CloseCurrentPopup();
                        }

                        ImGui.SameLine();

                        // Cancel
                        if (ImGui.Button("Cancel", new Vector2(100, 0)))
                        {
                            _pendingCloseTabIndex = -1;
                            ImGui.CloseCurrentPopup();
                        }
                    }

                    ImGui.EndPopup();
                }
            }


            ImGui.EndChild();

            HitTestRegions.AddCurrentWindow();
        }

        // ---------------------------------------------------------------------
        // File ops
        // ---------------------------------------------------------------------

        private void RequestCloseTab(int index)
        {
            if (index < 0 || index >= _tabs.Count)
                return;

            var tab = _tabs[index];

            // Clean tab: close silently, but do it *outside* the tab-bar loop.
            if (!tab.IsDirty)
            {
                _pendingImmediateCloseIndex = index;
                return;
            }

            // Dirty -> ask the user via modal popup
            _pendingCloseTabIndex = index;
            _popupOpenRequested = true;
        }


        private void CloseTab(int index)
        {
            if (index < 0 || index >= _tabs.Count) { return; }

            _tabs.RemoveAt(index);

            if (_tabs.Count == 0)
            {
                _tabs.Add(CreateDefaultTab());
                _activeTabIndex = 0;
            }
            else
            {
                _activeTabIndex = Math.Clamp(_activeTabIndex, 0, _tabs.Count - 1);
            }
        }

        private void NewFileTab(bool webApplet = false)
        {
            if (!webApplet)
            {
                var tab = CreateDefaultTab();
                _tabs.Add(tab);
                _activeTabIndex = _tabs.Count - 1;
            }
            else
            {
                var tab = CreateWebTab();
                _tabs.Add(tab);
                _activeTabIndex = _tabs.Count - 1;
            }
        }

        private void OpenFileIntoNewTab()
        {
            try
            {
                using var ofd = new OpenFileDialog();
                ofd.Filter = "Lua files (*.lua)|*.lua|All files (*.*)|*.*";
                ofd.InitialDirectory = GetExternDir();
                ofd.Title = "Open Lua applet";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    string path = ofd.FileName;
                    string code = File.ReadAllText(path);

                    var tab = new EditorTab
                    {
                        Code = code,
                        LastSavedCode = code,
                        FilePath = path,
                        FileName = Path.GetFileName(path),
                        Status = $"Opened '{Path.GetFileName(path)}'."
                    };
                    tab.Editor.Text = code;

                    _tabs.Add(tab);
                    _activeTabIndex = _tabs.Count - 1;
                }
            }
            catch (Exception ex)
            {
                ActiveTab.Status = $"Error opening file: {ex.Message}";
            }
        }

        private bool SaveTab(int index)
        {
            if (index < 0 || index >= _tabs.Count)
                return false;

            var tab = _tabs[index];

            // No path yet? Behave like Save As
            if (string.IsNullOrWhiteSpace(tab.FilePath))
                return SaveTabAs(index);

            try
            {
                string text = tab.Editor.Text;

                File.WriteAllText(tab.FilePath, text);
                tab.LastSavedCode = text;
                tab.Code = text; // optional, if you still want to keep Code in sync
                tab.Status = $"Saved '{tab.FileName}'.";

                // Keep ExternApplets in sync
                string externDir = GetExternDir();
                string scriptPath = Path.Combine(externDir, tab.FileName);
                File.WriteAllText(scriptPath, text);

                StarboardMain.RegisterOrUpdateLuaApplet(scriptPath, selectAfterAdd: true);
                return true;
            }
            catch (Exception ex)
            {
                tab.Status = $"Error saving file: {ex.Message}";
                return false;
            }
        }


        private bool SaveTabAs(int index)
        {
            if (index < 0 || index >= _tabs.Count)
                return false;

            var tab = _tabs[index];

            try
            {
                using var sfd = new SaveFileDialog();
                sfd.Filter = "Lua files (*.lua)|*.lua|All files (*.*)|*.*";
                sfd.InitialDirectory = GetExternDir();
                sfd.Title = "Save Lua applet as";
                sfd.FileName = tab.FileName;

                if (sfd.ShowDialog() != DialogResult.OK)
                    return false;

                string path = sfd.FileName;
                if (string.IsNullOrWhiteSpace(Path.GetExtension(path)))
                    path += ".lua";

                string text = tab.Editor.Text;

                File.WriteAllText(path, text);

                tab.FilePath = path;
                tab.FileName = Path.GetFileName(path);
                tab.LastSavedCode = text;
                tab.Code = text; // optional
                tab.Status = $"Saved as '{tab.FileName}'.";

                string externDir = GetExternDir();
                string scriptPath = Path.Combine(externDir, tab.FileName);
                if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(scriptPath), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(path, scriptPath, overwrite: true);
                }

                StarboardMain.RegisterOrUpdateLuaApplet(scriptPath, selectAfterAdd: true);
                return true;
            }
            catch (Exception ex)
            {
                tab.Status = $"Error saving file: {ex.Message}";
                return false;
            }
        }



        private void SaveFile()
        {
            SaveTab(_activeTabIndex);
        }

        private void SaveFileAs()
        {
            SaveTabAs(_activeTabIndex);
        }

        // ---------------------------------------------------------------------
        // Validation / "Run" check (no drawing)
        // ---------------------------------------------------------------------

        private void ValidateCurrentCode()
        {
            var tab = ActiveTab;
            tab.Code = tab.Editor.Text;

            try
            {
                var script = LuaEngine.CreateScript();

                UserData.RegisterType<LuaUiApi>();
                script.Globals["ui"] = UserData.Create(new LuaUiApi());

                script.DoString(tab.Code);

                var appVal = script.Globals.Get("app");
                if (appVal.Type != DataType.Table)
                    throw new InvalidOperationException("Lua applet must define global 'app' table.");

                var appTable = appVal.Table;

                string? id = CallStringFunc(script, appTable, "id");
                if (string.IsNullOrWhiteSpace(id))
                    throw new InvalidOperationException("app.id() must return a non-empty string.");

                string? name = CallStringFunc(script, appTable, "name");

                tab.Status = $"OK! All checks passed for: '{(name)}'.";
            }
            catch (SyntaxErrorException ex)
            {
                tab.Status = $"Syntax error: {ex.DecoratedMessage ?? ex.Message}";
            }
            catch (ScriptRuntimeException ex)
            {
                tab.Status = $"Runtime error: {ex.DecoratedMessage ?? ex.Message}";
            }
            catch (Exception ex)
            {
                tab.Status = $"Validation error: {ex.Message}";
            }
        }


        private static string? CallStringFunc(Script script, Table appTable, string funcName)
        {
            var fn = appTable.Get(funcName);
            if (fn.Type == DataType.Nil || fn.Type == DataType.Void)
                return null;

            if (fn.Type != DataType.Function)
                throw new InvalidOperationException($"app.{funcName} must be a function.");

            var result = script.Call(fn);

            return result.Type switch
            {
                DataType.String => result.String,
                DataType.Nil or DataType.Void => null,
                _ => throw new InvalidOperationException($"app.{funcName} must return a string or nil, got {result.Type}.")
            };
        }
    }
}

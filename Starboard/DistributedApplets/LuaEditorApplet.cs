using ImGuiNET;
using Overlay_Renderer.Methods;
using System.Numerics;
using MoonSharp.Interpreter;
using Starboard.Lua;
using Starboard.UI;
using Windows.Networking.Sockets;
using System.IO;
using System.Reflection;

namespace Starboard.DistributedApplets
{
    internal sealed class LuaEditorApplet : IStarboardApplet
    {
        public string Id => "starboard.lua_editor";
        public string DisplayName => "Applet Editor";
        public string? FaviconUrl => "https://cdn-icons-png.flaticon.com/512/5105/5105701.png";

        public bool UsesWebView => false;

        private static string DefaultTemplate = String.Empty;
        private static string WebTemplate = String.Empty;

        private int _activeTabIndex = 0;
        private int _pendingCloseTabIndex = -1;
        private int _pendingImmediateCloseIndex = -1;

        private bool _popupOpenRequested = false;

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
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("Starboard.ExampleApplets.ExampleLuaApplet.lua");
            using var reader = new StreamReader(stream!);
            DefaultTemplate = reader.ReadToEnd();

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
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("Starboard.ExampleApplets.ExampleLuaWebApplet.lua");
            using var reader = new StreamReader(stream!);
            WebTemplate = reader.ReadToEnd();

            var tab = new EditorTab();
            tab.Code = WebTemplate;
            tab.LastSavedCode = tab.Code;
            tab.FilePath = string.Empty;
            tab.FileName = "Untitled.lua";
            tab.Status = "Ready.";
            tab.Editor.Text = tab.Code;

            return tab;
        }

        private static string GetExternDir()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Starboard",
                "ExternApplets");

            Directory.CreateDirectory(dir);
            return dir;
        }

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

            if (ImGui.BeginMenuBar())
            {
                if (ImGui.BeginMenu("File"))
                {
                    bool hasTab = _tabs.Count > 0;

                    if (ImGui.BeginMenu("New"))
                    {
                        if (ImGui.MenuItem("New Applet", "Ctrl+N", false, hasTab))
                        {
                            NewFileTab();
                        }

                        if (ImGui.MenuItem("New Web Applet", "Ctrl+Shift+N", false, hasTab))
                        {
                            NewFileTab(true);
                        }

                        ImGui.EndMenu();
                    }



                    if (ImGui.MenuItem("Open", "Ctrl+O", false, hasTab))
                    {
                        OpenFileIntoNewTab();
                    }


                    if (ImGui.MenuItem("Save", "Ctrl+S", false, hasTab))
                    {
                        SaveFile();
                    }

                    if (ImGui.MenuItem("Save As", "Ctrl+Shift+S", false, hasTab))
                    {
                        SaveFileAs();
                    }

                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Tools"))
                {
                    bool hasTab = _tabs.Count > 0;
                    if (ImGui.MenuItem("Method List", "Ctrl+Space", false, hasTab))
                    {
                        ActiveTab.Editor.OpenCompletion();
                    }

                    if (ImGui.MenuItem("Validate", null, false, hasTab))
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

            if (ImGui.BeginTabBar("EditorTabs",
                ImGuiTabBarFlags.Reorderable | ImGuiTabBarFlags.AutoSelectNewTabs))
            {
                for (int i = 0; i < _tabs.Count; i++)
                {
                    var t = _tabs[i];

                    string visibleTitle = $"{t.FileName}{(t.IsDirty ? "*" : string.Empty)}";
                    string tabTitle = $"{visibleTitle}##tab_{i}";

                    bool open = t.IsOpen;

                    if (ImGui.BeginTabItem(tabTitle, ref open))
                    {
                        _activeTabIndex = i;

                        Vector2 curAvailRegion = ImGui.GetContentRegionAvail();
                        t.Editor.Render($"##LuaTextEditor_{i}", curAvailRegion);
                        t.Code = t.Editor.Text;

                        ImGui.EndTabItem();
                    }

                    if (t.IsOpen && !open)
                    {
                        bool wasDirty = t.IsDirty;

                        RequestCloseTab(i);

                        if (wasDirty)
                        {
                            open = true;
                        }
                    }

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



            if (_pendingCloseTabIndex >= 0)
            {
                ImGui.SetNextWindowSize(new Vector2(420, 0), ImGuiCond.FirstUseEver);
                bool dummyOpen = true;

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

        private void RequestCloseTab(int index)
        {
            if (index < 0 || index >= _tabs.Count)
                return;

            var tab = _tabs[index];

            if (!tab.IsDirty)
            {
                _pendingImmediateCloseIndex = index;
                return;
            }

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

            if (string.IsNullOrWhiteSpace(tab.FilePath))
                return SaveTabAs(index);

            try
            {
                string text = tab.Editor.Text;

                File.WriteAllText(tab.FilePath, text);
                tab.LastSavedCode = text;
                tab.Code = text;
                tab.Status = $"Saved '{tab.FileName}'.";

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

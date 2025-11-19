using ImGuiNET;
using Overlay_Renderer.Methods;
using System.Numerics;
using MoonSharp.Interpreter;
using Starboard.Lua;

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

        private static ImFontPtr _orbiRegFont;



        // --- Code + file state ------------------------------------------------

        // Current buffer in the editor
        private string _currentCode = @"app = {}

function app.id()
    return ""user.new_applet""
end

function app.name()
    return ""New Applet""
end

function app.draw(dt, w, h)
    ui.text(""Hello from a Lua applet created in the editor!"")
end
";

        // Last-saved buffer (for dirty tracking)
        private string _lastSavedCode = string.Empty;

        // Path on disk of the current file (if any)
        private string _currentFilePath = string.Empty;

        // Just the name for UI purposes (tab title)
        private string _currentFileName = "Untitled.lua";

        // Status line at the bottom of the editor
        private string _status = "Idle";

        private bool IsDirty => _currentCode != _lastSavedCode;

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
            _editor.Text =_currentCode;
            _lastSavedCode = _currentCode;
            _status = "Ready.";
        }

        public void Draw(float dt, Vector2 availableSize)
        {
            _lastAvailableSize = availableSize;

            if (!ImGui.BeginChild("Editor", availableSize, ImGuiChildFlags.Borders, ImGuiWindowFlags.MenuBar))
                return;

            // --- Menu bar -----------------------------------------------------
            if (ImGui.BeginMenuBar())
            {
                if (ImGui.BeginMenu("File"))
                {
                    if (ImGui.MenuItem("New"))
                    {
                        NewFile();
                    }

                    if (ImGui.MenuItem("Open"))
                    {
                        OpenFile();
                    }

                    if (ImGui.MenuItem("Save", enabled: true))
                    {
                        SaveFile();
                    }

                    if (ImGui.MenuItem("Save As"))
                    {
                        SaveFileAs();
                    }

                    if (ImGui.MenuItem("Validate"))
                    {
                        ValidateCurrentCode();
                    }

                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu(_status))
                {
                    ImGui.EndMenu();
                }

                ImGui.EndMenuBar();
            }

            // --- Editor tabs --------------------------------------------------
            if (ImGui.BeginTabBar("EditorTabs", ImGuiTabBarFlags.Reorderable))
            {
                // Tab title: "Untitled.lua" or "foo.lua*", with * if unsaved
                string tabTitle = $"{_currentFileName}{(IsDirty ? "*" : string.Empty)}";

                if (ImGui.BeginTabItem(tabTitle))
                {
                    Vector2 curAvailRegion = ImGui.GetContentRegionAvail();
                    _editor.Render("##LuaTextEditor", curAvailRegion);
                    _currentCode = _editor.Text;
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            ImGui.EndChild();

            HitTestRegions.AddCurrentWindow();
        }

        // ---------------------------------------------------------------------
        // File ops
        // ---------------------------------------------------------------------

        private void NewFile()
        {
            // In future: optional "do you want to save changes?" prompt
            _currentCode = @"app = {}

function app.id()
    return ""user.new_applet""
end

function app.name()
    return ""New Applet""
end

function app.draw(dt, w, h)
    ui.text(""Hello from a Lua applet created in the editor!"")
end
";
            _editor.Text = _currentCode;
            _lastSavedCode = _currentCode;
            _currentFilePath = string.Empty;
            _currentFileName = "Untitled.lua";
            _status = "New file created.";
        }

        private void OpenFile()
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

                    _currentCode = code;
                    _editor.Text = _currentCode;
                    _lastSavedCode = code;
                    _currentFilePath = path;
                    _currentFileName = Path.GetFileName(path);
                    _status = $"Opened '{_currentFileName}'.";
                }
            }
            catch (Exception ex)
            {
                _status = $"Error opening file: {ex.Message}";
            }
        }

        private void SaveFile()
        {
            if (string.IsNullOrWhiteSpace(_currentFilePath))
            {
                SaveFileAs();
                return;
            }

            try
            {
                _currentCode = _editor.Text;
                File.WriteAllText(_currentFilePath, _currentCode);
                _lastSavedCode = _currentCode;
                _status = $"Saved '{_currentFileName}'.";
            }
            catch (Exception ex)
            {
                _status = $"Error saving file: {ex.Message}";
            }
        }

        private void SaveFileAs()
        {
            try
            {
                using var sfd = new SaveFileDialog();
                sfd.Filter = "Lua files (*.lua)|*.lua|All files (*.*)|*.*";
                sfd.InitialDirectory = GetExternDir();
                sfd.Title = "Save Lua applet as";
                sfd.FileName = _currentFileName;

                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    string path = sfd.FileName;

                    // Ensure .lua extension
                    if (string.IsNullOrWhiteSpace(Path.GetExtension(path)))
                    {
                        path += ".lua";
                    }

                    _currentCode = _editor.Text;
                    File.WriteAllText(path, _currentCode);

                    _currentFilePath = path;
                    _currentFileName = Path.GetFileName(path);
                    _lastSavedCode = _currentCode;
                    _status = $"Saved as '{_currentFileName}'.";
                }
            }
            catch (Exception ex)
            {
                _status = $"Error saving file: {ex.Message}";
            }
        }

        // ---------------------------------------------------------------------
        // Validation / "Run" check (no drawing)
        // ---------------------------------------------------------------------

        private void ValidateCurrentCode()
        {
            _currentCode = _editor.Text;
            try
            {
                // Create a sandboxed Lua script same way LuaApplet does
                var script = LuaEngine.CreateScript();

                // Make sure ui.* exists just like in the real applet environment
                UserData.RegisterType<LuaUiApi>();
                script.Globals["ui"] = UserData.Create(new LuaUiApi());

                // Run the current buffer
                script.DoString(_currentCode);

                var appVal = script.Globals.Get("app");
                if (appVal.Type != DataType.Table)
                    throw new InvalidOperationException("Lua applet must define global 'app' table.");

                var appTable = appVal.Table;

                string? id = CallStringFunc(script, appTable, "id");
                if (string.IsNullOrWhiteSpace(id))
                    throw new InvalidOperationException("app.id() must return a non-empty string.");

                string? name = CallStringFunc(script, appTable, "name");

                _status = $"OK – app.id() = '{id}', app.name() = '{(name ?? id)}'.";
            }
            catch (SyntaxErrorException ex)
            {
                // Syntax error with line info
                _status = $"Syntax error: {ex.DecoratedMessage ?? ex.Message}";
            }
            catch (ScriptRuntimeException ex)
            {
                _status = $"Runtime error: {ex.DecoratedMessage ?? ex.Message}";
            }
            catch (Exception ex)
            {
                _status = $"Validation error: {ex.Message}";
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

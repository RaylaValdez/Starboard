using System.Numerics;
using System.Text;
using System.IO;
using System.Linq;
using ImGuiNET;
using System.Reflection;
using Starboard.Lua;

namespace Starboard
{
    internal sealed class TextEditor
    {
        // ---- Public API -----------------------------------------------------

        public string Text
        {
            get => GetText();
            set => SetText(value ?? string.Empty);
        }

        /// <summary>Render the editor inside the current ImGui window.</summary>
        public void Render(string id, Vector2 size)
        {
            unsafe
            {
                if (Program._jBMReg.NativePtr != null)
                    ImGui.PushFont(Program._jBMReg.NativePtr);
            }

            // VSCode-like editor background (#1E1E1E)
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.117f, 0.117f, 0.117f, 1.0f));

            if (!ImGui.BeginChild(id, size, ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar))
            {
                ImGui.PopStyleColor();
                unsafe
                {
                    if (Program._jBMReg.NativePtr != null)
                        ImGui.PopFont();
                }
                return;
            }

            var io = ImGui.GetIO();
            bool focused = ImGui.IsWindowFocused();
            _hasFocus = focused;

            if (focused)
                HandleKeyboard(io);

            DrawContents();

            ImGui.EndChild();
            ImGui.PopStyleColor();

            unsafe
            {
                if (Program._jBMReg.NativePtr != null)
                    ImGui.PopFont();
            }
        }

        // ---- Internal data --------------------------------------------------

        private readonly List<string> _lines = new() { string.Empty };

        private int _cursorLine;
        private int _cursorColumn;

        // Focus
        private bool _hasFocus;

        // Mouse selection state
        private bool _isSelecting;
        private int _selStartLine = -1;
        private int _selStartColumn = -1;
        private int _selEndLine = -1;
        private int _selEndColumn = -1;

        private bool HasSelection =>
            _selStartLine >= 0 &&
            (_selStartLine != _selEndLine || _selStartColumn != _selEndColumn);

        // How far the visible Star Citizen cursor tip is from the actual ImGui mouse point
        // Tweak these values until clicking feels right
        private readonly Vector2 _mouseHitOffset = new Vector2(-8f, 0f);

        private enum TokenType
        {
            Default,
            Keyword,
            Builtin,
            Method,
            Number,
            String,
            Comment,
            Identifier,
            Operator,
            Field
        }

        private struct Token
        {
            public int Line;
            public int Start;
            public int Length;
            public TokenType Type;
        }

        // Full editor snapshot for undo/redo
        private struct EditorState
        {
            public List<string> Lines;
            public int CursorLine;
            public int CursorColumn;
            public int SelStartLine;
            public int SelStartColumn;
            public int SelEndLine;
            public int SelEndColumn;
        }

        private readonly List<Token> _tokens = new();

        // Undo / redo stacks
        private readonly Stack<EditorState> _undoStack = new();
        private readonly Stack<EditorState> _redoStack = new();
        private bool _suppressUndoCapture;

        private static readonly HashSet<string> LuaKeywords = new(StringComparer.Ordinal)
        {
            "and","break","do","else","elseif","end","false","for","function","goto",
            "if","in","local","nil","not","or","repeat","return","then","true","until",
            "while"
        };

        private static readonly HashSet<string> LuaBuiltins = new(StringComparer.Ordinal)
        {
            "assert","collectgarbage","dofile","error","getmetatable","ipairs","load",
            "loadfile","next","pairs","pcall","print","rawequal","rawget","rawlen",
            "rawset","require","select","setmetatable","tonumber","tostring","type",
            "xpcall","coroutine","string","table","math","io","ui","os","debug","package"
        };

        private static readonly HashSet<string> LuaAppMethods = new(StringComparer.Ordinal)
        {
            "id",
            "name",
            "init",
            "draw"
        };

        private static readonly HashSet<string> LuaUiMethods = BuildLuaUiMethods();

        private static HashSet<string> BuildLuaUiMethods()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);

            var apiType = typeof(LuaUiApi);
            const BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;

            foreach (var m in apiType.GetMethods(flags))
            {
                if (m.IsSpecialName)
                    continue;

                set.Add(m.Name);
            }

            return set;
        }

        // VSCode Dark+ colours
        private readonly Vector4 _colDefault = new(0.831f, 0.831f, 0.831f, 1.0f); // #D4D4D4
        private readonly Vector4 _colKeyword = new(0.773f, 0.525f, 0.753f, 1.0f); // #C586C0
        private readonly Vector4 _colBuiltin = new(0.831f, 0.831f, 0.831f, 1.0f); // default
        private readonly Vector4 _colMethod = new(0.863f, 0.863f, 0.667f, 1.0f); // #DCDCAA
        private readonly Vector4 _colNumber = new(0.710f, 0.808f, 0.659f, 1.0f); // #B5CEA8
        private readonly Vector4 _colString = new(0.808f, 0.569f, 0.471f, 1.0f); // #CE9178
        private readonly Vector4 _colComment = new(0.416f, 0.600f, 0.333f, 1.0f); // #6A9955
        private readonly Vector4 _colOperator = new(0.875f, 0.875f, 0.875f, 1.0f); // near default
        private readonly Vector4 _colField = new(0.612f, 0.863f, 0.996f, 1.0f); // #9CDCFE

        // VSCode-like bracket pair colours
        private readonly Vector4[] _bracketColors =
        {
            new(1f, 0.84f, 0f, 1.0f),      // Yellow
            new(0.85f, 0.43f, 0.83f, 1.0f),// Pink
            new(0.09f, 0.62f, 1f, 1.0f),   // Blue
        };

        // VSCode selection colour #264F78
        private readonly Vector4 _colSelection = new(0.149f, 0.310f, 0.471f, 1.0f);

        // VSCode caret colour #AEAFAD
        private readonly Vector4 _colCaret = new(0.682f, 0.686f, 0.678f, 1.0f);

        // Indent guide colour (#404040-ish)
        private readonly Vector4 _colIndentGuide = new(0.251f, 0.251f, 0.251f, 0.6f);

        private float _lineHeight;
        private float _charAdvance;

        private const int TabSize = 4;

        // ---------------------------------------------------------------------
        // Completion popup (Intellisense-lite)
        // ---------------------------------------------------------------------

        private sealed class CompletionItem
        {
            public string Name = "";       // e.g. "ui.text"
            public string Signature = "";  // e.g. "text(string str)"
            public string Summary = "";
            public string InsertText = ""; // e.g. "ui.text()"
        }

        private bool _completionInitialized = false;
        private readonly List<CompletionItem> _completionItems = new();
        private readonly List<CompletionItem> _completionFiltered = new();
        private bool _completionOpen = false;
        private string _completionFilter = "";
        private int _completionSelectedIndex = 0;

        // Last known caret screen position (bottom-left of caret)
        private Vector2 _caretScreenPos;


        // ---------------------------------------------------------------------
        // Text storage helpers
        // ---------------------------------------------------------------------

        private string GetText()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _lines.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(_lines[i]);
            }
            return sb.ToString();
        }

        private void SetText(string text)
        {
            _lines.Clear();
            using (var reader = new StringReader(text))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                    _lines.Add(line);
            }
            if (_lines.Count == 0)
                _lines.Add(string.Empty);

            _cursorLine = 0;
            _cursorColumn = 0;
            ClearSelection();

            ColorizeAll();

            // Reset undo history to this loaded state
            _undoStack.Clear();
            _redoStack.Clear();
            _undoStack.Push(CaptureState());
        }

        private void EnsureCursorInBounds()
        {
            if (_cursorLine < 0) _cursorLine = 0;
            if (_cursorLine >= _lines.Count) _cursorLine = _lines.Count - 1;
            var line = _lines[_cursorLine];
            if (_cursorColumn < 0) _cursorColumn = 0;
            if (_cursorColumn > line.Length) _cursorColumn = line.Length;
        }

        // ---------------------------------------------------------------------
        // Undo / Redo helpers
        // ---------------------------------------------------------------------

        private EditorState CaptureState()
        {
            return new EditorState
            {
                Lines = new List<string>(_lines),
                CursorLine = _cursorLine,
                CursorColumn = _cursorColumn,
                SelStartLine = _selStartLine,
                SelStartColumn = _selStartColumn,
                SelEndLine = _selEndLine,
                SelEndColumn = _selEndColumn
            };
        }

        private void RestoreState(EditorState state)
        {
            _lines.Clear();
            _lines.AddRange(state.Lines);

            _cursorLine = state.CursorLine;
            _cursorColumn = state.CursorColumn;
            _selStartLine = state.SelStartLine;
            _selStartColumn = state.SelStartColumn;
            _selEndLine = state.SelEndLine;
            _selEndColumn = state.SelEndColumn;

            ColorizeAll();
        }

        private void PushUndoState()
        {
            if (_suppressUndoCapture)
                return;

            _undoStack.Push(CaptureState());
            _redoStack.Clear();
        }

        private void Undo()
        {
            if (_undoStack.Count == 0)
                return;

            var current = CaptureState();
            var prev = _undoStack.Pop();
            _redoStack.Push(current);
            RestoreState(prev);
        }

        private void Redo()
        {
            if (_redoStack.Count == 0)
                return;

            var current = CaptureState();
            var next = _redoStack.Pop();
            _undoStack.Push(current);
            RestoreState(next);
        }

        // ---------------------------------------------------------------------
        // Selection helpers
        // ---------------------------------------------------------------------

        private void DeleteSelection()
        {
            if (!HasSelection)
                return;

            GetSelectionBounds(
                out int startLine, out int startCol,
                out int endLine, out int endCol);

            // Single-line selection
            if (startLine == endLine)
            {
                string line = _lines[startLine];
                startCol = Math.Clamp(startCol, 0, line.Length);
                endCol = Math.Clamp(endCol, 0, line.Length);

                if (endCol > startCol)
                    _lines[startLine] = line.Remove(startCol, endCol - startCol);
            }
            else
            {
                // Multi-line: keep prefix of first line + suffix of last line
                string first = _lines[startLine];
                string last = _lines[endLine];

                startCol = Math.Clamp(startCol, 0, first.Length);
                endCol = Math.Clamp(endCol, 0, last.Length);

                string merged = first.Substring(0, startCol) +
                                last.Substring(endCol);

                _lines[startLine] = merged;

                // Remove the lines in between and the old last line
                for (int i = endLine; i > startLine; i--)
                    _lines.RemoveAt(i);
            }

            _cursorLine = startLine;
            _cursorColumn = Math.Clamp(startCol, 0, _lines[_cursorLine].Length);

            ClearSelection();
            ColorizeAll();
        }

        private void ClearSelection()
        {
            _selStartLine = _selStartColumn = _selEndLine = _selEndColumn = -1;
            _isSelecting = false;
        }

        private void GetSelectionBounds(
            out int startLine, out int startCol,
            out int endLine, out int endCol)
        {
            startLine = _selStartLine;
            startCol = _selStartColumn;
            endLine = _selEndLine;
            endCol = _selEndColumn;

            if (!HasSelection)
                return;

            if (startLine > endLine ||
               startLine == endLine && startCol > endCol)
            {
                (startLine, endLine) = (endLine, startLine);
                (startCol, endCol) = (endCol, startCol);
            }
        }

        // --- Selection-based operations -------------------------------------------

        private void SelectAll()
        {
            if (_lines.Count == 0)
                return;

            _selStartLine = 0;
            _selStartColumn = 0;

            int lastLine = _lines.Count - 1;
            _selEndLine = lastLine;
            _selEndColumn = _lines[lastLine].Length;

            _isSelecting = false;

            _cursorLine = lastLine;
            _cursorColumn = _selEndColumn;
        }

        private string GetSelectedText()
        {
            if (!HasSelection)
                return string.Empty;

            GetSelectionBounds(
                out int startLine, out int startCol,
                out int endLine, out int endCol);

            var sb = new StringBuilder();

            if (startLine == endLine)
            {
                string line = _lines[startLine];
                startCol = Math.Clamp(startCol, 0, line.Length);
                endCol = Math.Clamp(endCol, 0, line.Length);

                if (endCol > startCol)
                    sb.Append(line.Substring(startCol, endCol - startCol));
            }
            else
            {
                // first line
                string first = _lines[startLine];
                startCol = Math.Clamp(startCol, 0, first.Length);
                sb.Append(first.Substring(startCol));
                sb.Append('\n');

                // middle lines
                for (int i = startLine + 1; i < endLine; i++)
                {
                    sb.Append(_lines[i]);
                    sb.Append('\n');
                }

                // last line
                string last = _lines[endLine];
                endCol = Math.Clamp(endCol, 0, last.Length);
                sb.Append(last.Substring(0, endCol));
            }

            return sb.ToString();
        }

        private void CopySelectionToClipboard()
        {
            if (!HasSelection)
                return;

            string text = GetSelectedText();
            if (!string.IsNullOrEmpty(text))
                ImGui.SetClipboardText(text);
        }

        private void CutSelectionToClipboard()
        {
            if (!HasSelection)
                return;

            PushUndoState();
            CopySelectionToClipboard();
            DeleteSelection();
        }

        private static string NormalizeNewlines(string? s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;

            // ImGui / OS clipboards love to mix \r\n and \r
            return s.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private void PasteFromClipboard()
        {
            string clip = NormalizeNewlines(ImGui.GetClipboardText());
            if (string.IsNullOrEmpty(clip))
                return;

            PushUndoState();

            if (HasSelection)
                DeleteSelection();

            _suppressUndoCapture = true;
            foreach (char c in clip)
                InsertChar(c);
            _suppressUndoCapture = false;
        }

        private void SetCaretFromMouse(Vector2 mousePos, Vector2 origin, float lineSpacing)
        {
            if (_lines.Count == 0)
            {
                _cursorLine = 0;
                _cursorColumn = 0;
                return;
            }

            // Mouse position relative to top-left of text area
            Vector2 local = mousePos - origin + _mouseHitOffset;

            // Pick line
            int line = (int)Math.Floor(local.Y / lineSpacing);
            if (line < 0) line = 0;
            if (line >= _lines.Count) line = _lines.Count - 1;

            string text = _lines[line];

            // Target X inside the line
            float targetX = local.X;
            if (targetX < 0) targetX = 0;

            // Binary search for the smallest column whose width >= targetX
            int lo = 0;
            int hi = text.Length;

            while (lo < hi)
            {
                int mid = (lo + hi) / 2;

                // Width of substring [0, mid)
                string substr = text.Substring(0, mid);
                float w = ImGui.CalcTextSize(substr).X;

                if (w < targetX)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            _cursorLine = line;
            _cursorColumn = lo;
        }

        private void EnsureCompletionItems()
        {
            if (_completionInitialized)
                return;

            _completionInitialized = true;

            var type = typeof(LuaUiApi);
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

            foreach (var m in methods)
            {
                if (m.IsSpecialName)
                    continue;

                string name = m.Name; // e.g. "text"

                var pars = m.GetParameters();
                string sig = $"{name}(" +
                             string.Join(", ",
                                 pars.Select(p => $"{p.ParameterType.Name} {p.Name}")) +
                             ")";

                var summary = BuildSummary(name, pars);

                var item = new CompletionItem
                {
                    Name = $"ui.{name}",
                    Signature = sig,         // keep for debugging, filtering, whatever
                    Summary = summary,       // NEW
                    InsertText = $"ui.{name}()"
                };

                _completionItems.Add(item);

            }

            _completionFiltered.Clear();
            _completionFiltered.AddRange(_completionItems);
        }

        private static string BuildSummary(string name, ParameterInfo[] pars)
        {
            if (pars.Length == 0)
                return $"{name}()";

            // Turn `float value, string label` into `value, label`
            var simple = string.Join(", ", pars.Select(p => p.Name));
            return $"{name}({simple})";
        }

        private void ApplyCompletionFilter()
        {
            _completionFiltered.Clear();

            string f = _completionFilter?.Trim().ToLowerInvariant() ?? "";

            if (string.IsNullOrEmpty(f))
            {
                _completionFiltered.AddRange(_completionItems);
            }
            else
            {
                foreach (var item in _completionItems)
                {
                    if (item.Name.ToLowerInvariant().Contains(f) ||
                        item.Signature.ToLowerInvariant().Contains(f))
                    {
                        _completionFiltered.Add(item);
                    }
                }
            }

            _completionSelectedIndex = 0;
        }

        private void InsertCompletion(CompletionItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.InsertText))
                return;

            // Undo-friendly insertion at caret
            PushUndoState();

            if (HasSelection)
                DeleteSelection();

            _suppressUndoCapture = true;
            foreach (char c in item.InsertText)
                InsertChar(c);
            _suppressUndoCapture = false;
        }

        private void HandleCompletionPopup()
        {
            EnsureCompletionItems();

            var io = ImGui.GetIO();

            // Open popup on Ctrl+Space while editor has focus
            if (_hasFocus && io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.Space))
            {
                Backspace();
                _completionOpen = true;
                _completionFilter = "";
                ApplyCompletionFilter();
            }

            if (!_completionOpen)
                return;

            // Position popup near caret
            Vector2 popupPos = _caretScreenPos + new Vector2(0f, 4f);
            ImGui.SetNextWindowPos(popupPos, ImGuiCond.Appearing);
            ImGui.SetNextWindowBgAlpha(0.95f);

            bool openFlag = _completionOpen;
            if (!ImGui.Begin("##LuaCompletion", ref openFlag,
                ImGuiWindowFlags.NoTitleBar |
                ImGuiWindowFlags.AlwaysAutoResize |
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoNavInputs))
            {
                ImGui.End();
                _completionOpen = openFlag;
                return;
            }

            _completionOpen = openFlag;

            // Click outside editor + popup -> close
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                bool popupHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows);
                if (!popupHovered && !_hasFocus)
                {
                    _completionOpen = false;
                    ImGui.End();
                    return;
                }
            }

            // Filter box
            ImGui.InputText("Filter", ref _completionFilter, 128);
            if (ImGui.IsItemEdited())
                ApplyCompletionFilter();

            ImGui.Separator();

            // Keyboard nav inside completion window
            if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
            {
                if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                {
                    _completionOpen = false;
                    ImGui.End();
                    return;
                }

                if (_completionFiltered.Count > 0)
                {
                    if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
                        _completionSelectedIndex = Math.Min(_completionSelectedIndex + 1, _completionFiltered.Count - 1);

                    if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
                        _completionSelectedIndex = Math.Max(_completionSelectedIndex - 1, 0);

                    if (ImGui.IsKeyPressed(ImGuiKey.Enter))
                    {
                        InsertCompletion(_completionFiltered[_completionSelectedIndex]);
                        _completionOpen = false;
                        ImGui.End();
                        return;
                    }
                }
            }

            // List
            Vector2 listSize = new Vector2(450f, 260f);
            if (ImGui.BeginChild("##LuaCompletionList", listSize, ImGuiChildFlags.None))
            {
                for (int i = 0; i < _completionFiltered.Count; i++)
                {
                    var item = _completionFiltered[i];
                    bool selected = i == _completionSelectedIndex;

                    string label = $"{item.Name}  {item.Summary}";

                    if (ImGui.Selectable(label, selected))
                    {
                        InsertCompletion(item);
                        _completionOpen = false;
                        ImGui.EndChild();
                        ImGui.End();
                        return;
                    }

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }
            }
            ImGui.EndChild();

            ImGui.End();
        }


        // ---------------------------------------------------------------------
        // Editing operations
        // ---------------------------------------------------------------------

        private void InsertChar(char c)
        {
            if (!_suppressUndoCapture)
                PushUndoState();

            if (HasSelection)
                DeleteSelection();

            if (_cursorLine < 0 || _cursorLine >= _lines.Count)
                return;

            if (c == '\n')
            {
                string line = _lines[_cursorLine];
                _cursorColumn = Math.Clamp(_cursorColumn, 0, line.Length);

                string left = line[.._cursorColumn];
                string right = line[_cursorColumn..];

                _lines[_cursorLine] = left;
                _lines.Insert(_cursorLine + 1, right);

                _cursorLine++;
                _cursorColumn = 0;

                ColorizeAll();
                return;
            }

            string curLine = _lines[_cursorLine];
            _cursorColumn = Math.Clamp(_cursorColumn, 0, curLine.Length);

            if (c == '\t')
            {
                int spaces = TabSize - _cursorColumn % TabSize;
                if (spaces <= 0) spaces = TabSize;

                string insert = new string(' ', spaces);
                curLine = curLine.Insert(_cursorColumn, insert);
                _cursorColumn += insert.Length;
            }
            else
            {
                curLine = curLine.Insert(_cursorColumn, c.ToString());
                _cursorColumn++;
            }

            _lines[_cursorLine] = curLine;
            ColorizeLine(_cursorLine);
        }

        private void Backspace()
        {
            if (_lines.Count == 0)
                return;

            if (_cursorLine < 0 || _cursorLine >= _lines.Count)
                return;

            PushUndoState();

            if (HasSelection)
            {
                DeleteSelection();
                return;
            }

            ClearSelection();

            if (_cursorColumn > 0)
            {
                var line = _lines[_cursorLine];
                _cursorColumn = Math.Clamp(_cursorColumn, 1, line.Length);

                line = line.Remove(_cursorColumn - 1, 1);
                _lines[_cursorLine] = line;
                _cursorColumn--;

                ColorizeLine(_cursorLine);
                return;
            }

            if (_cursorLine == 0)
                return;

            var prev = _lines[_cursorLine - 1];
            var cur = _lines[_cursorLine];

            int newCol = prev.Length;
            _lines[_cursorLine - 1] = prev + cur;
            _lines.RemoveAt(_cursorLine);

            _cursorLine--;
            _cursorColumn = newCol;

            ColorizeAll();
        }

        private void Delete()
        {
            if (_lines.Count == 0)
                return;

            if (_cursorLine < 0 || _cursorLine >= _lines.Count)
                return;

            PushUndoState();

            if (HasSelection)
            {
                DeleteSelection();
                return;
            }

            ClearSelection();

            var line = _lines[_cursorLine];
            _cursorColumn = Math.Clamp(_cursorColumn, 0, line.Length);

            if (_cursorColumn < line.Length)
            {
                line = line.Remove(_cursorColumn, 1);
                _lines[_cursorLine] = line;

                ColorizeLine(_cursorLine);
                return;
            }

            if (_cursorLine + 1 >= _lines.Count)
                return;

            var next = _lines[_cursorLine + 1];
            _lines[_cursorLine] = line + next;
            _lines.RemoveAt(_cursorLine + 1);

            ColorizeAll();
        }

        private void MoveLeft()
        {
            ClearSelection();

            if (_cursorColumn > 0)
                _cursorColumn--;
            else if (_cursorLine > 0)
            {
                _cursorLine--;
                _cursorColumn = _lines[_cursorLine].Length;
            }
        }

        private void MoveRight()
        {
            ClearSelection();

            var line = _lines[_cursorLine];
            if (_cursorColumn < line.Length)
                _cursorColumn++;
            else if (_cursorLine + 1 < _lines.Count)
            {
                _cursorLine++;
                _cursorColumn = 0;
            }
        }

        private void MoveUp()
        {
            ClearSelection();

            if (_cursorLine > 0)
            {
                _cursorLine--;
                _cursorColumn = Math.Clamp(_cursorColumn, 0, _lines[_cursorLine].Length);
            }
        }

        private void MoveDown()
        {
            ClearSelection();

            if (_cursorLine + 1 < _lines.Count)
            {
                _cursorLine++;
                _cursorColumn = Math.Clamp(_cursorColumn, 0, _lines[_cursorLine].Length);
            }
        }

        private void MoveHome()
        {
            ClearSelection();
            _cursorColumn = 0;
        }

        private void MoveEnd()
        {
            ClearSelection();
            _cursorColumn = _lines[_cursorLine].Length;
        }

        // ---------------------------------------------------------------------
        // Keyboard handling
        // ---------------------------------------------------------------------

        private void HandleKeyboard(ImGuiIOPtr io)
        {
            if (_completionOpen)
                return;

            bool ctrl = io.KeyCtrl;
            bool shift = io.KeyShift;

            // Ctrl combos (no repeat)
            if (ctrl)
            {
                if (ImGui.IsKeyPressed(ImGuiKey.Z, false))
                {
                    if (shift)
                        Redo();      // Ctrl+Shift+Z
                    else
                        Undo();      // Ctrl+Z
                }

                if (ImGui.IsKeyPressed(ImGuiKey.Y, false))
                    Redo();

                if (ImGui.IsKeyPressed(ImGuiKey.A, false))
                    SelectAll();

                if (ImGui.IsKeyPressed(ImGuiKey.C, false))
                    CopySelectionToClipboard();

                if (ImGui.IsKeyPressed(ImGuiKey.X, false))
                    CutSelectionToClipboard();

                if (ImGui.IsKeyPressed(ImGuiKey.V, false))
                    PasteFromClipboard();
            }

            // Text input
            for (int i = 0; i < io.InputQueueCharacters.Size; i++)
            {
                ushort u = io.InputQueueCharacters[i];
                if (u == 0) continue;

                char c = (char)u;
                if (!char.IsControl(c) || c == '\n' || c == '\t')
                    InsertChar(c);
            }

            // Navigation / editing keys
            if (ImGui.IsKeyPressed(ImGuiKey.Backspace, true))
                Backspace();
            if (ImGui.IsKeyPressed(ImGuiKey.Delete, true))
                Delete();
            if (ImGui.IsKeyPressed(ImGuiKey.Enter, true))
                InsertChar('\n');
            if (ImGui.IsKeyPressed(ImGuiKey.Tab, true))
                InsertChar('\t');

            if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow, true))
                MoveLeft();
            if (ImGui.IsKeyPressed(ImGuiKey.RightArrow, true))
                MoveRight();
            if (ImGui.IsKeyPressed(ImGuiKey.UpArrow, true))
                MoveUp();
            if (ImGui.IsKeyPressed(ImGuiKey.DownArrow, true))
                MoveDown();
            if (ImGui.IsKeyPressed(ImGuiKey.Home, true))
                MoveHome();
            if (ImGui.IsKeyPressed(ImGuiKey.End, true))
                MoveEnd();

            EnsureCursorInBounds();
        }

        // ---------------------------------------------------------------------
        // Syntax colouring
        // ---------------------------------------------------------------------

        private void ColorizeAll()
        {
            _tokens.Clear();
            for (int i = 0; i < _lines.Count; i++)
                ColorizeLine(i);
        }

        private static bool IsIdentStart(char c)
            => char.IsLetter(c) || c == '_';

        private static bool IsIdentChar(char c)
            => char.IsLetterOrDigit(c) || c == '_';

        private void ColorizeLine(int lineIndex)
        {
            if (lineIndex < 0 || lineIndex >= _lines.Count)
                return;

            _tokens.RemoveAll(t => t.Line == lineIndex);

            string line = _lines[lineIndex];
            int i = 0;
            int n = line.Length;

            while (i < n)
            {
                int start = i;
                char c = line[i];

                if (c == '-' && i + 1 < n && line[i + 1] == '-')
                {
                    int len = n - i;
                    _tokens.Add(new Token
                    {
                        Line = lineIndex,
                        Start = i,
                        Length = len,
                        Type = TokenType.Comment
                    });
                    break;
                }

                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    i++;
                    while (i < n)
                    {
                        if (line[i] == quote)
                        {
                            i++;
                            break;
                        }
                        if (line[i] == '\\' && i + 1 < n)
                            i += 2;
                        else
                            i++;
                    }

                    int len = i - start;
                    _tokens.Add(new Token
                    {
                        Line = lineIndex,
                        Start = start,
                        Length = len,
                        Type = TokenType.String
                    });
                    continue;
                }

                if (char.IsDigit(c))
                {
                    i++;
                    while (i < n && (char.IsDigit(line[i]) || line[i] == '.' || line[i] == 'x' || line[i] == 'X' ||
                                     line[i] >= 'a' && line[i] <= 'f' || line[i] >= 'A' && line[i] <= 'F'))
                    {
                        i++;
                    }

                    int len = i - start;
                    _tokens.Add(new Token
                    {
                        Line = lineIndex,
                        Start = start,
                        Length = len,
                        Type = TokenType.Number
                    });
                    continue;
                }

                if (IsIdentStart(c))
                {
                    i++;
                    while (i < n && IsIdentChar(line[i]))
                        i++;

                    int len = i - start;
                    string ident = line.Substring(start, len);

                    // Look at the previous non-whitespace char to see if we're after '.' or ':'
                    char prevNonWs = '\0';
                    for (int p = start - 1; p >= 0; p--)
                    {
                        char pc = line[p];
                        if (!char.IsWhiteSpace(pc))
                        {
                            prevNonWs = pc;
                            break;
                        }
                    }
                    bool afterDotOrColon = prevNonWs == '.' || prevNonWs == ':';

                    TokenType tt;
                    if (LuaKeywords.Contains(ident))
                    {
                        tt = TokenType.Keyword;
                    }
                    else if (LuaBuiltins.Contains(ident))
                    {
                        tt = TokenType.Builtin;
                    }
                    else if (LuaUiMethods.Contains(ident) || LuaAppMethods.Contains(ident))
                    {
                        // Known API / app methods -> yellow
                        tt = TokenType.Method;
                    }
                    else if (afterDotOrColon)
                    {
                        // Anything like state.counter, state.name etc. -> blue
                        tt = TokenType.Field;
                    }
                    else
                    {
                        tt = TokenType.Identifier;
                    }

                    _tokens.Add(new Token
                    {
                        Line = lineIndex,
                        Start = start,
                        Length = len,
                        Type = tt
                    });
                    continue;
                }

                // Operators / punctuation we care to colour (including brackets)
                if ("+-*/%=&|<>~^#()[]{}".IndexOf(c) >= 0)
                {
                    _tokens.Add(new Token
                    {
                        Line = lineIndex,
                        Start = i,
                        Length = 1,
                        Type = TokenType.Operator
                    });
                    i++;
                    continue;
                }

                i++;
            }
        }

        // ---------------------------------------------------------------------
        // Rendering
        // ---------------------------------------------------------------------

        private int CountIndentLevels(string line)
        {
            int spaces = 0;
            foreach (char c in line)
            {
                if (c == ' ')
                    spaces++;
                else if (c == '\t')
                    spaces += TabSize;
                else
                    break;
            }
            return spaces / TabSize;
        }

        private void DrawContents()
        {
            var drawList = ImGui.GetWindowDrawList();
            Vector2 origin = ImGui.GetCursorScreenPos();

            float lineSpacing = ImGui.GetTextLineHeightWithSpacing();
            _lineHeight = lineSpacing;
            _charAdvance = ImGui.CalcTextSize("X").X;

            var io = ImGui.GetIO();
            bool hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows);

            // Mouse selection / caret placement
            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                SetCaretFromMouse(io.MousePos, origin, lineSpacing);

                _isSelecting = true;
                _selStartLine = _cursorLine;
                _selStartColumn = _cursorColumn;
                _selEndLine = _cursorLine;
                _selEndColumn = _cursorColumn;
            }
            else if (_isSelecting && ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                SetCaretFromMouse(io.MousePos, origin, lineSpacing);
                _selEndLine = _cursorLine;
                _selEndColumn = _cursorColumn;
            }
            else if (_isSelecting && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                SetCaretFromMouse(io.MousePos, origin, lineSpacing);
                _selEndLine = _cursorLine;
                _selEndColumn = _cursorColumn;
                _isSelecting = false;
            }

            var tokensByLine = new Dictionary<int, List<Token>>();
            foreach (var t in _tokens)
            {
                if (!tokensByLine.TryGetValue(t.Line, out var list))
                {
                    list = new List<Token>();
                    tokensByLine[t.Line] = list;
                }
                list.Add(t);
            }

            foreach (var list in tokensByLine.Values)
                list.Sort((a, b) => a.Start.CompareTo(b.Start));

            GetSelectionBounds(out int selStartLine, out int selStartCol, out int selEndLine, out int selEndCol);

            for (int lineIndex = 0; lineIndex < _lines.Count; lineIndex++)
            {
                string line = _lines[lineIndex];
                Vector2 linePos = origin + new Vector2(0, lineIndex * lineSpacing);

                // Compute nesting level per bracket column on this line
                var bracketLevels = new Dictionary<int, int>();
                int nesting = 0;
                for (int col = 0; col < line.Length; col++)
                {
                    char ch = line[col];
                    if (ch == '(' || ch == '[' || ch == '{')
                    {
                        bracketLevels[col] = nesting;
                        nesting++;
                    }
                    else if (ch == ')' || ch == ']' || ch == '}')
                    {
                        nesting = Math.Max(0, nesting - 1);
                        bracketLevels[col] = nesting;
                    }
                }

                // Indent guides
                int indentLevels = CountIndentLevels(line);
                if (indentLevels > 0)
                {
                    uint indentCol = ImGui.GetColorU32(_colIndentGuide);
                    float guideXStep = TabSize * _charAdvance;

                    for (int lvl = 0; lvl < indentLevels; lvl++)
                    {
                        float xStepped = linePos.X + (lvl + 0.5f) * guideXStep;
                        Vector2 p1 = new(xStepped, linePos.Y);
                        Vector2 p2 = new(xStepped, linePos.Y + lineSpacing);
                        drawList.AddLine(p1, p2, indentCol, 1.0f);
                    }
                }

                // Selection highlight for this line
                if (HasSelection &&
                    lineIndex >= selStartLine && lineIndex <= selEndLine)
                {
                    int lineLen = line.Length;
                    int startCol, endCol;

                    if (selStartLine == selEndLine)
                    {
                        startCol = selStartCol;
                        endCol = selEndCol;
                    }
                    else if (lineIndex == selStartLine)
                    {
                        startCol = selStartCol;
                        endCol = lineLen;
                    }
                    else if (lineIndex == selEndLine)
                    {
                        startCol = 0;
                        endCol = selEndCol;
                    }
                    else
                    {
                        startCol = 0;
                        endCol = lineLen;
                    }

                    startCol = Math.Clamp(startCol, 0, lineLen);
                    endCol = Math.Clamp(endCol, 0, lineLen);

                    if (endCol > startCol)
                    {
                        // x1 = width of [0, startCol)
                        float x1 = linePos.X;
                        if (startCol > 0)
                        {
                            string left = line.Substring(0, startCol);
                            x1 += ImGui.CalcTextSize(left).X;
                        }

                        // width of [startCol, endCol)
                        float x2 = x1;
                        int selLen = endCol - startCol;
                        if (selLen > 0)
                        {
                            string mid = line.Substring(startCol, selLen);
                            x2 = x1 + ImGui.CalcTextSize(mid).X;
                        }

                        Vector2 selMin = new(x1, linePos.Y);
                        Vector2 selMax = new(x2, linePos.Y + _lineHeight);

                        uint selCol = ImGui.GetColorU32(_colSelection);
                        drawList.AddRectFilled(selMin, selMax, selCol);
                    }
                }

                tokensByLine.TryGetValue(lineIndex, out var tokenList);

                // Caret (VSCode-style blink, only when focused)
                if (_hasFocus && lineIndex == _cursorLine)
                {
                    int caretColIndex = Math.Clamp(_cursorColumn, 0, line.Length);
                    float xCaret = 0f;

                    if (tokenList == null || tokenList.Count == 0)
                    {
                        if (caretColIndex > 0)
                        {
                            string sub = line.Substring(0, caretColIndex);
                            xCaret = ImGui.CalcTextSize(sub).X;
                        }
                    }
                    else
                    {
                        int srcIndex = 0;

                        foreach (var t in tokenList)
                        {
                            if (caretColIndex <= t.Start)
                            {
                                if (caretColIndex > srcIndex)
                                {
                                    string plainSeg = line.Substring(srcIndex, caretColIndex - srcIndex);
                                    xCaret += ImGui.CalcTextSize(plainSeg).X;
                                }
                                goto CaretDone;
                            }

                            if (t.Start > srcIndex)
                            {
                                string plainSeg = line.Substring(srcIndex, t.Start - srcIndex);
                                xCaret += ImGui.CalcTextSize(plainSeg).X;
                                srcIndex = t.Start;
                            }

                            int tokenEnd = t.Start + t.Length;
                            if (caretColIndex <= tokenEnd)
                            {
                                if (caretColIndex > t.Start)
                                {
                                    string tokenSeg = line.Substring(t.Start, caretColIndex - t.Start);
                                    xCaret += ImGui.CalcTextSize(tokenSeg).X;
                                }
                                goto CaretDone;
                            }
                            else
                            {
                                string tokenText = line.Substring(t.Start, tokenEnd - t.Start);
                                xCaret += ImGui.CalcTextSize(tokenText).X;
                                srcIndex = tokenEnd;
                            }
                        }

                        if (caretColIndex > srcIndex)
                        {
                            string tailSeg = line.Substring(srcIndex, caretColIndex - srcIndex);
                            xCaret += ImGui.CalcTextSize(tailSeg).X;
                        }
                    }

                    CaretDone:
                    float caretX = linePos.X + xCaret;

                    // Remember caret position so the popup can appear near it
                    _caretScreenPos = new Vector2(caretX, linePos.Y + _lineHeight);

                    double time = ImGui.GetTime();
                    bool caretVisible = (long)(time / 0.53) % 2 == 0;

                    if (caretVisible)
                    {
                        Vector2 caretMin = new(caretX, linePos.Y);
                        Vector2 caretMax = new(caretX + 1.0f, linePos.Y + _lineHeight);
                        uint caretColor = ImGui.GetColorU32(_colCaret);
                        drawList.AddRectFilled(caretMin, caretMax, caretColor);
                    }
                }


                if (tokenList == null || tokenList.Count == 0)
                {
                    if (line.Length > 0)
                        drawList.AddText(linePos, ImGui.GetColorU32(_colDefault), line);
                    continue;
                }

                int cursor = 0;
                float x = 0f;

                foreach (var t in tokenList)
                {
                    if (t.Start > cursor)
                    {
                        int lenPlain = t.Start - cursor;
                        if (cursor + lenPlain > line.Length)
                            lenPlain = Math.Max(0, line.Length - cursor);

                        if (lenPlain > 0)
                        {
                            string plain = line.Substring(cursor, lenPlain);
                            Vector2 pos = linePos + new Vector2(x, 0);
                            drawList.AddText(pos, ImGui.GetColorU32(_colDefault), plain);
                            x += ImGui.CalcTextSize(plain).X;
                        }
                    }

                    if (t.Start >= line.Length)
                    {
                        cursor = line.Length;
                        break;
                    }

                    int maxLen = line.Length - t.Start;
                    int tokLen = Math.Min(t.Length, maxLen);
                    if (tokLen <= 0)
                    {
                        cursor = t.Start;
                        continue;
                    }

                    string tokenText = line.Substring(t.Start, tokLen);
                    if (tokenText.Length > 0)
                    {
                        Vector4 col = t.Type switch
                        {
                            TokenType.Keyword => _colKeyword,
                            TokenType.Builtin => _colBuiltin,
                            TokenType.Method => _colMethod,
                            TokenType.Number => _colNumber,
                            TokenType.String => _colString,
                            TokenType.Comment => _colComment,
                            TokenType.Operator => _colOperator,
                            TokenType.Field => _colField,
                            _ => _colDefault
                        };

                        // Rainbow bracket override: if this operator is a single bracket, recolour it
                        if (t.Type == TokenType.Operator && tokenText.Length == 1)
                        {
                            char ch = tokenText[0];
                            if (ch == '(' || ch == ')' || ch == '[' || ch == ']' || ch == '{' || ch == '}')
                            {
                                if (bracketLevels.TryGetValue(t.Start, out int level) && _bracketColors.Length > 0)
                                {
                                    int idx = level % _bracketColors.Length;
                                    col = _bracketColors[idx];
                                }
                            }
                        }

                        Vector2 pos = linePos + new Vector2(x, 0);
                        drawList.AddText(pos, ImGui.GetColorU32(col), tokenText);
                        x += ImGui.CalcTextSize(tokenText).X;
                    }

                    cursor = t.Start + tokLen;
                }

                if (cursor < line.Length)
                {
                    string tail = line[cursor..];
                    Vector2 pos = linePos + new Vector2(x, 0);
                    drawList.AddText(pos, ImGui.GetColorU32(_colDefault), tail);
                }
            }

            ImGui.Dummy(new Vector2(0, _lines.Count * lineSpacing));

            HandleCompletionPopup();
        }
    }
}

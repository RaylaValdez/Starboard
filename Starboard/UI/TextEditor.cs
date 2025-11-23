using ImGuiNET;
using Starboard.Lua;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace Starboard.UI
{
    internal sealed class TextEditor
    {
        public string Text
        {
            get => GetText();
            set => SetText(value ?? string.Empty);
        }
        private string _inlineSuggestion = string.Empty;
        private string _completionFilter = "";
        private string _findText = "";
        private string _replaceText = "";
        private string replaceLabel = "Replace";

        private int _cursorLine;
        private int _cursorColumn;
        private int _selStartLine = -1;
        private int _selStartColumn = -1;
        private int _selEndLine = -1;
        private int _selEndColumn = -1;
        private int _completionSelectedIndex = 0;
        private int _inlineSuggestionLine = -1;
        private int _inlineSuggestionColumn = -1;
        private int _searchResultLine = -1;
        private int _searchResultCol = -1;

        private const int TabSize = 4;

        private bool _hasFocus;
        private bool _isSelecting;
        private bool HasSelection =>
            _selStartLine >= 0 &&
            (_selStartLine != _selEndLine || _selStartColumn != _selEndColumn);
        private bool _suppressUndoCapture;
        private bool _completionInitialized = false;
        private bool _completionOpen = false;
        private bool _searchOpen = false;
        private bool _caseSensitive = false;
        private bool _wholeWord = false;

        private float _lineHeight;
        private float _charAdvance;

        private readonly List<CompletionItem> _completionItems = new();
        private readonly List<CompletionItem> _completionFiltered = new();
        private readonly List<string> _lines = new() { string.Empty };
        private readonly List<Token> _tokens = new();

        private readonly Stack<EditorState> _undoStack = new();
        private readonly Stack<EditorState> _redoStack = new();

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

        private struct Token
        {
            public int Line;
            public int Start;
            public int Length;
            public TokenType Type;
        }

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

        private sealed class CompletionItem
        {
            public string Name = "";
            public string Signature = "";
            public string Summary = "";
            public string InsertText = "";
        }

        private Vector2 _caretScreenPos;
        private readonly Vector2 _mouseHitOffset = new Vector2(-8f, 0f);

        private readonly Vector4 _colDefault = new(0.831f, 0.831f, 0.831f, 1.0f);
        private readonly Vector4 _colKeyword = new(0.773f, 0.525f, 0.753f, 1.0f);
        private readonly Vector4 _colBuiltin = new(0.831f, 0.831f, 0.831f, 1.0f);
        private readonly Vector4 _colMethod = new(0.863f, 0.863f, 0.667f, 1.0f);
        private readonly Vector4 _colNumber = new(0.710f, 0.808f, 0.659f, 1.0f);
        private readonly Vector4 _colString = new(0.808f, 0.569f, 0.471f, 1.0f);
        private readonly Vector4 _colComment = new(0.416f, 0.600f, 0.333f, 1.0f);
        private readonly Vector4 _colOperator = new(0.875f, 0.875f, 0.875f, 1.0f);
        private readonly Vector4 _colField = new(0.612f, 0.863f, 0.996f, 1.0f);
        private readonly Vector4[] _bracketColors =
        {
            new(1f, 0.84f, 0f, 1.0f),
            new(0.85f, 0.43f, 0.83f, 1.0f),
            new(0.09f, 0.62f, 1f, 1.0f),
        };
        private readonly Vector4 _colSelection = new(0.149f, 0.310f, 0.471f, 1.0f);
        private readonly Vector4 _colCaret = new(0.682f, 0.686f, 0.678f, 1.0f);
        private readonly Vector4 _colIndentGuide = new(0.251f, 0.251f, 0.251f, 0.6f);
        private readonly Vector4 _colLineNumber = new(0.5f, 0.5f, 0.5f, 0.9f);

        public void Render(string id, Vector2 size)
        {
            unsafe
            {
                if (Program._jBMReg.NativePtr != null)
                    ImGui.PushFont(Program._jBMReg.NativePtr);
            }

            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.117f, 0.117f, 0.117f, 1.0f));

            if (!ImGui.BeginChild(id, size, ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar))
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

            if (_searchOpen)
            {
                float barWidth = 360f;
                float barHeight = 100f;

                Vector2 childPos = ImGui.GetCursorScreenPos();
                Vector2 childSize = ImGui.GetContentRegionAvail();

                Vector2 posTopRight = new Vector2(
                    childPos.X + childSize.X - barWidth - 6f,
                    childPos.Y + 6f
                );

                Vector2 oldCursor = ImGui.GetCursorScreenPos();
                ImGui.SetCursorScreenPos(posTopRight);

                ImGui.BeginChild("##SearchReplace",
                    new Vector2(barWidth, barHeight),
                    ImGuiChildFlags.Borders,
                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings);

                DrawSearchUI();
                ImGui.EndChild();

                ImGui.SetCursorScreenPos(oldCursor);
            }

            DrawContents();

            ImGui.EndChild();
            ImGui.PopStyleColor();

            unsafe
            {
                if (Program._jBMReg.NativePtr != null)
                    ImGui.PopFont();
            }
        }

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

        private void DeleteSelection()
        {
            if (!HasSelection)
                return;

            GetSelectionBounds(
                out int startLine, out int startCol,
                out int endLine, out int endCol);

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
                string first = _lines[startLine];
                string last = _lines[endLine];

                startCol = Math.Clamp(startCol, 0, first.Length);
                endCol = Math.Clamp(endCol, 0, last.Length);

                string merged = string.Concat(first.AsSpan(0, startCol), last.AsSpan(endCol));

                _lines[startLine] = merged;

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
                    sb.Append(line.AsSpan(startCol, endCol - startCol));
            }
            else
            {
                string first = _lines[startLine];
                startCol = Math.Clamp(startCol, 0, first.Length);
                sb.Append(first.AsSpan(startCol));
                sb.Append('\n');

                for (int i = startLine + 1; i < endLine; i++)
                {
                    sb.Append(_lines[i]);
                    sb.Append('\n');
                }

                string last = _lines[endLine];
                endCol = Math.Clamp(endCol, 0, last.Length);
                sb.Append(last.AsSpan(0, endCol));
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

            Vector2 local = mousePos - origin + _mouseHitOffset;

            int line = (int)Math.Floor(local.Y / lineSpacing);
            if (line < 0) line = 0;
            if (line >= _lines.Count) line = _lines.Count - 1;

            string text = _lines[line];

            float targetX = local.X;
            if (targetX < 0) targetX = 0;

            int lo = 0;
            int hi = text.Length;

            while (lo < hi)
            {
                int mid = (lo + hi) / 2;

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

                string name = m.Name;

                var pars = m.GetParameters();
                string sig = $"{name}(" +
                             string.Join(", ",
                                 pars.Select(p => $"{p.ParameterType.Name} {p.Name}")) +
                             ")";

                var summary = TryGetXmlSummary(m) ?? BuildSummary(name, pars);

                var item = new CompletionItem
                {
                    Name = $"ui.{name}",
                    Signature = sig,
                    Summary = summary,
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
                    if (item.Name.Contains(f, StringComparison.InvariantCultureIgnoreCase) ||
                        item.Signature.Contains(f, StringComparison.InvariantCultureIgnoreCase))
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

            PushUndoState();

            if (HasSelection)
                DeleteSelection();

            _suppressUndoCapture = true;
            foreach (char c in item.InsertText)
                InsertChar(c);
            _suppressUndoCapture = false;
        }

        private void UpdateInlineSuggestion()
        {
            _inlineSuggestion = string.Empty;
            _inlineSuggestionLine = -1;
            _inlineSuggestionColumn = -1;

            if (!_hasFocus)
                return;

            EnsureCompletionItems();

            if (_cursorLine < 0 || _cursorLine >= _lines.Count)
                return;

            string line = _lines[_cursorLine];
            int col = Math.Clamp(_cursorColumn, 0, line.Length);

            int start = col;
            while (start > 0)
            {
                char ch = line[start - 1];
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.')
                    start--;
                else
                    break;
            }

            if (start == col)
                return;

            string prefix = line.Substring(start, col - start);
            if (string.IsNullOrWhiteSpace(prefix))
                return;

            CompletionItem? best = null;

            foreach (var item in _completionItems)
            {
                if (item.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    item.Name.Length > prefix.Length)
                {
                    best = item;
                    break;
                }
            }

            if (best == null)
                return;

            string suffix = best.Name.Substring(prefix.Length);

            _inlineSuggestion = suffix;
            _inlineSuggestionLine = _cursorLine;
            _inlineSuggestionColumn = _cursorColumn;
        }

        private void AcceptInlineSuggestion()
        {
            if (string.IsNullOrEmpty(_inlineSuggestion))
                return;

            PushUndoState();
            _suppressUndoCapture = true;

            foreach (char c in _inlineSuggestion)
                InsertChar(c);

            _suppressUndoCapture = false;

            _inlineSuggestion = string.Empty;
        }

        private void HandleCompletionPopup()
        {
            EnsureCompletionItems();

            var io = ImGui.GetIO();

            if (_hasFocus && io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.Space))
            {
                Backspace();
                _completionOpen = true;
                _completionFilter = "";
                ApplyCompletionFilter();
            }

            if (!_completionOpen)
                return;

            Vector2 popupPos = _caretScreenPos + new Vector2(0f, 4f);
            ImGui.SetNextWindowPos(popupPos, ImGuiCond.Appearing);
            ImGui.SetNextWindowBgAlpha(0.95f);

            bool openFlag = _completionOpen;
            if (!ImGui.Begin("##LuaCompletion", ref openFlag,
                ImGuiWindowFlags.NoTitleBar |
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoNavInputs))
            {
                ImGui.End();
                _completionOpen = openFlag;
                return;
            }

            _completionOpen = openFlag;

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

            ImGui.InputText("Filter", ref _completionFilter, 128);
            if (ImGui.IsItemEdited())
                ApplyCompletionFilter();

            ImGui.Separator();

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

            float rowHeight = ImGui.GetTextLineHeightWithSpacing();
            int visibleRows = 10;
            Vector2 listSize = new Vector2(450f, rowHeight * visibleRows);

            if (ImGui.BeginChild("##LuaCompletionList", listSize, ImGuiChildFlags.None, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
            {
                for (int i = 0; i < _completionFiltered.Count; i++)
                {
                    var item = _completionFiltered[i];
                    bool selected = i == _completionSelectedIndex;

                    float maxLabelWidth = ImGui.GetContentRegionAvail().X;

                    string label = BuildCompletionLabel(item, maxLabelWidth);

                    if (ImGui.Selectable(label, selected))
                    {
                        InsertCompletion(item);
                        _completionOpen = false;
                        ImGui.EndChild();
                        ImGui.End();
                        return;
                    }

                    if (ImGui.IsItemHovered())
                    {
                        float wrapPx = ImGui.GetFontSize() * 20f;

                        ImGui.SetNextWindowSizeConstraints(
                            new Vector2(100f, 0f),
                            new Vector2(wrapPx + 40, float.MaxValue)
                        );

                        ImGui.BeginTooltip();
                        ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + wrapPx);
                        ImGui.TextUnformatted(SanitizeSummary(item.Summary));
                        ImGui.PopTextWrapPos();
                        ImGui.EndTooltip();
                    }

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }
            }
            ImGui.EndChild();

            ImGui.End();
        }

        private static string GetLeadingIndent(string line)
        {
            int i = 0;
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
                i++;
            return line.Substring(0, i);
        }

        private static string StripComment(string line)
        {
            int idx = line.IndexOf("--", StringComparison.Ordinal);
            if (idx >= 0)
                line = line.Substring(0, idx);
            return line.Trim();
        }

        private static bool LineOpensLuaBlock(string line)
        {
            string trimmed = StripComment(line);
            if (trimmed.Length == 0)
                return false;

            if (trimmed.StartsWith("if ", StringComparison.Ordinal) &&
                trimmed.EndsWith(" then", StringComparison.Ordinal))
                return true;

            if (trimmed.StartsWith("for ", StringComparison.Ordinal) &&
                trimmed.EndsWith(" do", StringComparison.Ordinal))
                return true;

            if (trimmed.StartsWith("while ", StringComparison.Ordinal) &&
                trimmed.EndsWith(" do", StringComparison.Ordinal))
                return true;

            if (trimmed == "repeat")
                return true;

            if (trimmed.StartsWith("function ", StringComparison.Ordinal) ||
                trimmed.StartsWith("local function ", StringComparison.Ordinal))
                return true;

            if (trimmed == "do")
                return true;

            return false;
        }

        private static bool LineIsLuaBlockCloser(string line)
        {
            string trimmed = StripComment(line);
            if (trimmed.Length == 0)
                return false;

            if (trimmed == "end" || trimmed.StartsWith("end ", StringComparison.Ordinal))
                return true;

            if (trimmed.StartsWith("until ", StringComparison.Ordinal))
                return true;

            return false;
        }

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
                string prevLine = _lines[_cursorLine];
                _cursorColumn = Math.Clamp(_cursorColumn, 0, prevLine.Length);

                string left = prevLine[.._cursorColumn];
                string right = prevLine[_cursorColumn..];

                _lines[_cursorLine] = left;

                int indentLevels = CountIndentLevels(prevLine);

                if (LineIsLuaBlockCloser(prevLine) && indentLevels > 0)
                    indentLevels--;

                if (LineOpensLuaBlock(prevLine))
                    indentLevels++;

                if (indentLevels < 0)
                    indentLevels = 0;

                int indentSpaces = indentLevels * TabSize;
                string indent = new string(' ', indentSpaces);

                string newLine = indent + right;
                _lines.Insert(_cursorLine + 1, newLine);

                _cursorLine++;
                _cursorColumn = indent.Length;

                ClearSelection();
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

        private void HandleKeyboard(ImGuiIOPtr io)
        {
            if (_completionOpen)
                return;

            bool ctrl = io.KeyCtrl;
            bool shift = io.KeyShift;

            if (ctrl)
            {
                if (ImGui.IsKeyPressed(ImGuiKey.Z, true))
                {
                    if (shift)
                        Redo();
                    else
                        Undo();
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

            for (int i = 0; i < io.InputQueueCharacters.Size; i++)
            {
                char c = (char)io.InputQueueCharacters[i];
                if (!char.IsControl(c) || c == '\n' || c == '\t')
                {
                    InsertChar(c);
                }
            }

            if (ImGui.IsKeyPressed(ImGuiKey.Backspace, true))
                Backspace();
            if (ImGui.IsKeyPressed(ImGuiKey.Delete, true))
                Delete();
            if (ImGui.IsKeyPressed(ImGuiKey.Enter, true))
                InsertChar('\n');
            if (ImGui.IsKeyPressed(ImGuiKey.Tab, false))
            {
                if (!string.IsNullOrEmpty(_inlineSuggestion))
                {
                    AcceptInlineSuggestion();
                }
                else
                {
                    InsertChar('\t');
                }
            }

            if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow, true))
            {
                if (ctrl)
                    _cursorColumn = FindPrevWordBoundary(_cursorLine, _cursorColumn);
                else
                    MoveLeft();

                if (shift)
                    BeginOrExtendSelection();
                else
                    ClearSelection();
            }

            if (ImGui.IsKeyPressed(ImGuiKey.RightArrow, true))
            {
                if (ctrl)
                    _cursorColumn = FindNextWordBoundary(_cursorLine, _cursorColumn);
                else
                    MoveRight();
                if (shift)
                    BeginOrExtendSelection();
                else
                    ClearSelection();
            }

            if (ImGui.IsKeyPressed(ImGuiKey.UpArrow, true))
            {
                if (ctrl)
                    _cursorColumn = FindNextWordBoundary(_cursorLine, _cursorColumn);
                else MoveUp();

                if (shift)
                    BeginOrExtendSelection();
                else ClearSelection();
            }

            if (ImGui.IsKeyPressed(ImGuiKey.DownArrow, true))
            {
                if (ctrl)
                    _cursorColumn = FindNextWordBoundary(_cursorLine, _cursorColumn);
                else MoveDown();

                if (shift)
                    BeginOrExtendSelection();
                else ClearSelection();
            }

            if (ImGui.IsKeyPressed(ImGuiKey.Home, true))
            {
                int smart = GetSmartHomeColumn(_cursorLine);
                if (_cursorColumn == smart)
                    _cursorColumn = 0;
                else
                    _cursorColumn = smart;

                if (shift)
                    BeginOrExtendSelection();
                else
                    ClearSelection();
            }

            if (ImGui.IsKeyPressed(ImGuiKey.End, true))
            {
                _cursorColumn = _lines[_cursorLine].Length;
                if (shift)
                    BeginOrExtendSelection();
                else
                    ClearSelection();
            }

            if (ctrl && ImGui.IsKeyPressed(ImGuiKey.F, false))
            {
                _searchOpen = true;
            }



            EnsureCursorInBounds();

            UpdateInlineSuggestion();
        }

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
                        tt = TokenType.Method;
                    }
                    else if (afterDotOrColon)
                    {
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

                if ("+-*/%=&|<>~^#()[]{}".Contains(c))
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

        private static int CountIndentLevels(string line)
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
            bool hovered = ImGui.IsWindowHovered();

            if (hovered && io.MouseWheelH != 0.0f)
            {
                float scrollStep = ImGui.GetTextLineHeightWithSpacing() * 4.0f;

                float scrollX = ImGui.GetScrollX();
                scrollX -= io.MouseWheelH * scrollStep;

                ImGui.SetScrollX(scrollX);
            }

            int lineCount = Math.Max(1, _lines.Count);
            int digits = lineCount.ToString().Length;
            string sample = new string('9', digits);
            float numWidth = ImGui.CalcTextSize(sample).X;

            float gutterPadding = 6.0f;
            float gutterWidth = numWidth + gutterPadding * 2.0f;

            Vector2 textOrigin = origin + new Vector2(gutterWidth, 0);

            float maxLinePixelWidth = gutterWidth;

            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                SetCaretFromMouse(io.MousePos, textOrigin, lineSpacing);

                _isSelecting = true;
                _selStartLine = _cursorLine;
                _selStartColumn = _cursorColumn;
                _selEndLine = _cursorLine;
                _selEndColumn = _cursorColumn;
            }
            else if (_isSelecting && ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                SetCaretFromMouse(io.MousePos, textOrigin, lineSpacing);
                _selEndLine = _cursorLine;
                _selEndColumn = _cursorColumn;
            }
            else if (_isSelecting && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                SetCaretFromMouse(io.MousePos, textOrigin, lineSpacing);
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

            {
                Vector2 sepTop = origin + new Vector2(gutterWidth - gutterPadding * 0.5f, 0);
                Vector2 sepBot = sepTop + new Vector2(0, _lines.Count * lineSpacing);
                drawList.AddLine(sepTop, sepBot, ImGui.GetColorU32(_colIndentGuide), 1.0f);
            }

            for (int lineIndex = 0; lineIndex < _lines.Count; lineIndex++)
            {
                string line = _lines[lineIndex];

                Vector2 lineBase = origin + new Vector2(0, lineIndex * lineSpacing);

                Vector2 linePos = lineBase + new Vector2(gutterWidth, 0);

                float linePixelWidth = gutterWidth;

                string numText = (lineIndex + 1).ToString();
                Vector2 numSize = ImGui.CalcTextSize(numText);

                float numX = lineBase.X + gutterWidth - gutterPadding - numSize.X;
                float numY = lineBase.Y;

                drawList.AddText(
                    new Vector2(numX, numY),
                    ImGui.GetColorU32(_colLineNumber),
                    numText);

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
                        float x1 = linePos.X;
                        if (startCol > 0)
                        {
                            string left = line.Substring(0, startCol);
                            x1 += ImGui.CalcTextSize(left).X;
                        }

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

                if (lineIndex == _searchResultLine &&
                    _searchResultCol >= 0 &&
                    !string.IsNullOrEmpty(_findText))
                {
                    int matchLen = Math.Min(_findText.Length, line.Length - _searchResultCol);
                    if (matchLen > 0)
                    {
                        string prefix = line.Substring(0, _searchResultCol);
                        float xStart = linePos.X + ImGui.CalcTextSize(prefix).X;

                        string match = line.Substring(_searchResultCol, matchLen);
                        float xEnd = xStart + ImGui.CalcTextSize(match).X;

                        Vector2 a = new(xStart, linePos.Y);
                        Vector2 b = new(xEnd, linePos.Y + _lineHeight);

                        drawList.AddRectFilled(a, b,
                            ImGui.GetColorU32(new Vector4(0.8f, 0.4f, 0.2f, 0.45f)));
                    }
                }

                tokensByLine.TryGetValue(lineIndex, out var tokenList);

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

                    if (!string.IsNullOrEmpty(_inlineSuggestion) &&
                        _inlineSuggestionLine == lineIndex &&
                        _inlineSuggestionColumn == _cursorColumn)
                    {
                        Vector4 ghostCol = _colDefault;
                        ghostCol.W *= 0.35f;

                        Vector2 ghostPos = new(caretX, linePos.Y);
                        drawList.AddText(ghostPos, ImGui.GetColorU32(ghostCol), _inlineSuggestion);
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

                linePixelWidth = gutterWidth + x;
                if (linePixelWidth > maxLinePixelWidth)
                    maxLinePixelWidth = linePixelWidth;
            }

            ImGui.Dummy(new Vector2(maxLinePixelWidth, _lines.Count * lineSpacing));

            HandleCompletionPopup();
        }

        private static readonly Dictionary<string, XDocument> _xmlDocCache = new();

        private static string GetXmlMemberName(MethodInfo method)
        {
            var type = method.DeclaringType;
            if (type == null)
                throw new InvalidOperationException("Method has no DeclaringType.");

            var sb = new StringBuilder();
            sb.Append("M:");
            sb.Append(type.FullName!.Replace('+', '.'));

            sb.Append('.');
            sb.Append(method.Name);

            var pars = method.GetParameters();
            if (pars.Length > 0)
            {
                sb.Append('(');
                sb.Append(string.Join(",", pars.Select(p => GetXmlTypeName(p.ParameterType))));
                sb.Append(')');
            }

            return sb.ToString();
        }

        private static string GetXmlTypeName(Type type)
        {
            if (type.IsByRef)
                type = type.GetElementType()!;

            if (type.IsGenericType)
            {
                var genericDef = type.GetGenericTypeDefinition();
                string genericName = genericDef.FullName!;
                int tickIdx = genericName.IndexOf('`');
                if (tickIdx >= 0)
                    genericName = genericName[..tickIdx];

                var args = type.GetGenericArguments().Select(GetXmlTypeName);
                return $"{genericName}{{{string.Join(",", args)}}}";
            }

            return type.FullName!;
        }

        private static string? TryGetXmlSummary(MethodInfo method)
        {
            var asm = method.DeclaringType?.Assembly;
            if (asm == null)
                return null;

            string? xmlPath = null;

            try
            {
                var loc = asm.Location;
                if (!string.IsNullOrEmpty(loc))
                    xmlPath = Path.ChangeExtension(loc, ".xml");
            }
            catch { /* single-file often throws on Location */ }

            if (string.IsNullOrEmpty(xmlPath) || !File.Exists(xmlPath))
            {
                var baseDir = AppContext.BaseDirectory;
                var fileName = asm.GetName().Name + ".xml";
                var candidate = Path.Combine(baseDir, fileName);
                if (File.Exists(candidate))
                    xmlPath = candidate;
            }

            if (string.IsNullOrEmpty(xmlPath) || !File.Exists(xmlPath))
                return null;

            if (!_xmlDocCache.TryGetValue(xmlPath, out var doc))
            {
                try
                {
                    doc = XDocument.Load(xmlPath);
                    _xmlDocCache[xmlPath] = doc;
                }
                catch
                {
                    return null;
                }
            }

            var memberName = GetXmlMemberName(method);

            var member = doc.Descendants("member")
                            .FirstOrDefault(m => (string?)m.Attribute("name") == memberName);

            var rawSummary = member?.Element("summary")?.Value;
            if (string.IsNullOrWhiteSpace(rawSummary))
                return null;

            var cleaned = string.Join(" ",
                rawSummary.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            return cleaned.Trim();
        }

        private static string SanitizeSummary(string? summary)
        {
            if (string.IsNullOrEmpty(summary)) return string.Empty;
            return summary
                .Replace('–', '-')
                .Replace('—', '-')
                .Replace('\u2018', '\'').Replace('\u2019', '\'')
                .Replace('\u201C', '"').Replace('\u201D', '"')
                .Replace('\u2026', '…');
        }

        private static string BuildCompletionLabel(CompletionItem item, float maxWidth)
        {
            string summary = item.Summary ?? string.Empty;

            summary = SanitizeSummary(summary);

            string baseLabel = $"{summary}";

            if (ImGui.CalcTextSize(baseLabel).X <= maxWidth)
                return baseLabel;

            const string ellipsis = "...";
            float ellipsisWidth = ImGui.CalcTextSize(ellipsis).X;
            float targetWidth = maxWidth - ellipsisWidth - 4f;
            if (targetWidth <= 0)
                return item.Name;

            int lo = 0;
            int hi = baseLabel.Length;

            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                string substr = baseLabel.Substring(0, mid);
                float w = ImGui.CalcTextSize(substr).X;

                if (w <= targetWidth)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            int len = Math.Max(0, lo - 1);
            return string.Concat(baseLabel.AsSpan(0, len), ellipsis);
        }

        private static bool IsWordChar(char c)
    => char.IsLetterOrDigit(c) || c == '_';

        private int FindPrevWordBoundary(int lineIndex, int col)
        {
            string line = _lines[lineIndex];
            col = Math.Clamp(col, 0, line.Length);

            while (col > 0 && char.IsWhiteSpace(line[col - 1]))
                col--;

            while (col > 0 && IsWordChar(line[col - 1]))
                col--;

            return col;
        }

        private int FindNextWordBoundary(int lineIndex, int col)
        {
            string line = _lines[lineIndex];
            col = Math.Clamp(col, 0, line.Length);

            int n = line.Length;

            while (col < n && char.IsWhiteSpace(line[col]))
                col++;

            while (col < n && IsWordChar(line[col]))
                col++;

            return col;
        }

        private void BeginOrExtendSelection()
        {
            if (!HasSelection)
            {
                _selStartLine = _cursorLine;
                _selStartColumn = _cursorColumn;
            }
            _selEndLine = _cursorLine;
            _selEndColumn = _cursorColumn;
        }

        private int GetSmartHomeColumn(int lineIndex)
        {
            string line = _lines[lineIndex];
            int i = 0;
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
                i++;
            return i;
        }

        private void ClearSearchResult()
        {
            _searchResultLine = -1;
            _searchResultCol = -1;
        }

        private void SearchNext()
        {
            DoSearch(forward: true);
        }

        private void SearchPrev()
        {
            DoSearch(forward: false);
        }

        private void DoSearch(bool forward)
        {
            ClearSelection();

            if (string.IsNullOrWhiteSpace(_findText) || _lines.Count == 0)
            {
                ClearSearchResult();
                return;
            }

            string needle = _findText;
            var comparison = _caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            int startLine = _searchResultLine >= 0 ? _searchResultLine : _cursorLine;
            int startCol = _searchResultLine >= 0 ? _searchResultCol : _cursorColumn;

            int lineCount = _lines.Count;

            if (forward)
            {
                int line = startLine;
                int col = startCol + (_searchResultLine >= 0 ? 1 : 0);

                for (int wrap = 0; wrap < 2; wrap++)
                {
                    for (; line < lineCount; line++)
                    {
                        string text = _lines[line];
                        int searchFrom = (line == startLine) ? col : 0;

                        int idx = FindMatchInLine(text, needle, searchFrom, comparison, _wholeWord);
                        if (idx >= 0)
                        {
                            _searchResultLine = line;
                            _searchResultCol = idx;
                            _cursorLine = line;
                            _cursorColumn = idx;
                            EnsureCursorInBounds();
                            return;
                        }
                    }

                    line = 0;
                    col = 0;
                }
            }
            else
            {
                int line = startLine;
                int col = startCol - 1;
                if (col < 0) { line--; col = int.MaxValue; }

                for (int wrap = 0; wrap < 2; wrap++)
                {
                    for (; line >= 0; line--)
                    {
                        string text = _lines[line];
                        int searchFrom = (line == startLine) ? Math.Min(col, text.Length - 1) : text.Length - 1;

                        int idx = FindLastMatchInLine(text, needle, searchFrom, comparison, _wholeWord);
                        if (idx >= 0)
                        {
                            _searchResultLine = line;
                            _searchResultCol = idx;
                            _cursorLine = line;
                            _cursorColumn = idx;
                            EnsureCursorInBounds();
                            return;
                        }
                    }

                    line = lineCount - 1;
                    col = int.MaxValue;
                }
            }
            ClearSearchResult();
        }

        private static int FindMatchInLine(string text, string needle, int startIndex,
                                    StringComparison cmp, bool wholeWord)
        {
            if (startIndex < 0) startIndex = 0;
            while (startIndex <= text.Length - needle.Length)
            {
                int idx = text.IndexOf(needle, startIndex, cmp);
                if (idx < 0) return -1;

                if (!wholeWord || IsWholeWordMatch(text, idx, needle.Length))
                    return idx;

                startIndex = idx + 1;
            }
            return -1;
        }

        private static int FindLastMatchInLine(string text, string needle, int startIndex,
                                StringComparison cmp, bool wholeWord)
        {
            if (string.IsNullOrEmpty(text))
                return -1;

            if (startIndex < 0)
                startIndex = text.Length - 1;
            if (startIndex > text.Length - 1)
                startIndex = text.Length - 1;

            int idx = text.LastIndexOf(needle, startIndex, cmp);
            while (idx >= 0)
            {
                if (!wholeWord || IsWholeWordMatch(text, idx, needle.Length))
                    return idx;

                if (idx == 0)
                    break;

                idx = text.LastIndexOf(needle, idx - 1, cmp);
            }

            return -1;
        }

        private static bool IsWholeWordMatch(string text, int index, int length)
        {
            int start = index;
            int end = index + length;

            bool leftOk = (start == 0) || !IsWordChar(text[start - 1]);
            bool rightOk = (end >= text.Length) || !IsWordChar(text[end]);

            return leftOk && rightOk;
        }

        private void ReplaceCurrent()
        {
            if (string.IsNullOrEmpty(_findText))
                return;

            if (_searchResultLine < 0 || _searchResultCol < 0)
                return;

            string needle = _findText;
            string replacement = _replaceText ?? string.Empty;

            string line = _lines[_searchResultLine];

            if (_searchResultCol + needle.Length > line.Length)
                return;

            PushUndoState();

            string before = line.Substring(0, _searchResultCol);
            string after = line.Substring(_searchResultCol + needle.Length);

            _lines[_searchResultLine] = before + replacement + after;
            ColorizeLine(_searchResultLine);

            _cursorLine = _searchResultLine;
            _cursorColumn = before.Length + replacement.Length;
            ClearSelection();

            _searchResultLine = -1;
            _searchResultCol = -1;

            DoSearch(true);
        }

        private void ReplaceAll()
        {
            if (string.IsNullOrEmpty(_findText) || _lines.Count == 0)
                return;

            string needle = _findText;
            string replacement = _replaceText ?? string.Empty;
            var comparison = _caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            PushUndoState();

            for (int lineIndex = 0; lineIndex < _lines.Count; lineIndex++)
            {
                string line = _lines[lineIndex];
                if (string.IsNullOrEmpty(line))
                    continue;

                int searchFrom = 0;
                bool changed = false;

                while (searchFrom <= line.Length - needle.Length)
                {
                    int idx = FindMatchInLine(line, needle, searchFrom, comparison, _wholeWord);
                    if (idx < 0)
                        break;

                    line = string.Concat(line.AsSpan(0, idx), replacement, line.AsSpan(idx + needle.Length));

                    searchFrom = idx + replacement.Length;
                    changed = true;
                }

                if (changed)
                {
                    _lines[lineIndex] = line;
                    ColorizeLine(lineIndex);
                }
            }

            ClearSelection();
            ClearSearchResult();
            EnsureCursorInBounds();
        }

        public void OpenCompletion(string? seedFilter = null)
        {
            EnsureCompletionItems();
            _completionOpen = true;
            _completionFilter = seedFilter ?? "";
            ApplyCompletionFilter();
        }

        private void DrawSearchUI()
        {
            ImGui.PushItemWidth(-1);

            if (ImGui.InputText("##find", ref _findText, 256))
            {
                ClearSearchResult();
                SearchNext();
            }

            ImGui.InputText("##replace", ref _replaceText, 256);

            if (ImGui.Button("Aa")) _caseSensitive = !_caseSensitive;
            ImGui.SameLine();
            if (ImGui.Button("ab.")) _wholeWord = !_wholeWord;

            ImGui.SameLine();
            if (ImGui.Button("Prev")) SearchPrev();
            ImGui.SameLine();
            if (ImGui.Button("Next")) SearchNext();
            ImGui.SameLine();

            if (ImGui.Button(replaceLabel))
            {
                if (replaceLabel == "Replace")
                {
                    ReplaceCurrent();
                }
                else if (replaceLabel == "Replace All")
                {
                    ReplaceAll();
                }
            }

            if (ImGui.IsItemHovered() && ImGui.GetIO().KeyShift)
            {
                replaceLabel = "Replace All";
            }
            else
            {
                replaceLabel = "Replace";
            }

            ImGui.SameLine();
            if (ImGui.Button("X"))
            {
                _searchOpen = false;
                ClearSearchResult();
            }
        }

    }
}

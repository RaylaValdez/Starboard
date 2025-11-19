using ImGuiNET;
using Overlay_Renderer.Helpers;
using Overlay_Renderer.Methods;
using Starboard.DistributedApplets;
using Starboard.GuiElements;
using Starboard.Lua;
using System.Drawing;
using System.Linq;
using System.Numerics;

namespace Starboard.Guis
{
    internal class StarboardMain
    {
        private static Rectangle _mobiFramePx;
        private static float _dpiScale = 1f;

        private static float _hoverAnim = 0f;
        private static bool _wasHoveredLastFrame = false;

        private static float _hoverLiftPx = 60f;

        private static ImFontPtr _orbiBoldFont;
        private static ImFontPtr _orbiRegFont;
        private static ImFontPtr _orbiRegFontSmall;

        private static IntPtr _cassioTex = IntPtr.Zero;

        private static bool _isExpanded = false;
        private static float _expandAnim = 0f;

        private static readonly List<IStarboardApplet> _applets = new();
        private static int _selectedAppletIndex = -1;

        private static float _secondsSinceInteraction = 0f;

        public static float SecondsSinceInteraction => _secondsSinceInteraction;

        private static bool _gameIsForeground = true;

        internal static bool GameIsForeground
        {
            get => _gameIsForeground;
            set => _gameIsForeground = value;
        }

        private static bool _importInProgress = false;
        private static float _importProgress = 0f;
        private static int _lastImportCount = 0;



        public static void Initialize(
            IntPtr cassioTex,
            float dpiScale,
            Rectangle mobiFrame,
            ImFontPtr fontBold,
            ImFontPtr font,
            ImFontPtr smallFont)
        {
            _dpiScale = dpiScale;
            _mobiFramePx = mobiFrame;
            _orbiBoldFont = fontBold;
            _orbiRegFont = font;
            _orbiRegFontSmall = smallFont;
            _cassioTex = cassioTex;

            _applets.Clear();

            var asm = typeof(StarboardMain).Assembly;

            var appletTypes = asm.GetTypes()
                .Where(t =>
                    typeof(IStarboardApplet).IsAssignableFrom(t) &&
                    !t.IsAbstract &&
                    t.GetConstructor(Type.EmptyTypes) != null &&
                    t.Namespace == "Starboard.DistributedApplets");

            foreach (var t in appletTypes)
            {
                try
                {
                    if (Activator.CreateInstance(t) is IStarboardApplet applet)
                    {
                        _applets.Add(applet);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[StarboardMain] Failed to instantiate applet '{t.FullName}': {ex.Message}");
                }
            }

            try
            {
                var externDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Starboard",
                    "ExternApplets");

                if (Directory.Exists(externDir))
                {
                    var luaFiles = Directory.EnumerateFiles(externDir, "*.lua", SearchOption.TopDirectoryOnly).ToList();

                    Logger.Info($"[StarboardMain] Found {luaFiles.Count} external Lua applet file(s) in '{externDir}'.");

                    foreach (var luaPath in luaFiles)
                    {
                        try
                        {
                            var luaApplet = new LuaApplet(luaPath);
                            _applets.Add(luaApplet);
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"[StarboardMain] Failed to load Lua applet from '{luaPath}': {ex.Message}");
                        }
                    }
                }
                else
                {
                    Logger.Info($"[StarboardMain] No external applet directory yet at '{externDir}'.");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[StarboardMain] Error while scanning external Lua applets: {ex.Message}");
            }

            SortApplets();

            AppletOrderStore.ApplySavedOrder(_applets);

            foreach (var app in _applets)
                app.Initialize();

            _selectedAppletIndex = -1;
            WebBrowserManager.SetActiveApplet(null);

        }

        public static void SetMobiFrame(Rectangle mobiFrame)
        {
            _mobiFramePx = mobiFrame;
        }

        public static void ResetOnMobiClosed()
        {
            _isExpanded = false;
            _expandAnim = 0f;
            _hoverAnim = 0f;
            _wasHoveredLastFrame = false;
            _selectedAppletIndex = -1;
            WebBrowserManager.SetActiveApplet(null);
            _secondsSinceInteraction = 0f;
        }

        private static void DrawAppletErrorToast(string message, float alpha)
        {
            if (alpha <= 0f || string.IsNullOrEmpty(message))
                return;

            Vector2 winPos = ImGui.GetWindowPos();
            Vector2 winSize = ImGui.GetWindowSize();
            var dl = ImGui.GetWindowDrawList();

            float outerPadding = 10f * _dpiScale; 
            float innerPadding = 8f * _dpiScale;  
            float maxWidth = winSize.X * 0.7f;    

            Vector2 textSize = ImGui.CalcTextSize(message, false, maxWidth);

            float boxWidth = MathF.Min(textSize.X, maxWidth) + innerPadding * 2f;
            float boxHeight = textSize.Y + innerPadding * 2f;

            Vector2 boxMax = new(
                winPos.X + winSize.X - outerPadding,
                winPos.Y + winSize.Y - outerPadding);

            Vector2 boxMin = new(
                boxMax.X - boxWidth,
                boxMax.Y - boxHeight);

            var bgCol = new Vector4(0.8f, 0.1f, 0.1f, 0.9f * alpha);
            var borderCol = new Vector4(1.0f, 0.4f, 0.4f, 1.0f * alpha);
            var textCol = new Vector4(1.0f, 1.0f, 1.0f, 1.0f * alpha);
            float rounding = 6f * _dpiScale;

            uint bgU32 = ImGui.GetColorU32(bgCol);
            uint borderU32 = ImGui.GetColorU32(borderCol);

            dl.AddRectFilled(boxMin, boxMax, bgU32, rounding);
            dl.AddRect(boxMin, boxMax, borderU32, rounding, ImDrawFlags.None, 2f * _dpiScale);

            Vector2 textPos = boxMin + new Vector2(innerPadding, innerPadding);

            ImGui.SetCursorScreenPos(textPos);

            float innerWidth = boxWidth - innerPadding * 2f;
            ImGui.PushTextWrapPos(textPos.X + innerWidth);
            ImGui.PushStyleColor(ImGuiCol.Text, textCol);
            ImGui.TextUnformatted(message);
            ImGui.PopStyleColor();
            ImGui.PopTextWrapPos();
        }

        private static void SortApplets()
        {
            _applets.Sort((a, b) =>
                string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        }




        public static void Draw(float dt, float globalAlpha)
        {
            var io = ImGui.GetIO();
            globalAlpha = Math.Clamp(globalAlpha, 0f, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, globalAlpha);

            IStarboardApplet? selectedApplet = null;
            bool isWebApplet = false;

            if (_selectedAppletIndex >= 0 && _selectedAppletIndex < _applets.Count)
            {
                selectedApplet = _applets[_selectedAppletIndex];
                isWebApplet = selectedApplet.UsesWebView;
            }

            bool interactedThisFrame = false;

            bool webForBg = isWebApplet;

            var winSize = new Vector2(_mobiFramePx.Width * 0.85f, _mobiFramePx.Height * 0.9f);
            float centerX = _mobiFramePx.Left + (_mobiFramePx.Width - winSize.X) / 2f;
            float restY = _mobiFramePx.Top + _mobiFramePx.Height * 0.965f;

            bool targetHover = _wasHoveredLastFrame;
            float hoverTarget = targetHover ? 1f : 0f;
            const float hoverSpeed = 8f;
            float ht = dt * hoverSpeed;
            ht = Math.Clamp(ht, 0f, 1f);
            _hoverAnim += (hoverTarget - _hoverAnim) * ht;

            float lift = _hoverLiftPx * _dpiScale;
            float baseY = restY - _hoverAnim * lift;

            const float expandSpeed = 6f;
            float expandTarget = _isExpanded ? 1f : 0f;
            float et = dt * expandSpeed;
            et = Math.Clamp(et, 0f, 1f);
            _expandAnim += (expandTarget - _expandAnim) * et;

            float centerY = _mobiFramePx.Top + (_mobiFramePx.Height - winSize.Y) / 2f;
            float animatedY = baseY + (centerY - baseY) * _expandAnim;

            var windowPos = new Vector2(centerX, animatedY);

            var style = ImGui.GetStyle();
            Vector4 baseWindowBg = style.Colors[(int)ImGuiCol.WindowBg];
            Vector4 baseChildBg = style.Colors[(int)ImGuiCol.ChildBg];

            if (webForBg)
            {
                ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0f, 0f, 0f, 0f));
            }

            ImGui.SetNextWindowSize(winSize, ImGuiCond.Always);
            ImGui.SetNextWindowPos(windowPos, ImGuiCond.Always, new Vector2(0f, 0f));

            ImGui.Begin("##StarboardMain",
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoTitleBar);

            ImDrawListPtr dlWindow = ImGui.GetWindowDrawList();
            if (webForBg)
            {
                dlWindow.ChannelsSplit(2);
                dlWindow.ChannelsSetCurrent(1);
            }

            // Hover / click handling
            bool hoveredThisFrame = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByPopup);
            bool clickedLeft = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
            bool uiCapturingMouse = io.WantCaptureMouse;


            if (hoveredThisFrame)
                interactedThisFrame = true;

            if (hoveredThisFrame &&
                (ImGui.IsMouseDown(ImGuiMouseButton.Left) ||
                 ImGui.IsMouseDown(ImGuiMouseButton.Right) ||
                 ImGui.IsMouseDown(ImGuiMouseButton.Middle)))
            {
                interactedThisFrame = true;
            }

            if (_isExpanded)
                interactedThisFrame = true;

            if (clickedLeft)
            {
                if (hoveredThisFrame)
                {
                    if (!_isExpanded) _isExpanded = true;
                }
                else
                {
                    if (_gameIsForeground && !uiCapturingMouse)
                    {
                        _selectedAppletIndex = -1;
                        selectedApplet = null;
                        isWebApplet = false;
                        WebBrowserManager.SetActiveApplet(null);
                        _isExpanded = false;
                    }
                }
            }


            float windowWidth = ImGui.GetWindowSize().X;

            if (_cassioTex != IntPtr.Zero)
            {
                float iconH = 64f * _dpiScale;
                var iconSize = new Vector2(iconH, iconH);
                float iconX = (windowWidth - iconH) * 0.025f;
                ImGui.SetCursorPosX(iconX);
                ImGui.Image(_cassioTex, iconSize);
            }

            ImGui.SameLine();

            float textHeight = ImGui.GetTextLineHeight();
            float yOffset = 38f * _dpiScale - textHeight;
            if (yOffset < 0) yOffset = 0;
            var cursor = ImGui.GetCursorPos();
            ImGui.SetCursorPosY(cursor.Y + yOffset);

            unsafe
            {
                if (_orbiBoldFont.NativePtr != null)
                    ImGui.PushFont(_orbiBoldFont);
            }
            ImGui.Text("Starboard");
            unsafe
            {
                if (_orbiBoldFont.NativePtr != null)
                    ImGui.PopFont();
            }

            ImGui.Dummy(new Vector2(0f, 8f * _dpiScale));

            float marginX = 20f * _dpiScale;
            float marginBottom = 20f * _dpiScale;
            float gapBetween = 20f * _dpiScale;

            Vector2 panelStart = ImGui.GetCursorPos();
            panelStart.X += marginX;
            ImGui.SetCursorPos(panelStart);

            float availWidth = ImGui.GetContentRegionAvail().X - marginX - gapBetween;
            float availHeight = ImGui.GetContentRegionAvail().Y - marginBottom;
            if (availHeight < 0) availHeight = 0;

            const float phoneAspectHOverW = 19.5f / 9f;
            float leftWidth = availHeight / phoneAspectHOverW;
            float maxLeftWidth = availWidth * 0.5f;
            if (leftWidth > maxLeftWidth) leftWidth = maxLeftWidth;
            float rightWidth = Math.Max(0f, availWidth - leftWidth);
            float rightTopOffset = 60f * _dpiScale;
            float rightHeight = availHeight + rightTopOffset;

            float buttonHeight = 32f * _dpiScale;
            float buttonSpacing = 8f * _dpiScale;

            float leftHeight = Math.Max(0f, availHeight - (buttonHeight + buttonSpacing));

            Vector2 rightPos = panelStart - new Vector2(0f, 60f);
            rightPos.X += leftWidth + gapBetween;

            // -----------------------------------------------------------------
            // COOKIE-CUT BACKGROUND FOR WEB APPLETS (uses frozen webForBg)
            // -----------------------------------------------------------------
            if (webForBg)
            {
                dlWindow.ChannelsSetCurrent(0); // background channel

                Vector2 winPosScreen = ImGui.GetWindowPos();
                Vector2 winSizeScreen = ImGui.GetWindowSize();

                Vector2 cardMin = winPosScreen;
                Vector2 cardMax = winPosScreen + winSizeScreen;

                Vector2 holeMin = winPosScreen + rightPos;
                Vector2 holeMax = holeMin + new Vector2(rightWidth, rightHeight);

                uint bgCol = ImGui.GetColorU32(baseWindowBg);
                uint borderCol = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.35f));
                float rounding = 10f * _dpiScale;

                // NOTE: rounding = 0 here to avoid tiny triangle gaps
                // Left bar
                dlWindow.AddRectFilled(
                    cardMin,
                    new Vector2(holeMin.X, cardMax.Y),
                    bgCol,
                    0.0f);

                // Top bar
                dlWindow.AddRectFilled(
                    new Vector2(holeMin.X, cardMin.Y),
                    new Vector2(cardMax.X, holeMin.Y),
                    bgCol,
                    0.0f);

                // Right bar
                dlWindow.AddRectFilled(
                    new Vector2(holeMax.X, cardMin.Y),
                    cardMax,
                    bgCol,
                    0.0f);

                // Bottom bar
                dlWindow.AddRectFilled(
                    new Vector2(holeMin.X, holeMax.Y),
                    new Vector2(holeMax.X, cardMax.Y),
                    bgCol,
                    0.0f);

                dlWindow.AddRect(cardMin, cardMax, borderCol, rounding, ImDrawFlags.None, 2f * _dpiScale);

                dlWindow.ChannelsSetCurrent(1);
            }

            ImGui.PushStyleColor(ImGuiCol.ChildBg, baseChildBg);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f * _dpiScale);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 2f * _dpiScale);

            ImGui.BeginChild(
                "##StarboardLeftPanel",
                new Vector2(leftWidth, leftHeight),
                ImGuiChildFlags.Borders,
                ImGuiWindowFlags.None);

            Vector2 leftPanelPos = ImGui.GetWindowPos();
            Vector2 leftPanelSize = ImGui.GetWindowSize();

            var leftRect = new RectangleF(
                leftPanelPos.X,
                leftPanelPos.Y,
                leftPanelSize.X,
                leftPanelSize.Y);

            var newLuaPaths = FileDropManager.ProcessExternalAppletDrops(leftRect);
            int newlyAddedApplets = 0;

            if (newLuaPaths != null && newLuaPaths.Count > 0)
            {
                foreach (var luaPath in newLuaPaths)
                {
                    try
                    {
                        var newLuaApplet = new LuaApplet(luaPath);
                        _applets.Add(newLuaApplet);
                        newLuaApplet.Initialize();
                        newlyAddedApplets++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"[StarboardMain] Failed to load dropped LuaApplet from '{luaPath}': {ex.Message}");
                    }
                }

                if (newlyAddedApplets > 0)
                {
                    SortApplets();

                    _importInProgress = true;
                    _importProgress = 0;
                    _lastImportCount = newlyAddedApplets;
                }
            }

            var dlLeft = ImGui.GetWindowDrawList();
            float rowHeight = 40f * _dpiScale;

            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0f, 0f, 0f, 0f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0f, 0f, 0f, 0f));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0f, 0f, 0f, 0f));

            for (int i = 0; i < _applets.Count; i++)
            {
                var applet = _applets[i];
                bool rowSelected = (i == _selectedAppletIndex);

                ImGui.PushID(i);

                if (ImGui.Selectable("##appletRow", rowSelected,
                        ImGuiSelectableFlags.None,
                        new Vector2(0, rowHeight)))
                {
                    interactedThisFrame = true;

                    _selectedAppletIndex = i;

                    if (applet.UsesWebView)
                        WebBrowserManager.SetActiveApplet(applet.Id);
                    else
                        WebBrowserManager.SetActiveApplet(null);

                    selectedApplet = applet;
                    isWebApplet = applet.UsesWebView;
                }

                bool hoveredRow = ImGui.IsItemHovered();
                Vector2 rowMin = ImGui.GetItemRectMin();
                Vector2 rowMax = ImGui.GetItemRectMax();

                if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
                {
                    unsafe
                    {
                        int indexForPayload = i;
                        ImGui.SetDragDropPayload(
                            "SB_APPLET_INDEX",
                            new IntPtr(&indexForPayload),
                            sizeof(int));
                    }

                    string dragLabel = applet.DisplayName ?? "<unnamed>";

                    unsafe
                    {
                        if (_orbiRegFont.NativePtr != null)
                            ImGui.PushFont(_orbiRegFont);
                    }
                    ImGui.Text(dragLabel);
                    unsafe
                    {
                        if (_orbiRegFont.NativePtr != null)
                            ImGui.PopFont();
                    }

                    ImGui.EndDragDropSource();
                }

                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload("SB_APPLET_INDEX");
                    unsafe
                    {
                        if (payload.NativePtr != null)
                        {
                            unsafe
                            {
                                int srcIndex = *(int*)payload.Data.ToPointer();
                                if (srcIndex >= 0 && srcIndex < _applets.Count && srcIndex != i)
                                {
                                    var moved = _applets[srcIndex];
                                    _applets.RemoveAt(srcIndex);

                                    if (srcIndex < i)
                                        i--;

                                    _applets.Insert(i, moved);
                                    _selectedAppletIndex = i;

                                    AppletOrderStore.SaveOrder(_applets);
                                }
                            }
                        }
                    }

                    ImGui.EndDragDropTarget();
                }

                Vector4 bgColRow = rowSelected
                    ? new Vector4(1f, 1f, 1f, 0.08f)
                    : (hoveredRow ? new Vector4(1f, 1f, 1f, 0.04f) : new Vector4(0f, 0f, 0f, 0f));

                if (bgColRow.W > 0f)
                {
                    dlLeft.AddRectFilled(
                        rowMin,
                        rowMax,
                        ImGui.GetColorU32(bgColRow),
                        6f * _dpiScale);
                }

                float iconSizePx = 32f * _dpiScale;
                float iconPadX = 8f * _dpiScale;
                float iconPadY = (rowHeight - iconSizePx) * 0.5f;

                Vector2 iconMin = rowMin + new Vector2(iconPadX, iconPadY);
                Vector2 iconMax = iconMin + new Vector2(iconSizePx, iconSizePx);

                IntPtr favTex = FaviconManager.GetOrRequest(applet.Id, applet.FaviconUrl);

                if (favTex != IntPtr.Zero)
                {
                    dlLeft.AddImage(
                        favTex,
                        iconMin,
                        iconMax,
                        new Vector2(0, 0),
                        new Vector2(1, 1),
                        ImGui.GetColorU32(new Vector4(1, 1, 1, 1)));
                }
                else
                {
                    uint iconCol = ImGui.GetColorU32(new Vector4(0.85f, 0.85f, 0.9f, 1f));
                    dlLeft.AddRect(iconMin, iconMax, iconCol, 6f * _dpiScale, ImDrawFlags.None, 2f);
                }

                string name = applet.DisplayName ?? "<unnamed>";
                float textYRow = rowMin.Y + (rowHeight - ImGui.GetTextLineHeight()) * 0.5f;
                float textXRow = iconMax.X + 8f * _dpiScale;

                unsafe
                {
                    if (_orbiRegFont.NativePtr != null)
                        ImGui.PushFont(_orbiRegFont);
                }
                dlLeft.AddText(
                    new Vector2(textXRow, textYRow),
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.95f)),
                    name);
                unsafe
                {
                    if (_orbiRegFont.NativePtr != null)
                        ImGui.PopFont();
                }

                ImGui.PopID();
            }

            float remainingY = ImGui.GetContentRegionAvail().Y;
            float epsilon = 5f * _dpiScale;
            if (remainingY > rowHeight + epsilon)
            {
                ImGui.Dummy(new Vector2(0f, remainingY - rowHeight - epsilon));
            }

            ImGui.PushID("AddAppletRow");

            bool addClicked = false;
            Vector2 addRowMin, addRowMax;

            if (_importInProgress)
            {
                ImGui.Selectable("##importRow", false,
                    ImGuiSelectableFlags.None,
                    new Vector2(0, rowHeight));

                addRowMin = ImGui.GetItemRectMin();
                addRowMax = ImGui.GetItemRectMax();

                _importProgress += dt * 2.0f;
                if (_importProgress >= 1f)
                {
                    _importProgress = 1f;
                    _importInProgress = false;
                }

                float fullWidth = addRowMax.X - addRowMin.X;
                float fillWidth = fullWidth * _importProgress;

                uint bgCol = ImGui.GetColorU32(new Vector4(0.2f, 0.7f, 0.3f, 0.25f));
                uint fillCol = ImGui.GetColorU32(new Vector4(0.2f, 0.9f, 0.4f, 0.9f));

                dlLeft.AddRectFilled(addRowMin, addRowMax, bgCol, 6f * _dpiScale);
                dlLeft.AddRectFilled(
                    addRowMin,
                    new Vector2(addRowMin.X + fillWidth, addRowMax.Y),
                    fillCol,
                    6f * _dpiScale);

                string label = $"Imported {_lastImportCount} applet(s)...";
                Vector2 textSize = ImGui.CalcTextSize(label);
                Vector2 textPos = new(
                    addRowMin.X + (fullWidth - textSize.X) * 0.5f,
                    addRowMin.Y + (rowHeight - textSize.Y) * 0.5f);

                dlLeft.AddText(textPos, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)), label);
            }
            else
            {
                addClicked = ImGui.Selectable("##addAppletRow", false,
                    ImGuiSelectableFlags.None,
                    new Vector2(0, rowHeight));

                addRowMin = ImGui.GetItemRectMin();
                addRowMax = ImGui.GetItemRectMax();

                bool addHovered = ImGui.IsItemHovered();
                bool deleteHighlight = false;

                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload(
                        "SB_APPLET_INDEX",
                        ImGuiDragDropFlags.AcceptBeforeDelivery);

                    unsafe
                    {
                        if (payload.NativePtr != null)
                        {
                            unsafe
                            {
                                int srcIndex = *(int*)payload.Data.ToPointer();

                                if (srcIndex >= 0 && srcIndex < _applets.Count &&
                                    _applets[srcIndex] is LuaApplet luaApplet)
                                {
                                    deleteHighlight = true;

                                    if (payload.IsDelivery())
                                    {
                                        var scriptPath = luaApplet.ScriptPath;
                                        if (!string.IsNullOrEmpty(scriptPath))
                                        {
                                            _selectedAppletIndex = -1;
                                            selectedApplet = null;
                                            isWebApplet = false;
                                            WebBrowserManager.SetActiveApplet(null);
                                            try
                                            {
                                                if (File.Exists(scriptPath))
                                                {
                                                    Logger.Info($"[StarboardMain] Deleting Lua applet file: '{scriptPath}'.");
                                                    File.Delete(scriptPath);
                                                }
                                                else
                                                {
                                                    Logger.Info($"[StarboardMain] Lua applet file '{scriptPath}' not found on disk (already removed?).");
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                Logger.Warn($"[StarboardMain] Failed to delete lua applet file '{scriptPath}': {ex.Message}");
                                            }
                                        }

                                        var removed = _applets[srcIndex];
                                        _applets.RemoveAt(srcIndex);
                                        Logger.Info($"[StarboardMain] Removed Lua applet '{removed.DisplayName}' via delete zone.");

                                        if (_selectedAppletIndex == srcIndex)
                                            _selectedAppletIndex = -1;
                                        else if (_selectedAppletIndex > srcIndex)
                                            _selectedAppletIndex--;

                                        AppletOrderStore.SaveOrder(_applets);
                                    }
                                }
                            }
                        }
                    }

                    ImGui.EndDragDropTarget();


                }

                Vector4 bgVec;
                Vector4 iconVec;
                bool drawMinus = deleteHighlight; 

                if (deleteHighlight)
                {
                    bgVec = new Vector4(0.9f, 0.1f, 0.1f, 0.55f);
                    iconVec = new Vector4(1f, 0.9f, 0.9f, 1f);
                }
                else
                {
                    bgVec = addHovered
                        ? new Vector4(0.2f, 0.5f, 1f, 0.25f)   
                        : new Vector4(1f, 1f, 1f, 0.03f);      

                    iconVec = addHovered
                        ? new Vector4(0.8f, 0.9f, 1f, 1f)
                        : new Vector4(1f, 1f, 1f, 0.8f);
                }

                uint bgCol = ImGui.GetColorU32(bgVec);
                dlLeft.AddRectFilled(addRowMin, addRowMax, bgCol, 6f * _dpiScale);

                Vector2 center = (addRowMin + addRowMax) * 0.5f;
                float halfSize = rowHeight * 0.25f;
                uint iconCol = ImGui.GetColorU32(iconVec);

                dlLeft.AddLine(
                    new Vector2(center.X - halfSize, center.Y),
                    new Vector2(center.X + halfSize, center.Y),
                    iconCol,
                    2f * _dpiScale);

                if (!drawMinus)
                {
                    dlLeft.AddLine(
                        new Vector2(center.X, center.Y - halfSize),
                        new Vector2(center.X, center.Y + halfSize),
                        iconCol,
                        2f * _dpiScale);
                }
            }


            ImGui.PopID();

            if (addClicked && !_importInProgress)
            {
                try
                {
                    using (var ofd = new OpenFileDialog())
                    {
                        ofd.Filter = "Lua applets (*.lua)|*.lua|All files (*.*)|*.*";
                        ofd.Multiselect = true;
                        ofd.Title = "Select Lua applet(s) to import";

                        if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            var externDir = Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "Starboard",
                                "ExternApplets");

                            Directory.CreateDirectory(externDir);

                            var importedPaths = new List<string>();
                            foreach (var src in ofd.FileNames)
                            {
                                if (!string.Equals(Path.GetExtension(src), ".lua", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                var destPath = Path.Combine(externDir, Path.GetFileName(src));
                                try
                                {
                                    File.Copy(src, destPath, overwrite: true);
                                    importedPaths.Add(destPath);
                                }
                                catch (Exception ex)
                                {
                                    Logger.Warn($"[StarboardMain] Failed to import Lua applet '{src}' -> '{destPath}': {ex.Message}");
                                }
                            }

                            int added = 0;
                            foreach (var p in importedPaths)
                            {
                                try
                                {
                                    var newLuaApplet = new LuaApplet(p);
                                    _applets.Add(newLuaApplet);
                                    newLuaApplet.Initialize();
                                    added++;
                                }
                                catch (Exception ex)
                                {
                                    Logger.Warn($"[StarboardMain] Failed to load imported Lua applet from '{p}': {ex.Message}");
                                }
                            }

                            if (added > 0)
                            {
                                SortApplets();
                                _importInProgress = true;
                                _importProgress = 0f;
                                _lastImportCount = added;

                                AppletOrderStore.SaveOrder(_applets);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[StarboardMain] Error during Lua applet import: {ex.Message}");
                }
            }


            ImGui.PopStyleColor(3);
            ImGui.EndChild();

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor();

            Vector2 buttonsPos = new Vector2(
                panelStart.X,
                panelStart.Y + leftHeight + buttonSpacing);

            ImGui.SetCursorPos(buttonsPos);

            unsafe
            {
                float padX = 16f * _dpiScale * 2f;

                ImFontPtr bigFont = _orbiRegFont;
                ImFontPtr smallFont = _orbiRegFontSmall;

                float backBig = bigFont.CalcTextSizeA(bigFont.FontSize, float.MaxValue, 0f, "Back").X + padX;
                float homeBig = bigFont.CalcTextSizeA(bigFont.FontSize, float.MaxValue, 0f, "Home").X + padX;
                float setBig = bigFont.CalcTextSizeA(bigFont.FontSize, float.MaxValue, 0f, "Settings").X + padX;
                float totalBig = backBig + homeBig + setBig + (ImGui.GetStyle().ItemSpacing.X * 2f);

                float backSm = smallFont.CalcTextSizeA(smallFont.FontSize, float.MaxValue, 0f, "Back").X + padX;
                float homeSm = smallFont.CalcTextSizeA(smallFont.FontSize, float.MaxValue, 0f, "Home").X + padX;
                float setSm = smallFont.CalcTextSizeA(smallFont.FontSize, float.MaxValue, 0f, "Settings").X + padX;
                float totalSmall = backSm + homeSm + setSm + (ImGui.GetStyle().ItemSpacing.X * 2f);

                float spaceToRightPanel = rightPos.X - buttonsPos.X;

                bool useSmallFont = totalBig > spaceToRightPanel;

                if (useSmallFont && smallFont.NativePtr != null)
                    ImGui.PushFont(smallFont);
                else if (!useSmallFont && bigFont.NativePtr != null)
                    ImGui.PushFont(bigFont);
            }

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f * _dpiScale);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(16f * _dpiScale, 6f * _dpiScale));

            bool hasActiveApplet = selectedApplet != null;
            bool canGoBack = hasActiveApplet && selectedApplet!.UsesWebView && WebBrowserManager.ActiveCanGoBack();

            ImGui.BeginDisabled(!canGoBack);
            if (ImGui.Button("Back"))
            {
                interactedThisFrame = true;
                WebBrowserManager.GoBackOnActiveApplet();
            }
            ImGui.EndDisabled();

            ImGui.SameLine();

            if (ImGui.Button("Home"))
            {
                interactedThisFrame = true;
                _selectedAppletIndex = -1;
                selectedApplet = null;
                isWebApplet = false;
                WebBrowserManager.SetActiveApplet(null);
            }

            ImGui.SameLine();

            if (ImGui.Button("Settings"))
            {
                interactedThisFrame = true;
                FirstStartWindow.OpenToPage(1);
            }

            ImGui.PopStyleVar(2);
            ImGui.PopFont();

            ImGui.SetCursorPos(rightPos);

            ImGui.PushStyleColor(ImGuiCol.ChildBg, isWebApplet ? new Vector4(0, 0, 0, 0) : baseChildBg);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f * _dpiScale);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 2f * _dpiScale);

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,
                isWebApplet ? Vector2.Zero : style.WindowPadding);

            ImGui.BeginChild(
                "##StarboardRightPanel",
                new Vector2(rightWidth, rightHeight),
                ImGuiChildFlags.Borders,
                ImGuiWindowFlags.None);

            unsafe
            {
                if (_orbiRegFont.NativePtr != null)
                    ImGui.PushFont(_orbiRegFont);
            }

            if (selectedApplet != null)
            {
                Vector2 avail = ImGui.GetContentRegionAvail();

                ImGui.BeginChild(
                    "##StarboardAppletSandbox",
                    avail,
                    ImGuiChildFlags.None,
                    ImGuiWindowFlags.None);

                Vector2 sandboxSize = ImGui.GetContentRegionAvail();
                selectedApplet.Draw(dt, sandboxSize);

                ImGui.EndChild();
            }
            else
            {
                var dlRight = ImGui.GetWindowDrawList();
                Vector2 winPos = ImGui.GetWindowPos();
                Vector2 childSize = ImGui.GetWindowSize();
                Vector2 center = winPos + childSize * 0.5f;

                // how far in from each corner
                float cornerPadding = 60f * _dpiScale;
                float innerFactor = 0.65f;
                float thickness = 3f * _dpiScale;
                uint lineCol = ImGui.GetColorU32(ImGuiCol.Border);

                Vector2 topLeftOuter = winPos + new Vector2(cornerPadding, cornerPadding);
                Vector2 topRightOuter = winPos + new Vector2(childSize.X - cornerPadding, cornerPadding);
                Vector2 bottomLeftOuter = winPos + new Vector2(cornerPadding, childSize.Y - cornerPadding);
                Vector2 bottomRightOuter = winPos + new Vector2(childSize.X - cornerPadding, childSize.Y - cornerPadding);

                Vector2 topLeftInner = topLeftOuter + (center - topLeftOuter) * innerFactor;
                Vector2 topRightInner = topRightOuter + (center - topRightOuter) * innerFactor;
                Vector2 bottomLeftInner = bottomLeftOuter + (center - bottomLeftOuter) * innerFactor;
                Vector2 bottomRightInner = bottomRightOuter + (center - bottomRightOuter) * innerFactor;

                dlRight.AddLine(topLeftOuter, topLeftInner, lineCol, thickness);
                dlRight.AddLine(topRightOuter, topRightInner, lineCol, thickness);
                dlRight.AddLine(bottomLeftOuter, bottomLeftInner, lineCol, thickness);
                dlRight.AddLine(bottomRightOuter, bottomRightInner, lineCol, thickness);

                const string placeholder = "Select an applet on the left to begin.";
                Vector2 textSize = ImGui.CalcTextSize(placeholder);
                Vector2 textPosLocal = (childSize - textSize) * 0.5f;

                ImGui.SetCursorPos(textPosLocal);
                ImGui.TextDisabled(placeholder);
            }

            if (selectedApplet is LuaApplet luaSelected &&
                luaSelected.TryGetErrorForDisplay(out var errMsg, out var errAlpha) &&
                errAlpha > 0f)
            {
                DrawAppletErrorToast(errMsg, errAlpha);
            }

            unsafe
            {
                if (_orbiRegFont.NativePtr != null)
                    ImGui.PopFont();
            }


            ImGui.EndChild();

            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor();

            _wasHoveredLastFrame = hoveredThisFrame;

            if (webForBg)
            {
                dlWindow.ChannelsMerge();
                ImGui.PopStyleColor();
            }

            HitTestRegions.AddCurrentWindow();

            if (interactedThisFrame)
            {
                _secondsSinceInteraction = 0f;
            }
            else
            {
                _secondsSinceInteraction += dt;
            }

            ImGui.End();
            ImGui.PopStyleVar();
        }
    }
}

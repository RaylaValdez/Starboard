using ImGuiNET;
using Overlay_Renderer.Methods;
using Starboard.DistributedApplets;
using Starboard.GuiElements;
using System.Drawing;
using System.Numerics;
using System.Linq;

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
                catch
                {
                }
            }

            _applets.Sort((a, b) =>
                string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

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

        public static void Draw(float dt, float globalAlpha)
        {
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
            bool hoveredThisFrame = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.NoPopupHierarchy);
            bool clickedLeft = ImGui.IsMouseClicked(ImGuiMouseButton.Left);

            if (hoveredThisFrame)
                interactedThisFrame = true;

            if (hoveredThisFrame &&
                (ImGui.IsMouseDown(ImGuiMouseButton.Left) ||
                 ImGui.IsMouseDown(ImGuiMouseButton.Right) ||
                 ImGui.IsMouseDown(ImGuiMouseButton.Middle)))
            {
                interactedThisFrame = true;
            }

            if (clickedLeft)
            {
                if (hoveredThisFrame)
                {
                    if (!_isExpanded) _isExpanded = true;
                }
                else
                {
                    _selectedAppletIndex = -1;
                    selectedApplet = null;
                    isWebApplet = false;
                    WebBrowserManager.SetActiveApplet(null);
                    _isExpanded = false;
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

            ImGui.PushStyleColor(ImGuiCol.ChildBg, isWebApplet ? new Vector4(0, 0, 0, 0) : baseChildBg);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f * _dpiScale);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 2f * _dpiScale);

            ImGui.BeginChild(
                "##StarboardLeftPanel",
                new Vector2(leftWidth, leftHeight),
                ImGuiChildFlags.Borders,
                ImGuiWindowFlags.None);

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
            bool canGoBack = hasActiveApplet &&
                             selectedApplet!.UsesWebView &&
                             WebBrowserManager.ActiveCanGoBack();

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

            // pop style + font
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
                Vector2 innerSize = ImGui.GetContentRegionAvail();
                selectedApplet.Draw(dt, innerSize);
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

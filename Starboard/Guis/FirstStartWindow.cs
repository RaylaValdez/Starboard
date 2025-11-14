using ImGuiNET;
using Overlay_Renderer.Methods;
using Starboard.GuiElements;
using System.Drawing;
using System.Numerics;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Starboard.Guis;

internal static class FirstStartWindow
{
    private static Rectangle _mobiFramePx;
    private static float _dpiScale = 1f;
    private static IntPtr _cassioTex = IntPtr.Zero;
    private static ImFontPtr _orbiBoldFont;
    private static ImFontPtr _orbiRegFont;
    private static ImFontPtr _orbiRegFontSmall;
    private static int pageNumber = 0;

    private static VIRTUAL_KEY _openMobiglassVk;
    private static ImGuiKey _openMobiglassImGui;
    private static VIRTUAL_KEY _openMobiMapVk;
    private static ImGuiKey _openMobiMapImGui;
    private static VIRTUAL_KEY _openMobiCommsVk;
    private static ImGuiKey _openMobiCommsImGui;

    private static bool _usesJoypad;
    private static bool _firstRunComplete;

    private static float _idleCloseSeconds = 15f;

    private static List<ControllerBinding> _openMobiglassControllerBinds = new();
    private static List<ControllerBinding> _openMobimapControllerBinds = new();
    private static List<ControllerBinding> _openMobicommsControllerBinds = new();

    public static void SetMobiFrame(Rectangle mobiFrame)
    {
        _mobiFramePx = mobiFrame;
    }

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
        _cassioTex = cassioTex;
        _orbiBoldFont = fontBold;
        _orbiRegFont = font;
        _orbiRegFontSmall = smallFont;

        StarboardSettingsStore.Load();

        _openMobiglassVk = StarboardSettingsStore.Current.OpenMobiglassKeybindVk;
        _openMobiglassImGui = StarboardSettingsStore.Current.OpenMobiglassKeybind;

        _openMobiMapVk = StarboardSettingsStore.Current.OpenMobimapKeybindVk;
        _openMobiMapImGui = StarboardSettingsStore.Current.OpenMobiMapKeybind;

        _openMobiCommsVk = StarboardSettingsStore.Current.OpenMobiCommsKeybindVk;
        _openMobiCommsImGui = StarboardSettingsStore.Current.OpenMobiCommsKeybind;

        _usesJoypad = StarboardSettingsStore.Current.UsesJoyPad;
        _firstRunComplete = StarboardSettingsStore.Current.FirstRunCompleted;

        _idleCloseSeconds = StarboardSettingsStore.Current.IdleCloseSeconds > 0 ? StarboardSettingsStore.Current.IdleCloseSeconds : 15f;

        _openMobiglassControllerBinds =
            StarboardSettingsStore.Current.OpenMobiglassControllerBinds ?? new List<ControllerBinding> { new ControllerBinding() };
        _openMobimapControllerBinds =
            StarboardSettingsStore.Current.OpenMobimapControllerBinds ?? new List<ControllerBinding> { new ControllerBinding() };
        _openMobicommsControllerBinds =
            StarboardSettingsStore.Current.OpenMobicommsControllerBinds ?? new List<ControllerBinding> { new ControllerBinding() };

        if (_openMobiglassControllerBinds.Count == 0) _openMobiglassControllerBinds.Add(new ControllerBinding());
        if (_openMobimapControllerBinds.Count == 0) _openMobimapControllerBinds.Add(new ControllerBinding());
        if (_openMobicommsControllerBinds.Count == 0) _openMobicommsControllerBinds.Add(new ControllerBinding());
    }

    public static void Draw()
    {
        var winSize = new Vector2(_mobiFramePx.Width / 3, _mobiFramePx.Height * 0.9f);
        var centerX = _mobiFramePx.Left + (_mobiFramePx.Width - winSize.X) / 2;
        var centerY = _mobiFramePx.Top + (_mobiFramePx.Height - winSize.Y) / 2;
        var centerPos = new Vector2(centerX, centerY);

        ImGui.SetNextWindowSize(winSize, ImGuiCond.Always);
        ImGui.SetNextWindowPos(centerPos, ImGuiCond.Always, new Vector2(0f, 0f));

        ImGui.Begin("##FirstStart",
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoTitleBar);

        float windowWidth = ImGui.GetWindowSize().X;
        float windowHeight = ImGui.GetWindowSize().Y;
        float padding = 18f * _dpiScale;

        Vector2 buttonSize = new Vector2(100 * _dpiScale, 28 * _dpiScale);
        Vector2 controllerButtonSize = new Vector2(90 * _dpiScale, 28 * _dpiScale);
        Vector2 toggleSize = new Vector2(66 * _dpiScale, 28 * _dpiScale);

        float buttonY = windowHeight - buttonSize.Y / 1.5f - padding;

        float leftButtonX = padding;
        float rightButtonX = windowWidth - buttonSize.X - padding;
        float toggleX = windowWidth - toggleSize.X - padding;

        switch (pageNumber)
        {
            case 0:
            {
                if (_cassioTex != IntPtr.Zero)
                {
                    float iconH = 96f * _dpiScale;
                    float iconW = iconH;
                    var iconSize = new Vector2(iconW, iconH);

                    float iconX = (windowWidth - iconW) / 2;

                    ImGui.SetCursorPosX(iconX);
                    ImGui.Image(_cassioTex, iconSize);
                }

                string title = "Welcome to Starboard";
                unsafe
                {
                    if (_orbiBoldFont.NativePtr != null)
                        ImGui.PushFont(_orbiBoldFont);
                }

                var textSize = ImGui.CalcTextSize(title);
                float windowCenter = ImGui.GetWindowSize().X / 2;
                ImGui.SetCursorPosX(windowCenter - textSize.X / 2);
                ImGui.Text(title);
                unsafe
                {
                    if (_orbiBoldFont.NativePtr != null)
                        ImGui.PopFont();
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                unsafe
                {
                    if (_orbiRegFont.NativePtr != null)
                        ImGui.PushFont(_orbiRegFont);
                }
                ImGui.TextWrapped("Starboard is installed correctly.");
                ImGui.NewLine();
                ImGui.TextWrapped("Please read the following Disclaimer.");

                var scrollSize = new Vector2(
                    windowWidth - padding,
                    windowHeight * 0.5f
                );

                ImGui.SetCursorPosY(windowHeight * 0.35f);
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0f, 0f, 0f, 0f));

                ImGui.BeginChild("##scrollText", scrollSize, ImGuiChildFlags.None, ImGuiWindowFlags.AlwaysVerticalScrollbar);

                ImGui.Spacing();

                ImGui.TextWrapped("Starboard is an external overlay and does not modify, inject into, or read the memory of any game process.");

                ImGui.Spacing();
                ImGui.TextWrapped("'Star Citizen' and related marks are trademarks of Cloud Imperium Games. Starboard is an independent community project and is not affiliated with or endorsed by CIG/RSI.");

                ImGui.Spacing();
                ImGui.TextWrapped("Starboard and its related software are provided 'as-is' without any warranty or guarantee of safety, functionality, or compatibility with third-party applications or services.");

                ImGui.Spacing();
                ImGui.TextWrapped("By using this software, you acknowledge that you do so at your own risk. The developers and contributors of Starboard are not responsible for any account actions, suspensions, bans, or penalties that may result from its use.");

                ImGui.Spacing();
                ImGui.TextWrapped("If you choose to continue using Starboard, you agree that you are solely responsible for any outcomes related to its use.");

                ImGui.EndChild();
                ImGui.PopStyleColor();
                unsafe
                {
                    if (_orbiRegFont.NativePtr != null)
                        ImGui.PopFont();
                }
                break;
            }

            case 1:
            {
                if (_cassioTex != IntPtr.Zero)
                {
                    float iconH = 96f * _dpiScale;
                    float iconW = iconH;
                    var iconSize = new Vector2(iconW, iconH);

                    float iconX = (windowWidth - iconW) / 2;

                    ImGui.SetCursorPosX(iconX);
                    ImGui.Image(_cassioTex, iconSize);
                }

                string title = "Keyboard Button Assigment";
                unsafe
                {
                    if (_orbiBoldFont.NativePtr != null)
                        ImGui.PushFont(_orbiBoldFont);
                }

                var textSize = ImGui.CalcTextSize(title);
                float windowCenter = ImGui.GetWindowSize().X / 2;
                ImGui.SetCursorPosX(windowCenter - textSize.X / 2);
                ImGui.Text(title);
                unsafe
                {
                    if (_orbiBoldFont.NativePtr != null)
                        ImGui.PopFont();
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                unsafe
                {
                    if (_orbiRegFont.NativePtr != null)
                        ImGui.PushFont(_orbiRegFont);
                }

                ImGui.TextWrapped("Starboard needs to know when you open your mobiglass.");
                ImGui.Spacing();
                ImGui.TextWrapped("If your Mobiglass binds are different, update them here!");

                ImGui.SetCursorPosY(windowHeight * 0.4f);
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.Text("Mobiglass:");
                ImGui.SameLine();
                ImGui.SetCursorPosX(windowWidth - buttonSize.X - padding);

                if (HotkeyPicker.Draw("Open Mobiglass", buttonSize, ref _openMobiglassVk, ref _openMobiglassImGui))
                {
                    StarboardSettingsStore.Current.OpenMobiglassKeybindVk = _openMobiglassVk;
                    StarboardSettingsStore.Current.OpenMobiglassKeybind = _openMobiglassImGui;
                    StarboardSettingsStore.Save();
                }

                ImGui.NewLine();
                ImGui.Text("Mobiglass Map:");
                ImGui.SameLine();
                ImGui.SetCursorPosX(windowWidth - buttonSize.X - padding);
                if (HotkeyPicker.Draw("Open Mobiglass Map", buttonSize, ref _openMobiMapVk, ref _openMobiMapImGui))
                {
                    StarboardSettingsStore.Current.OpenMobimapKeybindVk = _openMobiMapVk;
                    StarboardSettingsStore.Current.OpenMobiMapKeybind = _openMobiMapImGui;
                    StarboardSettingsStore.Save();
                }

                ImGui.NewLine();
                ImGui.Text("Mobiglass Comms:");
                ImGui.SameLine();
                ImGui.SetCursorPosX(windowWidth - buttonSize.X - padding);
                if (HotkeyPicker.Draw("Open Mobiglass Comms", buttonSize, ref _openMobiCommsVk, ref _openMobiCommsImGui))
                {
                    StarboardSettingsStore.Current.OpenMobiCommsKeybindVk = _openMobiCommsVk;
                    StarboardSettingsStore.Current.OpenMobiCommsKeybind = _openMobiCommsImGui;
                    StarboardSettingsStore.Save();
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextWrapped("Auto-close Starboard after inactivity:");

                float minSeconds = 5f;
                float maxSeconds = 300f;

                float idleSeconds = _idleCloseSeconds;

                float sliderWidth = buttonSize.X * 2f;
                ImGui.SetCursorPosX(windowWidth - sliderWidth - padding);
                ImGui.PushItemWidth(sliderWidth);

                if (ImGui.SliderFloat("##IdleTimeout", ref idleSeconds, minSeconds, maxSeconds, "%.0f seconds"))
                {
                    idleSeconds = Math.Clamp(idleSeconds, minSeconds, maxSeconds);
                    _idleCloseSeconds = idleSeconds;
                    StarboardSettingsStore.Current.IdleCloseSeconds = _idleCloseSeconds;
                    StarboardSettingsStore.Save();
                }

                ImGui.PopItemWidth();

                ImGui.SetCursorPosY(windowHeight * 0.8f);
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextWrapped("Do you open your Mobiglass with a gamepad/joystick?");

                ImGui.SetCursorPos(new Vector2(toggleX, windowHeight * 0.85f));
                unsafe
                {
                    if (_orbiRegFont.NativePtr != null)
                        ImGui.PopFont();
                }

                unsafe
                {
                    if (_orbiRegFontSmall.NativePtr != null)
                        ImGui.PushFont(_orbiRegFontSmall);
                }

                if (ToggleSwitch.Draw("UsesJoypad", toggleSize, ref _usesJoypad, "YES", "NO"))
                {
                    StarboardSettingsStore.Current.UsesJoyPad = _usesJoypad;
                    StarboardSettingsStore.Save();
                }


                unsafe
                {
                    if (_orbiRegFontSmall.NativePtr != null)
                        ImGui.PopFont();
                }

                break;
            }

            case 2:
            {
                if (_cassioTex != IntPtr.Zero)
                {
                    float iconH = 96f * _dpiScale;
                    float iconW = iconH;
                    var iconSize = new Vector2(iconW, iconH);

                    float iconX = (windowWidth - iconW) / 2;

                    ImGui.SetCursorPosX(iconX);
                    ImGui.Image(_cassioTex, iconSize);
                }

                string title = "Controller Assignment";
                unsafe
                {
                    if (_orbiBoldFont.NativePtr != null)
                        ImGui.PushFont(_orbiBoldFont);
                }

                var textSize = ImGui.CalcTextSize(title);
                float windowCenter = ImGui.GetWindowSize().X / 2;
                ImGui.SetCursorPosX(windowCenter - textSize.X / 2);
                ImGui.Text(title);
                unsafe
                {
                    if (_orbiBoldFont.NativePtr != null)
                        ImGui.PopFont();
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                unsafe
                {
                    if (_orbiRegFont.NativePtr != null)
                        ImGui.PushFont(_orbiRegFont);
                }

                ImGui.TextWrapped("Starboard needs to know when you open your mobiglass.");
                ImGui.Spacing();
                ImGui.TextWrapped("If your controller bindings for Mobiglass are different, you can update them!");
                ImGui.Spacing();
                ImGui.TextWrapped("You can add binds from multiple devices, just click +.");
                ImGui.Spacing();
                ImGui.TextWrapped("To remove a binding, right click it.");

                ImGui.SetCursorPosY(windowHeight * 0.52f);
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                bool changed = false;

                ImGui.Text("Mobiglass:");
                ImGui.SameLine();
                changed |= DrawControllerBindingList(
                    "OpenMobiglassController",
                    _openMobiglassControllerBinds,
                    controllerButtonSize
                );

                ImGui.NewLine();
                ImGui.Text("Mobiglass Map:");
                ImGui.SameLine();
                changed |= DrawControllerBindingList(
                    "OpenMobimapController",
                    _openMobimapControllerBinds,
                    controllerButtonSize
                );

                ImGui.NewLine();
                ImGui.Text("Mobiglass Comms:");
                ImGui.SameLine();
                changed |= DrawControllerBindingList(
                    "OpenMobicommsController",
                    _openMobicommsControllerBinds,
                    controllerButtonSize
                );

                if (changed)
                {
                    StarboardSettingsStore.Current.OpenMobiglassControllerBinds = _openMobiglassControllerBinds;
                    StarboardSettingsStore.Current.OpenMobimapControllerBinds = _openMobimapControllerBinds;
                    StarboardSettingsStore.Current.OpenMobicommsControllerBinds = _openMobicommsControllerBinds;
                    StarboardSettingsStore.Save();
                }

                unsafe
                {
                    if (_orbiRegFont.NativePtr != null)
                        ImGui.PopFont();
                }
                break;
            }
        }

        // Bottom buttons
        ImGui.SetCursorPosY(buttonY - padding * 0.5f);
        ImGui.Separator();
        ImGui.Spacing();

        unsafe
        {
            if (_orbiRegFont.NativePtr != null)
                ImGui.PushFont(_orbiRegFont);
        }

        if (pageNumber > 0)
        {
            ImGui.SetCursorPos(new Vector2(leftButtonX, buttonY));
            if (ImGui.Button("Back", buttonSize))
                pageNumber--;
        }

        ImGui.SetCursorPos(new Vector2(rightButtonX, buttonY));
        if (ImGui.Button("Continue", buttonSize))
        {
            // Explicit wizard flow:
            // 0 -> 1
            // 1 -> 2 if using controller, else complete
            // 2 -> complete
            if (pageNumber == 0)
            {
                pageNumber = 1;
            }
            else if (pageNumber == 1)
            {
                if (_usesJoypad)
                    pageNumber = 2;
                else
                    CompleteFirstRun();
            }
            else if (pageNumber == 2)
            {
                CompleteFirstRun();
            }
        }

        unsafe
        {
            if (_orbiRegFont.NativePtr != null)
                ImGui.PopFont();
        }

        HitTestRegions.AddCurrentWindow();
        ImGui.End();
    }

    private static bool DrawControllerBindingList(
    string idPrefix,
    List<ControllerBinding> binds,
    Vector2 buttonSize)
    {
        bool changed = false;
        ImGui.PushID(idPrefix);

        var style = ImGui.GetStyle();

        float rowStartX = ImGui.GetCursorPosX();
        float rowStartY = ImGui.GetCursorPosY();

        float availWidth = ImGui.GetContentRegionAvail().X;
        if (availWidth <= 0f)
        {
            ImGui.PopID();
            return false;
        }

        float rowHeight = buttonSize.Y + style.FramePadding.Y * 2f;

        float plusTextWidth = ImGui.CalcTextSize("+").X;
        float plusWidth = plusTextWidth + style.FramePadding.X * 2f;
        float gap = plusWidth * 0.5f;

        float buttonsRegionWidth = availWidth - plusWidth - gap;
        if (buttonsRegionWidth < 1f)
            buttonsRegionWidth = 1f;

        float buttonsTotalWidth = 0f;
        if (binds.Count > 0)
        {
            buttonsTotalWidth =
                binds.Count * buttonSize.X +
                (binds.Count - 1) * style.ItemInnerSpacing.X;
        }

        float contentWidth = Math.Max(buttonsRegionWidth, buttonsTotalWidth);

        // ----- SCROLLABLE CHILD (RIGHT-ALIGNED, INVISIBLE SCROLLBAR) -----
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        ImGui.SetNextWindowContentSize(new Vector2(contentWidth, rowHeight));

        ImGui.BeginChild(
            "##buttonsScroll",
            new Vector2(buttonsRegionWidth, rowHeight),
            ImGuiChildFlags.None,
            ImGuiWindowFlags.NoScrollbar);

        float startX = 0f;
        if (buttonsTotalWidth < buttonsRegionWidth)
            startX = buttonsRegionWidth - buttonsTotalWidth;

        ImGui.SetCursorPosX(startX);

        for (int i = 0; i < binds.Count; i++)
        {
            if (i > 0)
                ImGui.SameLine();

            ImGui.PushID(i);
            if (ControllerButtonPicker.Draw("##controllerbind", buttonSize, binds[i]))
            {
                changed = true;
            }

            if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                if (i > 1)
                {
                    binds.RemoveAt(i);
                    i--;
                    changed = true;
                }
            }

            ImGui.PopID();
        }

        ImGui.EndChild();

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(5);

        float plusX = rowStartX + availWidth - plusWidth;
        ImGui.SetCursorPos(new Vector2(plusX, rowStartY));

        if (ImGui.Button("+", new Vector2(plusWidth, rowHeight * 0.8f)))
        {
            binds.Add(new ControllerBinding());
            changed = true;
        }

        ImGui.PopID();
        return changed;
    }

    public static void OpenToPage(int page)
    {
        pageNumber = Math.Max(1, page);
        AppState.ShowFirstStart = true;
    }

    public static void Close()
    {
        AppState.ShowFirstStart = false;
    }

    private static void CompleteFirstRun()
    {
        _firstRunComplete = true;
        StarboardSettingsStore.Current.FirstRunCompleted = true;
        StarboardSettingsStore.Save();

        AppState.FirstRunCompleted = true;
        AppState.ShowPlayground = true;
        AppState.ShowFirstStart = false;
    }
}

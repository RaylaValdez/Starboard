using ImGuiNET;
using Overlay_Renderer.Methods;
using Starboard.GuiElements;
using System.Drawing;
using System.Net.NetworkInformation;
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

    public static void SetMobiFrame(Rectangle mobiFrame)
    {
        _mobiFramePx = mobiFrame;
    }

    public static void Initialize(IntPtr cassioTex, float dpiScale, Rectangle mobiFrame, ImFontPtr fontBold, ImFontPtr font, ImFontPtr smallFont)
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
    }


    public static void Draw()
    {
        // Computed Middle Rect
        var winSize = new Vector2(_mobiFramePx.Width / 3, _mobiFramePx.Height * 0.9f);
        var centerX = _mobiFramePx.Left + (_mobiFramePx.Width - winSize.X) / 2;
        var centerY = _mobiFramePx.Top + (_mobiFramePx.Height - winSize.Y) / 2;
        var centerPos = new Vector2(centerX, centerY);

        ImGui.SetNextWindowSize(winSize, ImGuiCond.Always);
        ImGui.SetNextWindowPos(centerPos, ImGuiCond.Always, new Vector2(0f,0f));

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
        Vector2 toggleSize = new Vector2(66 * _dpiScale, 28 * _dpiScale);


        float buttonY = windowHeight - buttonSize.Y / 1.5f - padding;
        float toggleY = windowHeight - toggleSize.Y / 1.5f - padding;


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
                
                ImGui.Text("Open Mobiglass:");
                ImGui.SameLine();
                ImGui.SetCursorPosX(windowWidth - buttonSize.X - padding);

                if (HotkeyPicker.Draw("Open Mobiglass", buttonSize, ref _openMobiglassVk, ref _openMobiglassImGui))
                {
                    StarboardSettingsStore.Current.OpenMobiglassKeybindVk = _openMobiglassVk;
                    StarboardSettingsStore.Current.OpenMobiglassKeybind = _openMobiglassImGui;
                    StarboardSettingsStore.Save();
                }

                ImGui.NewLine();
                ImGui.Text("Open Mobiglass Map:");
                ImGui.SameLine();
                ImGui.SetCursorPosX(windowWidth - buttonSize.X - padding);
                if (HotkeyPicker.Draw("Open Mobiglass Map", buttonSize, ref _openMobiMapVk, ref _openMobiMapImGui))
                {
                    StarboardSettingsStore.Current.OpenMobimapKeybindVk = _openMobiMapVk;
                    StarboardSettingsStore.Current.OpenMobiMapKeybind = _openMobiMapImGui;
                    StarboardSettingsStore.Save();
                }

                ImGui.NewLine();
                ImGui.Text("Open Mobiglass Comms:");
                ImGui.SameLine();
                ImGui.SetCursorPosX(windowWidth - buttonSize.X - padding);
                if (HotkeyPicker.Draw("Open Mobiglass Comms", buttonSize, ref _openMobiCommsVk, ref _openMobiCommsImGui))
                {
                    StarboardSettingsStore.Current.OpenMobiCommsKeybindVk = _openMobiCommsVk;
                    StarboardSettingsStore.Current.OpenMobiCommsKeybind = _openMobiCommsImGui;
                    StarboardSettingsStore.Save();
                }

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
        }
        
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

        ImGui.SetCursorPos(new(rightButtonX, buttonY));
        if (ImGui.Button("Continue", buttonSize))
        {
            if (pageNumber < 2)
            {
                pageNumber++;
            }

            if (pageNumber > 1)
            {
                if (!_usesJoypad)
                {
                    _firstRunComplete = true;
                    StarboardSettingsStore.Current.FirstRunCompleted = true;
                    StarboardSettingsStore.Save();
                    AppState.ShowPlayground = true;
                }
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
}

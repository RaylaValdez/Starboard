# 💫 Starboard
Starboard is a transparent, topmost, click-through overlay that renders a single ImGui frame on top of Star Citizen. It will allow devs to build external tools (applets) that render inside a designated ImGui child. Because it’s non-embedded and process-agnostic, it stays anti-cheat friendly.

---

## ✨ Features


![Demo](https://raw.githubusercontent.com/RaylaValdez/Starboard/main/Starboard/Assets/StarboardDemonstration.gif)

- First Start Setup to discover your Mobiglass bindings. 
- User made applets easy thanks to ImGui!
- No more tabbing in and out of the game, you can access all the information you'd need right in game, Starboard opens when you open your Mobiglass!
- Preloaded with handy community made websites.
- Lua API for writing applets!
- DragNDrop importing of luaapplets!
- Full Fledged lua editor applet for making applets live!

![EditorDemo](https://github.com/RaylaValdez/Starboard/blob/main/Starboard/Assets/Gifs/EditorDemonstration.gif?raw=true)

---

## ❌ Known Issues

- Due to Starboard not hooking or reading any memory from Star Citizen, mobiglass detection isn't solid. 
- The main Developer (me) is VERY dumb.


---

## 🧰 Technologies

- **.NET 8.0**
- **C# 11**
- **Direct3D 11** via [Win32 interop (CsWin32)](https://github.com/microsoft/CsWin32)
- **ImGui.NET** for GUI rendering
- **DirectComposition** for hardware-accelerated transparency
- **Windows API** (`HWND`, `RECT`, `SetWindowRgn`, `WM_NCHITTEST`, etc.)

---

## 🚀 Usage

### Star Citizen Must be fully loaded prior to launching Starboard

You can either;

1. Download the installer from the latest release, follow the install wizard and launch from Start Menu or Desktop (if you made a shortcut)

OR

1. Download the binaries for manual extraction, also from the latest release.
2. Extract them into a folder of your choice.
3. Run Starboard.exe.

### Starboard may function weirdly in Fullscreen, use Borderless Fullscreen wherever possible.

Optionaly, enable game sounds when game is in background, so you can hear the game while you browse your applets!

---

## 🧰 Development

### Requirements
- Visual Studio 2022 or Rider
- .NET 8 SDK
- Windows 10+ with Direct3D 11
- [Overlay-Renderer](https://github.com/RaylaValdez/Overlay-Renderer)

---

## ⚖️ License

Apache-2.0 License

---

## ❗Disclaimer

Starboard is an external overlay and does not modify, inject into, or read the memory of any game process. “Star Citizen” and related marks are trademarks of Cloud Imperium Games. Starboard is an independent community project and is not affiliated with or endorsed by CIG/RSI.

---

## 💬 Credits

Built with ❤️ by **Rayla**  
Powered by [Overlay-Renderer](https://github.com/RaylaValdez/Overlay-Renderer).

---

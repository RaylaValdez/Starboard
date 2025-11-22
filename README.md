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

---

## ❌ Known Issues

- Cursor flicker when hovering over WebView2 region
- Due to Starboard not hooking or reading any memory from Star Citizen, mobiglass detection isn't solid. 


---

## 🧰 Technologies

- **.NET 8.0**
- **C# 11**
- **Direct3D 11** via [Win32 interop (CsWin32)](https://github.com/microsoft/CsWin32)
- **ImGui.NET** for GUI rendering
- **DirectComposition** for hardware-accelerated transparency
- **Windows API** (`HWND`, `RECT`, `SetWindowRgn`, `WM_NCHITTEST`, etc.)

---

## 🚀 Build instructions

### 1. Cloning
Clone **Starboard** and **Overlay-Renderer** into the same parent folder (so relative paths match):

```bash
git clone https://github.com/RaylaValdez/Overlay-Renderer.git
git clone https://github.com/RaylaValdez/Starboard.git
```

---

### 2. Add the project reference
Add **Overlay-Renderer** to your Starboard solution:

```
Right-click your solution → Add → Existing Project → Browse to Overlay-Renderer/Overlay-Renderer.csproj
```

Then add it as a project reference:

```
Right-click Starboard → Add → Project Reference → check "Overlay-Renderer"
```

---

### 3. Install required NuGet packages
Starboard and Overlay-Renderer rely on a few NuGet dependencies:

| Package | Used for |
|----------|-----------|
| `Vortice.Direct3D11` | Direct3D11 bindings |
| `Vortice.DXGI` | DXGI swap-chain / adapter handling |
| `Vortice.D3DCompiler` | Runtime shader compilation |
| `ImGui.NET` | ImGui C# bindings |
| `Svg` | SVG rasterization for favicons |
| `System.Drawing.Common` | Bitmap manipulation |
| `Microsoft.Web.WebView2` | (if you’re using WebViewSurface) embedded browser support |

Run:
```bash
dotnet restore
```
or from Visual Studio, open the **Package Manager Console** and run:
```powershell
Update-Package -reinstall
```

---

### 4. Folder layout
Your directory structure should look like this:

```
Projects/
 ├─ Overlay-Renderer/
 │   └─ Overlay-Renderer.csproj
 └─ Starboard/
     └─ Starboard.csproj
```

If you keep them in different roots, fix the project reference path manually in the `.csproj`.

---

### 5. Assets
Ensure these folders exist under **Starboard/**:
```
Assets/
 ├─ Fonts/
 │   └─ Orbitron/...
 ├─ Icons/
 │   └─ cassiopia.png / cassiopia.ico
 └─ favicons/   (created automatically at runtime)
```
You can replace these with your own artwork, just keep file names consistent.

---

### 6. Configuration
Starboard loads `StarboardSettingsStore.json` on launch.  
If it doesn’t exist, it’ll be created with defaults, but you can copy one from a working install.

---

### 7. Build
Set **Starboard** as the startup project.

Select **x64 Debug** or **x64 Release**, then:

```bash
Ctrl + Shift + B
```

or from CLI:

```bash
dotnet build Starboard.sln -c Release
```

---

### 8. Run
Launch **Starboard** (it will automatically attach to Star Citizen if running).

You must start Star Citizen before Starboard.

---

### 9. Optional (recommended)
- **Enable unsafe code** in both projects (ImGui interop requires it).  
- **Disable “Prefer 32-bit”** in project properties.  
- If you’re testing WebView, make sure **Microsoft Edge WebView2 Runtime** is installed.

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

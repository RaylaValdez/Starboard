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

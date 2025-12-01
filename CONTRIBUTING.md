# 🤝 Contributing to Starboard

Hey there, and welcome!  
If you’re reading this, you’re at least *thinking* about helping out — which is awesome. Starboard is a transparent overlay framework for Star Citizen built with C#, ImGui.NET, and Direct3D11, designed to let devs create applets (Lua or C#) that render safely on top of the game.

This document covers how to contribute, what’s expected, and a few guardrails to keep things tidy.

---

## 🧰 Getting Set Up

Before diving in, make sure you can build the project.  
See the **Build Instructions** section in the [BUILDINSTRUCTIONS](./BUILDINSTRUCTIONS.md) — it’ll walk you through cloning both **Starboard** and **Overlay-Renderer**, restoring packages, and verifying your environment.

Quick checklist:
- ✅ Visual Studio 2022 or Rider  
- ✅ .NET 10.0 + Windows SDK 10.0.19041+  
- ✅ Windows 10 or newer  
- ✅ Overlay-Renderer cloned and referenced  
- ✅ Unsafe code enabled in both projects  

Once you can launch Starboard and see the overlay appear in Star Citizen, you’re good to go.

---

## 🧩 What You Can Contribute

We’re open to anything that improves the project without turning it into a kitchen sink. Some examples:

- 🪄 **New Lua API bindings** (`LuaUiApi`, `LuaSysApi`, etc.)
- 💡 **New applets**
- 🎨 **UI tweaks** that keep the aesthetic consistent
- ⚙️ **Performance improvements** (rendering, memory usage, D3D interop)
- 🐛 **Bug fixes** and code cleanup
- 📖 **Documentation** and examples

If you’re unsure whether an idea fits, open a **Discussion** or **Issue** first — we’re happy to chat before you spend time coding.

---

## ✍️ Coding Style

Please try to match the existing conventions:

- Use **PascalCase** for methods and properties, **camelCase** for locals.
- Explicit types preferred over `var` unless obvious.
- Keep files focused — one class per file unless small helpers.
- Document public methods with XML `<summary>` comments.
- Group methods alphabetically or by functionality.
- No trailing whitespace, unused usings, or commented-out code.
- Tabs = 4 spaces, UTF-8 encoding, LF line endings.

When touching unsafe or interop code:
- Keep P/Invoke centralized through `Windows.Win32` (CsWin32-generated).
- Avoid adding unmanaged code unless absolutely necessary.
- Check for existing helpers before rolling your own.

---

## 🧪 Pull Request Workflow

All work should be done in branches.

Then:

1. Build and test locally (no warnings or errors).
2. Make sure Starboard starts cleanly and attaches to Star Citizen.
3. If you touched the Lua API or added new applets, verify they load correctly.
4. Update docs or README snippets if needed.
5. Commit with a clear, descriptive message:

```Add Lua wrapper for ImGui.AcceptDragDropPayload()```

6. Push your branch and open a Pull Request to main.

Pull requests are reviewed for:

- Code clarity and maintainability
- Performance or memory impact
- Visual/UI consistency
- API design compatibility
- Adherence to project goals (lightweight, external, anti-cheat safe)

---

## 🧙 Applet Development Guidelines

Applets are the fun part — they’re what make Starboard feel alive.
When creating or modifying one:

- Use the existing Lua API (ui, sys, etc.) or create wrappers under Starboard.Lua.
- Avoid blocking calls, busy loops, or heavy file I/O.
- Respect DPI scaling and don’t assume fixed resolutions.
- Keep CPU/GPU load minimal — Starboard runs every frame.
- If your applet uses WebView, ensure you’ve got WebView2 runtime installed.

Each new applet should include a one-line summary in its README and a small screenshot or GIF if possible.

---

## 🐞 Reporting Bugs

When reporting issues, please include:

- Windows version and build number
- GPU + driver version
- Starboard version
- A relevant log file from /Logs/ (e.g. OverlayRenderer_2025-12-01.log)
- Repro steps and screenshots if it’s visual

Logs and context save everyone time.

---

## ⚖️ Licensing and Legal

By contributing code, you agree that it’s licensed under the same license as Starboard.
Starboard is designed for use with Star Citizen but is not affiliated with or endorsed by Cloud Imperium Games.

---

## 💬 Code of Conduct

Be decent. Treat others like you’d want to be treated in a pull request.
Constructive feedback is always welcome — rudeness isn’t.

---

## 🎉 That’s It!

If you’ve made it this far, you’re already ahead of most contributors.
Fork it, experiment, build cool applets, and make Starboard even better.
And hey — if you do something awesome, show it off in a PR or post a demo GIF!

Happy coding,
– Rayla ✨

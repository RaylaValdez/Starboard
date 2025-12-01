# 🔒 Security Policy

## Supported Versions

Only the most recent release of **Starboard** is actively maintained and receives security updates.  
Older builds are considered archived and may not receive patches.

| Version | Supported          |
| -------- | ------------------ |
| Latest (main branch) | ✅ |
| Older releases | ❌ |

If you're using an outdated version, please update to the latest release or rebuild from `main`.

---

## Reporting a Vulnerability

If you discover a security issue, **please do not open a public GitHub issue**.

Instead, use GitHub’s built-in **Private Vulnerability Reporting** feature:

1. Go to the [Starboard repository](https://github.com/RaylaValdez/Starboard).  
2. Click **Security** → **Report a vulnerability**.  
3. Fill out the form with:
   - A clear description of the issue  
   - Steps to reproduce (if possible)  
   - The Starboard version and your environment (Windows version, GPU, etc.)

You’ll receive a confirmation within **48 hours**, and updates will follow as the issue is verified and addressed.  
Once a fix is ready, a patched release will be published, and the advisory will be made public after resolution.

---

## Scope

Starboard doesn’t handle personal user data or online authentication.  
Security concerns should focus on:
- Overlay safety and injection handling  
- Lua sandbox integrity and script isolation  
- WebView2 content security  
- Unsafe or unmanaged code paths that could affect host stability  

---

## Disclosure Policy

- Please report privately through the GitHub Security form.  
- Give maintainers reasonable time to investigate and release a fix before public disclosure.  
- You may be credited in the changelog if you wish (optional).  

---

Thank you for helping keep Starboard safe and reliable for everyone. 🛡️

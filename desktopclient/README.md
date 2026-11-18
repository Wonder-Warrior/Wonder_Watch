# WonderWatch Desktop Client (Windows x64)

Native Windows desktop client for **WonderWatch** powered by Microsoft Edge WebView2, with **uBlock Origin** bundled and automatically activated on first boot.

## Features
- **Built-in uBlock Origin (`extensions/ublock-origin`)**: Automatically sideloaded and enabled in the isolated app profile before the first page load.
- **First-Boot Ready**: Filter lists initialize on first launch — no browser extension store setup required.
- **Native Dark Immersion**: Dark Windows 10/11 titlebar (`#050608`), zero white startup flash, and full hardware-accelerated playback.
- **Popup & Ad-Tab Shield**: Blocks unsolicited embed popups while preserving Firebase/Google account sign-in popups.
- **Fullscreen & Shortcuts**:
  - `F11` — Toggle borderless fullscreen (also supports player fullscreen)
  - `Ctrl+Shift+U` — Open built-in uBlock Origin Shield Dashboard
  - `Alt+H` — Return to WonderWatch Home
  - `F5` / `Ctrl+R` — Reload

## Quick Start
1. Extract `WonderWatch-Desktop-win-x64.zip` (or run directly from `desktopclient/`).
2. Launch **`WonderWatch.exe`**.
   - Keep `Microsoft.Web.WebView2.Core.dll`, `Microsoft.Web.WebView2.WinForms.dll`, `WebView2Loader.dll`, and the `extensions/` folder next to `WonderWatch.exe`.

## Building from Source
Requires Windows 10/11 (uses the built-in .NET Framework 4.x C# compiler, no external SDK install needed):

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

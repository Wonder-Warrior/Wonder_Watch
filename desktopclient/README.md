# WonderWatch Desktop Client (Windows x64)

Dedicated native Windows desktop client for **WonderWatch**, built with WebView2.

## Features
- **Built-in uBlock Origin Ad & Popup Shield**: Pre-bundled and activated automatically on launch with zero setup (`Ctrl+Shift+U` to open shield settings).
- **Hardware-Accelerated Video Engine**: High-performance GPU preference enabled automatically for smooth 1080p 60fps playback.
- **Seamless Auto-Fullscreen Across Episodes**: Holds fullscreen behind a dark episode transition curtain and automatically re-engages the player's native fullscreen controls across all stream sources.
- **Discord Rich Presence**: Displays the anime and episode you are currently watching on Discord.
- **Custom In-App Updater**: Built-in dark update modal with version comparison, explicit **Update Now / Later** choice, and live download progress.
- **Dark Immersion**: Tailored Windows 10/11 dark title bar and animated startup splash with zero white flash.
- **Borderless Fullscreen**: One-key toggle with `F11` (automatically synchronized with HTML5 player fullscreen).
- **Persistent Window State**: Remembers your last window size, position, and maximized state across restarts.
- **Quick Shortcuts**:
  - `F11` — Toggle borderless fullscreen
  - `Alt+H` — Return to WonderWatch Home
  - `F5` / `Ctrl+R` — Reload page
  - `Ctrl+Shift+U` — Shield settings

## Installation & Running
- **Installer (Recommended)**: Run **`WonderWatch-Setup-win-x64.exe`** to install WonderWatch with Start Menu & Desktop shortcuts.
- **Portable**: Run **`WonderWatch.exe`** directly from the `desktopclient` folder (requires Microsoft Edge WebView2 Runtime, pre-installed on Windows 11 and modern Windows 10).

## Building from Source
Run `powershell -ExecutionPolicy Bypass -File desktopclient\build.ps1` from the repository root to compile `WonderWatch.exe`, bump `version.json`, and package `WonderWatch-Setup-win-x64.exe`.

# GitTool

GitTool `0.1.5` is a native Windows Git utility built with C#, .NET 10 LTS, WinUI 3, and the stable Windows App SDK. It provides focused clone and repository-management workflows in a continuous Mica Alt shell.

This is a vibe-coded app built to learn about agentic development and support my internal use of a Git GUI.

## Included workflows

- Clone public GitHub repositories by probing `ssh -T git@github.com` non-interactively. A confirmed GitHub authentication uses SSH; every other GitHub clone falls back to HTTPS.
- Choose the clone destination and optionally initialize/fetch submodules recursively.
- Select and validate a local Git working tree, then inspect its branch, origin, and changed-file count.
- Fetch all remotes, pull with `--ff-only`, update submodules when requested, and push committed changes.
- Block the application surface for the full lifetime of every Git operation, including submodule work.
- Cancel active Git work from the blocking overlay or window close confirmation; process trees are stopped and app-created partial clone folders are removed safely.
- Show live operation text, native taskbar activity/error badges, in-app error dialogs, and optional Windows notifications.
- Render with Per-Monitor V2 DPI awareness so WinUI redraws crisply when the window moves between displays with different scaling.
- Store centralized defaults and buffered logs under `%LOCALAPPDATA%\GitTool`.

Logs are queued in memory and written only every five minutes, when an error occurs, and when the app closes. Fourteen daily log files are retained.

## Requirements

- Windows 11 for the full Mica Alt appearance (Windows 10 20H1 or later is the package minimum).
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) — this solution is pinned to `10.0.302` in `global.json`.
- Visual Studio with the **WinUI application development** workload when using the designer or packaged F5 deployment.
- [Git for Windows](https://git-scm.com/downloads/win), available on `PATH`.
- Optional: an SSH key/agent accepted by GitHub for SSH cloning.

The project uses Windows App SDK `2.2.0` (stable) and Windows SDK build tools `10.0.28000.1721`. NuGet restores both packages for the Visual Studio project.

The Windows App SDK is deployed self-contained, so development and packaged builds do not require a separately installed Windows App Runtime 2.2 framework.

## Build

Open `GitTool.sln`, choose `x64`, set `GitTool.App` as the startup project, and run it with Visual Studio.

The shared `GitTool` solution launch profile starts only `GitTool.App`; `GitTool.Core` is a class library and cannot be launched directly.

From PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

Build an unsigned test MSIX:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release -Platform x64 -Package
```

The package is written under `src\GitTool.App\AppPackages`. Visual Studio can deploy the app for development. A distributed MSIX must be signed with a certificate whose subject matches the package publisher, or signed by the Microsoft Store.

When dependencies have already been restored and the machine is temporarily offline, add `-SkipRestore` to either command.

## Repository layout

- `src/GitTool.App` — WinUI 3 shell, pages, dialogs, pickers, notifications, and taskbar badges.
- `src/GitTool.Core` — process execution, Git URL selection, clone/inspection, operation registry, settings, and logging.
- `tests/GitTool.Core.Tests` — dependency-free executable checks for URL normalization, process-tree cancellation, safe clone cleanup, coordinator reuse, and local fetch/pull/push behavior.
- `docs/ARCHITECTURE.md` — extension points and operation lifecycle.

All process arguments are passed through `ProcessStartInfo.ArgumentList`; repository paths and URLs are never concatenated into a command shell string.

## Design references

- [WinUI Gallery](https://github.com/microsoft/WinUI-Gallery)
- [Windows app design guidelines](https://learn.microsoft.com/windows/apps/design/)
- [System backdrops and Mica](https://learn.microsoft.com/windows/apps/develop/ui/system-backdrops)
- [High-DPI desktop application development](https://learn.microsoft.com/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows)
- [Windows notifications overview](https://learn.microsoft.com/windows/apps/develop/notifications/)

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
- Show live operation text, native taskbar activity/error badges, in-app error dialogs, and optional Windows notifications when operations finish while GitTool is in the background.
- Render with Per-Monitor V2 DPI awareness so WinUI redraws crisply when the window moves between displays with different scaling.
- Store unpackaged settings and logs under `%LOCALAPPDATA%\GitTool`; packaged
  runs use MSIX-managed LocalState so Windows removes them on uninstall.

Logs are queued in memory and written only every five minutes, when an error occurs, and when the app closes. Fourteen daily log files are retained.

## Requirements

- Windows 11 for the full Mica Alt appearance (Windows 10 20H1 or later is the package minimum).
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) — this solution is pinned to `10.0.302` in `global.json`.
- Visual Studio with the **WinUI application development** workload when using the designer or packaged F5 deployment.
- [Git for Windows](https://git-scm.com/downloads/win), available on `PATH`.
- Optional: an SSH key/agent accepted by GitHub for SSH cloning.

The project uses Windows App SDK `2.2.0` (stable) and Windows SDK build tools `10.0.28000.1721`. NuGet restores both packages for the Visual Studio project.

Manual unsigned MSIX builds are framework-dependent and use the installed Windows App Runtime 2.2 framework, Main, and Singleton packages. This gives packaged runs access to the broker components required by `AppNotificationManager`. Elevated administrator processes cannot send Windows app notifications, so install from elevated PowerShell but launch GitTool normally.

## Build

Open `GitTool.sln`, choose `x64`, set `GitTool.App` as the startup project, and run it with Visual Studio.

The shared `GitTool` solution launch profile starts only `GitTool.App`; `GitTool.Core` is a class library and cannot be launched directly.

Build the default self-contained standalone app from PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release -Platform x64 -Target Standalone
```

The executable and all required files are written to
`artifacts\standalone\Release\x64`. Keep the complete folder together when
sharing it; only the app's own installation is avoided, while Git for Windows
must still be available on `PATH`.

Build an unsigned MSIX manually:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release -Platform x64 -Target Msix-Unsigned
```

The package is written under `src\GitTool.App\AppPackages`. Its identity and
Start menu name are selected from the current branch:

| Branch | App name |
| --- | --- |
| `master` | GitTool |
| `development` | GitTool Development |
| `experimental/*` | GitTool Experimental |

MSIX builds are intentionally manual for now. GitHub Actions publishes only
self-contained standalone ZIPs. Visual Studio can deploy the app for
development. A distributed MSIX must be signed with a certificate whose subject
matches the package publisher, or signed by the Microsoft Store.

An unsigned MSIX is local to its builder: its publisher and package identity
are derived from the current Windows username instead of repository metadata.
The package also carries the Windows-required unsigned-publisher namespace
marker; it contains no repository owner or hardcoded personal identity.
It also contains `BuildInfo.json` with the app version, UTC build time, local
builder and machine, branch, commit, configuration, platform, and package
identity. The local builder increments the fourth MSIX version component so a
new package upgrades an installed development build without removing its app
data. The standalone and CI builds do not include this file.
Because Windows forbids executable activation extensions in unsigned MSIX
packages, unsigned builds use the packaged no-COM toast path. Test and
background completion notifications still display, but notification clicks and
inputs cannot activate GitTool inside its existing process. A future signed
package can restore the COM activator for full notification activation.

On Windows 11, you can install an unsigned package directly with
`Add-AppxPackage`, or use the optional generic helper from an elevated
PowerShell session:

```powershell
.\scripts\install-unsigned-msix.ps1
```

The helper selects the newest local MSIX by default. Pass `-PackagePath` to
select a specific package. It reads the MSIX manifest, so it supports all three
GitTool package flavors without a branch-specific script name. To remove a
package and its package-managed LocalState, run:

```powershell
.\scripts\uninstall-unsigned-msix.ps1 -PackagePath .\src\GitTool.App\AppPackages\<package>.msix
```

Launch the app from the Start menu without elevation. Its
settings and logs are stored under the package's LocalState. On the first
packaged launch, existing `%LOCALAPPDATA%\GitTool` data is moved into that
package-managed location.

When dependencies have already been restored and the machine is temporarily offline, add `-SkipRestore` to either command.

## Repository layout

- `src/GitTool.App` — WinUI 3 shell, pages, dialogs, pickers, notifications, and taskbar badges.
- `src/GitTool.Core` — process execution, Git URL selection, clone/inspection, operation registry, settings, and logging.
- `tests/GitTool.Core.Tests` — dependency-free executable checks for URL normalization, process-tree cancellation, safe clone cleanup, coordinator reuse, and local fetch/pull/push behavior.
- `tests/GitTool.App.Tests` — fake-platform checks for notification capability, registration, system policy, foreground suppression, delivery failure, and shutdown behavior.
- `docs/ARCHITECTURE.md` — extension points and operation lifecycle.

All process arguments are passed through `ProcessStartInfo.ArgumentList`; repository paths and URLs are never concatenated into a command shell string.

## Design references

- [WinUI Gallery](https://github.com/microsoft/WinUI-Gallery)
- [Windows app design guidelines](https://learn.microsoft.com/windows/apps/design/)
- [System backdrops and Mica](https://learn.microsoft.com/windows/apps/develop/ui/system-backdrops)
- [High-DPI desktop application development](https://learn.microsoft.com/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows)
- [Windows notifications overview](https://learn.microsoft.com/windows/apps/develop/notifications/)
- [Windows App SDK deployment architecture](https://learn.microsoft.com/windows/apps/windows-app-sdk/deployment-architecture#singleton-package)
- [Self-contained Windows App SDK deployment](https://learn.microsoft.com/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)

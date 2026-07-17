# Architecture and extension guide

## Operation lifecycle

`UserOperationService` is the UI boundary for long-running work. It asks `OperationCoordinator` for the single global operation slot, applies an activity badge, and leaves the whole window blocked until the returned task completes. A failed result is logged immediately, changes the taskbar badge to Error, sends an optional Windows notification, and opens a diagnostic `ContentDialog`.

The window listens to `OperationCoordinator.StatusChanged`. This keeps status presentation separate from Git execution and guarantees that clone, pull, and any submodule follow-up remain one indivisible UI operation.

## Git execution

`ProcessRunner` is the only raw process boundary. It uses `ProcessStartInfo.ArgumentList`, redirects standard output/error, streams progress lines, caps captured diagnostics, and supports timeouts. No operation invokes PowerShell, `cmd.exe`, or a shell-built command string.

`GitClient` owns clone and repository inspection:

1. `GitHubSshProbe` runs a non-interactive, time-limited GitHub SSH check.
2. `GitUrlResolver` normalizes GitHub HTTPS, SSH, and `owner/repository` inputs.
3. SSH is selected only when GitHub reports successful authentication; otherwise HTTPS is selected.
4. The destination path is normalized and checked before `git clone` starts.

## Adding repository operations

Fetch, pull, and push implement `IRepositoryOperation`. To add commit, add, branch, stash, or another repository action:

1. Add an `IRepositoryOperation` implementation in `src/GitTool.Core/Git`.
2. Execute Git through `IGitCommandExecutor` so output and failures use the common result model.
3. Register the implementation in `AppServices.RepositoryOperationRegistry`.
4. Add the corresponding icon-and-label control to `RepositoryPage`, calling the registry by its operation key.

Options shared by operations belong in `RepositoryOperationOptions`. This is why submodule behavior can already be used by both fetch and pull without coupling it to either page button.

## Settings and logs

`JsonSettingsStore` writes `%LOCALAPPDATA%\GitTool\settings.json` using a temporary-file replacement. `BufferedFileLogger` queues informational entries and flushes on a five-minute timer. Errors force an immediate flush, and window shutdown performs a final synchronous wait for the logger to finish.

The package installation directory is read-only, so writable app data and logs intentionally live together under `%LOCALAPPDATA%\GitTool`.

## Packaging notes

The app is a single-project MSIX WinUI 3 application. Package identity enables native taskbar badge glyphs and Windows integration. Badge and notification calls are guarded so unsupported shells do not interrupt Git operations; the in-app status and error dialog remain the authoritative fallback.

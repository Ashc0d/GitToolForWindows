# Architecture and extension guide

## Operation lifecycle

`UserOperationService` is the UI boundary for long-running work. It asks `OperationCoordinator` for the single global operation slot, applies an activity badge, and leaves the whole window blocked until the returned task completes. Every final result is offered to `AppNotificationService`; it sends a completion notification only when notifications are enabled and the window is not in the foreground. Failed results are also logged immediately, change the taskbar badge to Error, and open a diagnostic `ContentDialog`.

The window listens to `OperationCoordinator.StatusChanged`. This keeps status presentation separate from Git execution and guarantees that clone, pull, and any submodule follow-up remain one indivisible UI operation.

`OperationCoordinator.CancelCurrentOperation()` signals the linked cancellation source for the active slot and publishes `Cancelling` immediately. The overlay Cancel button and window close button share one confirmation flow. A confirmed close hides the window, suppresses completion notifications, and waits for process shutdown and cleanup before closing. Deliberate cancellation is neutral and does not open an error dialog; a background cancellation can notify when the app remains open. Cleanup trouble is surfaced as an attention warning with the remaining path.

## Git execution

`ProcessRunner` is the only raw process boundary. It uses `ProcessStartInfo.ArgumentList`, redirects standard output/error, streams progress lines, caps captured diagnostics, and supports timeouts. Cancellation kills the entire process tree, waits for process exit, and drains both output readers before returning. Timeouts use the same safe shutdown mechanics but remain failure results. No operation invokes PowerShell, `cmd.exe`, or a shell-built command string.

`GitClient` owns clone and repository inspection:

1. `GitHubSshProbe` runs a non-interactive, time-limited GitHub SSH check.
2. `GitUrlResolver` normalizes GitHub HTTPS, SSH, and `owner/repository` inputs.
3. SSH is selected only when GitHub reports successful authentication; otherwise HTTPS is selected.
4. The destination path is normalized and checked before `git clone` starts.

Clone records whether its resolved target existed before Git started. After cancellation, it recursively removes only an absent-before-start target created by that clone. A pre-existing empty target and the selected destination root are never deleted.

## Adding repository operations

Fetch, pull, and push implement `IRepositoryOperation`. To add commit, add, branch, stash, or another repository action:

1. Add an `IRepositoryOperation` implementation in `src/GitTool.Core/Git`.
2. Execute Git through `IGitCommandExecutor` so output and failures use the common result model.
3. Register the implementation in `AppServices.RepositoryOperationRegistry`.
4. Add the corresponding icon-and-label control to `RepositoryPage`, calling the registry by its operation key.

Options shared by operations belong in `RepositoryOperationOptions`. This is why submodule behavior can already be used by both fetch and pull without coupling it to either page button.

## Settings and logs

`JsonSettingsStore` writes `settings.json` using a temporary-file replacement. `BufferedFileLogger` queues informational entries and flushes on a five-minute timer. Errors force an immediate flush, and window shutdown performs a final synchronous wait for the logger to finish.

Unpackaged runs keep writable app data under `%LOCALAPPDATA%\GitTool`.
Packaged runs resolve `ApplicationData.Current.LocalFolder` and keep the same
`GitTool\settings.json` and `GitTool\Logs` shape inside package-managed
LocalState. The first packaged launch moves legacy unpackaged app data into
LocalState when possible. Windows removes LocalState with the package.

## Packaging notes

The build script supports a self-contained standalone output and a manual,
framework-dependent unsigned MSIX. MSIX identity is selected by branch:
`master` uses GitTool, `development` uses GitTool Development, and
`experimental/*` uses GitTool Experimental. Package identity enables native
taskbar badge glyphs, while the installed Windows App Runtime Framework, Main,
and Singleton packages provide notification dependencies. Notification
registration remains capability-gated, and unsupported or policy-blocked states
never interrupt Git operations. Unsigned MSIX package identity and publisher
are derived from the local Windows username, and its `BuildInfo.json` records
local build metadata. Local unsigned builds keep an ignored revision counter
and compare it with any installed matching identity so repeated builds produce
an in-place MSIX upgrade. Windows does not permit executable notification
activations in unsigned MSIX packages, so those manifests omit the COM
activator. At runtime, `WindowsAppNotificationPlatform` detects that omission
and sends through the packaged `ToastNotificationManager` path instead. Banners
and background completion notifications remain available, but in-process click
and input activation require a future signed package with the COM extension. CI
builds only the standalone output.

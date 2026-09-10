# Architecture

## Summary

Use a single-process, Windows-only WPF application targeting .NET 10. WPF keeps
the binary and dependency model modest, starts quickly, supports accessibility,
and can call both COM and Win32 APIs directly. The application is
framework-dependent by default, remains resident in the signed-in user's
session, and has no background service.

The project compiles against `net10.0-windows10.0.22621.0` so the Wi-Fi and
Bluetooth radio adapter can consume the supported Windows Runtime projection
directly. `SupportedOSPlatformVersion` and the MSIX manifest set the runtime
minimum to Windows build `19041`, allowing Windows 10 version 2004 and newer.
Other platform features use direct Win32 and COM interop; no UI framework,
tray, or settings package is added.

## Edition and build selection

Free and Paid are build-time editions from the same source tree. An immutable
product configuration selects the edition and its feature catalog during app
composition; UI panels, hotkeys, tray commands, persistence, and package
metadata all consume that configuration. Paid is the compatibility default
while the split is introduced.

Profiles are the first paid-only module. The Free composition must not
instantiate the profile store, register profile hotkeys, expose profile tray
commands, or route a profile navigation request. The shared shell receives a
no-op profile boundary in Free and the full profile coordinator in Paid. This
keeps direct display/audio/device switching common while preventing a hidden
UI control from leaving a paid action active.

The build property is `WinQuickSwitchEdition=Free|Paid`. Store manifests,
identities, descriptions, and artifact directories are selected from the same
edition value. The existing Paid identity remains unchanged; Free receives a
separate Partner Center identity.

## Shared feature surfaces

Display, Audio, Devices, Options, Profiles, and About are reusable WPF feature
views created by one edition-aware `FeatureViewFactory`. The normal window,
overlay launcher, workspace cards, and supported floating windows each receive
a fresh visual instance because WPF controls cannot have more than one parent.
All instances use the same `IQuickSwitchCoordinator`, immutable
`QuickSwitchState` snapshots, typed commands, refresh gates, and result state.
Focus and scrolling remain local to each instance.

Overlay cards are not reconstructed when inventory, theme, or status changes;
bindings update the existing view. Workspace schema version 6 records exact
per-monitor geometry and a content-layout version. A new or legacy non-custom
card is measured once after its initial Ready, Empty, or Error state, capped to
90% of canvas width and 88% of height, then saved. Manual dimensions remain
authoritative until the user invokes Fit content.

## Layers

```text
WPF views and view models
          |
Application services and normalized models
          |
Windows adapters
  |          |             |
Display   Core Audio   Device enumeration
```

Views depend on application interfaces, not Windows API types. Each adapter
owns its COM objects, native handles, notifications, and thread-affinity rules.
This keeps compatibility-sensitive code small and testable.

## Suggested source layout

```text
src/WinQuickSwitch/
  Features/
    Display/
    Audio/
    Devices/
    Taskbar/
  Platform/Windows/
    Display/
    Audio/
    Devices/
  Shared/
```

Move to multiple projects only if the code or tests need the separation. A
single project is cheaper to navigate for the initial app.

## Window shell

Keep the normal Windows-managed WPF frame rather than replacing it with a
custom title-bar implementation. After WPF creates the native handle, the shell
calls `DwmSetWindowAttribute` with the dark-frame, border, caption, and text
attributes. Supported Windows 11 builds receive the app palette directly. If
an attribute is unavailable, its HRESULT is intentionally nonfatal and Windows
retains its own frame fallback.

This preserves native move, system-menu, and caption-button behavior without
another UI package or custom hit-testing layer. The widget itself is
resizable through the Windows-managed frame and always-on-top while visible.

The widget opens at 900 x 720 device-independent pixels, has a 600 x 420
minimum, and is capped by the working area's width and height. Display, Audio,
Devices, Options, and About are mutually exclusive panels in Free; Paid adds
Profiles to the same compact tab strip. The complete shell scrolls vertically when content exceeds
the resized viewport. Outer/card spacing is compact, the title bar is the only
app-name heading, and endpoint/session/device lists own their vertical
scrolling. Horizontal
scrolling is disabled in the compact tables; friendly audio names trim visually
while the full name remains in the bound model and automation text.

The standard Windows caption exposes minimize and maximize/restore. The window
has a taskbar entry while visible or minimized, and minimizing suppresses the
widget's normal click-away auto-hide path. Users can resize from any native
window edge; reduced heights fall back to the outer vertical scroller.

## Resident widget lifecycle

The first process owns a named per-user mutex and an auto-reset activation
event. A later launch signals that event and exits; the first process reveals
its existing window. This keeps all native callbacks and mutable Windows state
inside one process without an IPC framework.

`RegisterHotKey` owns the default `Win + Shift + Q` chord with no-repeat
semantics. Optional bindings can toggle the window, reveal Display/Audio/
Devices, request one of the four allowlisted projection modes, or select one
of four favorite playback outputs. `WM_HOTKEY` resolves the native identifier
back to one of those fixed actions. Each binding registers independently, so
one conflict does not disable the others; the Options panel reports the
conflict and does not save a newly rejected binding.

All four display-mode shortcuts can run while the widget remains hidden and
apply immediately without a confirmation prompt. Favorite-output shortcuts
call the same general-default
audio adapter as the visible **Default** button. A failed favorite selection
reveals Audio with the error; success remains unobtrusive.

The first reveal reads the pointer position, selects the nearest monitor, and
uses that monitor's work area. A pure placement calculator prefers the
lower-right side of the pointer, flips at the right or bottom edge, and clamps
the final rectangle. WPF device-independent units are converted to and from
physical pixels around the Win32 monitor calculation, so negative-coordinate
and mixed-DPI monitor layouts remain valid. The resulting position is retained
for later hide/show cycles and panel changes instead of repeatedly anchoring to
the moving pointer. A size-change clamp preserves that top-left position when
the new panel fits and moves it only by the overflow amount when a taller panel
or expanded shortcut section would cross the current monitor's work area.

The close button, `Esc`, and click-away hide the window. A short activation
grace prevents the launch or second-instance handoff from being immediately
undone if Windows is still settling foreground focus. A native
`Shell_NotifyIcon` keeps one notification-area icon for the resident process;
left-click or **Open WinQuickSwitch** reveals the widget, while **Exit
WinQuickSwitch** is the explicit process-exit path. The icon is registered on
the existing WPF window handle and removed during `OnClosed`, so no tray
process, polling loop, or third-party tray package is required.
The icon opts into `NOTIFYICON_VERSION_4`; callback routing therefore reads the
notification code from the low word of `lParam` and uses the packed anchor
coordinates when Windows does not allow a pointer query. Both `WM_RBUTTONUP`
and `WM_CONTEXTMENU` open the native Open/Favorites/Exit menu.

Optional sign-in startup writes one quoted command to
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. The command contains the
current executable path and the allowlisted `--startup` argument. That argument
creates the native window handle and registers global hotkeys without showing
the widget. A normal later launch still reveals the existing resident instance;
a delayed Windows startup launch does not unexpectedly reveal an instance that
is already running. Free and Paid intentionally share this one startup slot so
signing in cannot launch two competing resident editions; an edition only
removes the entry when it still points to its own executable.

### Taskbar

`WindowsTaskbarService` reads and changes the Windows taskbar's auto-hide state
through `SHAppBarMessage(ABM_GETSTATE/ABM_SETSTATE)`. **Show taskbar** sends a
zero state; **Hide taskbar** sends `ABS_AUTOHIDE`, which means Windows reveals
the taskbar when the pointer reaches its edge rather than destroying the shell
window. The adapter catches shell failures and leaves the widget usable. The
Options links use the documented `ms-settings:taskbar`, `ms-settings:display`,
and `ms-settings:notifications` URIs for settings that should remain owned by
Windows.

## Local options

The Options panel captures only modified A-Z, 0-9, and F1-F12 shortcuts. Ctrl,
Alt, or Win is required; Shift can be added. Duplicate in-app bindings are
rejected before native registration. Each shortcut row has an explicit Unset
action; Delete remains a keyboard shortcut for the same operation. Settings use
`System.Text.Json` and an atomic temporary-file replacement. Paid keeps the
existing `%LOCALAPPDATA%\WinQuickSwitch\settings.json` path, while Free uses
`%LOCALAPPDATA%\WinQuickSwitch Free\settings.json`; no third-party
configuration package is needed.

Native hotkey registration is transactional after the first working set is
established. Reapplying settings unregisters every active built-in and dynamic
profile ID; if any replacement fails, the new partial set is removed and the
previous working set is restored. Disposal uses the same complete registration
ledger, preventing removed Paid profile hotkeys from remaining registered until
process exit. Profile catalogs keep the first owner of a duplicate shortcut and
clear later duplicates during normalization. Cross-category conflicts report
the current command owner and the affected profile before registration. Native
profile IDs are deterministically derived from stable profile IDs, so reordering
the profile list cannot retarget a hotkey. `Ctrl+0` through `Ctrl+6` remain
reserved for edition-aware navigation inside the overlay.

`QuickSwitchCommandCatalog` is the compatibility seam between persisted
`WidgetHotkeyAction` values and the shared typed-command pipeline. Each
descriptor owns a stable key, user-facing label, category, availability rule,
and command factory. WM_HOTKEY dispatch resolves a descriptor and executes it
through the existing coordinator instead of calling display/audio/window
methods directly. The shared Options rows consume the same labels and actions;
Paid profile Apply controls use dynamic descriptors keyed by the stable profile
ID, while Free rejects those descriptors through `ProductConfiguration`.

Favorite playback outputs are fixed to four optional slots to keep persistence,
hotkey IDs, and the Options UI bounded. Each slot stores the Core Audio
endpoint ID, its last friendly name, and an optional shortcut. Endpoint IDs,
not list positions or names, drive selection after restart. Invalid and
duplicate entries are removed during settings normalization, and older
settings files load with all new shortcuts empty.

Windows is the source of truth for the separate start-with-Windows option. The
Options checkbox compares the current executable's exact quoted startup
command with the per-user `Run` value. Enabling overwrites a stale path;
disabling removes the value. Registry access failures are nonfatal and are
reported in the Options panel. Machine-wide startup, a scheduled task, and a
background service are intentionally avoided.

Dark and light palettes update WPF brushes through dynamic color resources.
The same palette is sent to the native caption through `DwmSetWindowAttribute`,
followed by a frame refresh so the title bar repaints immediately. Unsupported
DWM attributes remain nonfatal and use the Windows fallback.

## Display adapter

Version 1 supports the four global projection topologies exposed by `Win + P`:
internal, clone, extend, and external. Internal, clone, and external are applied
directly through `SetDisplayConfig` with `SDC_APPLY` plus one allowlisted
`SDC_TOPOLOGY_*` flag. Extend uses the hidden Windows `DisplaySwitch.exe
/extend` projection command, leaving placement, rotation, resolution, and
orientation restoration to Windows rather than supplying or saving a custom
path configuration.

Windows can restore an incomplete Extend topology when entering it directly
from Duplicate or second screen only. Those two transitions first request PC
screen only, wait briefly for that topology, and then request Windows Extend
only when no safe layout snapshot exists.

Before leaving a complete Extend topology, the display service captures the
active CCD paths and their full mode array in memory. This includes source
positions, target rotations, resolutions, refresh data, and path priority.
Returning to Extend reapplies that exact snapshot with
`SDC_USE_SUPPLIED_DISPLAY_CONFIG` but without `SDC_SAVE_TO_DATABASE` or best-mode
changes. The direct restore avoids the bridge and projection command while
preserving the layout byte-for-byte. If capture is unavailable or exact restore
fails, the PC-screen-only bridge remains the compatibility fallback.

If reliable current-topology detection is needed, query the active display
configuration through `QueryDisplayConfig`; keep that native mapping separate
from the action request.

The current implementation reads all display paths through
`QueryDisplayConfig(QDC_ALL_PATHS)` to count active and available targets. It
separately reads Windows' current CCD database topology through
`QueryDisplayConfig(QDC_DATABASE_CURRENT)` and uses the returned topology ID as
the authoritative PC screen only, Duplicate, Extend, or Second screen only
selection. This keeps the app aligned with Win+P even when active paths alone
look extended. If Windows does not return a standard topology ID, the app falls
back to path-based classification; a hybrid topology is then reported as
custom/mixed instead of being forced into one of the four modes.

`SetDisplayConfig` or `DisplaySwitch.exe` returning success means that Windows
accepted the request; it is not treated as proof that the requested topology
remained active. The transition executor polls the authoritative topology,
requires a stable final state, and performs one bounded reapply if another
component immediately restores a different topology. An unresolved override
is a failed command, so overlay smart-close does not dismiss the Display UI.
The status identifies the actual topology and recommends leaving exclusive
full-screen mode. The recovery path reuses the same captured Extend layout and
does not create or save a replacement monitor arrangement.

The display choices are persistent toggle controls rather than stateless
buttons. The current topology is highlighted at startup; selecting that same
mode again restores the checked state without another Windows request only when
the topology is complete. Extend is incomplete when an available target is not
active, so it remains retryable instead of being treated as already selected.
For a real change, the requested mode highlights immediately and the group
accepts no second request until the first completes.

After Windows accepts a projection request, a transient topology monitor checks
`QueryDisplayConfig` every 150 ms for at most 18 attempts (2.55 seconds). It
requires three identical snapshots, and Extend does not settle until every
available display target is active. This prevents both a stale immediate read
and the partial `Extend, 2 active, 3 available` state from being accepted as a
completed transition. It exists only during a user-requested transition and is
not an idle polling loop. Physical display blanking and link retraining remain
under Windows, GPU-driver, and monitor control.

Individual-monitor enable/disable and layout editing are intentionally
excluded. They require substantially more display-configuration handling and
are not equivalent to the four global projection modes.

## Audio adapter

Use Windows Core Audio COM interfaces:

- `IMMDeviceEnumerator` and `IMMNotificationClient` for playback and recording
  endpoints and change notifications.
- `IAudioSessionManager2`, `IAudioSessionEnumerator`, and session notifications
  for active application sessions.
- `ISimpleAudioVolume` for per-session volume and mute.

Keep master endpoint volume separate from application-session volume.
Applications may expose multiple sessions, protected/system sessions may not
have a useful process identity, and a process can exit during enumeration.
Models must tolerate all three cases.

Changing the Windows default playback or recording endpoint is the main
compatibility risk. Common desktop implementations use the undocumented
PolicyConfig COM contract. Place it behind `IDefaultAudioEndpointSetter`, add
Windows-build integration tests, and provide a Settings-page fallback. Never
scatter PolicyConfig declarations through feature code.

Version 1 changes endpoints for explicit roles only. Endpoint rows show a
green **DEFAULT** badge for console/multimedia and a blue **CALLS** badge for
communications; a row can show both. The visible row text remains the Windows
friendly device name, and the same role state is exposed as automation help
text and a tooltip.

M3 exposes session volume through `ISimpleAudioVolume`, always using the
session-instance identifier rather than process name so multiple sessions and
short-lived processes remain distinct. Sliders commit only after a mouse or
keyboard action; binding and inventory refresh never write audio state.

Default endpoint changes stay behind `IDefaultAudioEndpointService` and the
small `IDefaultAudioEndpointSetter` compatibility boundary. **Default**
updates console and multimedia roles together. **Calls** updates only the
communications role. The policy interface is not a documented Windows API, so
any activation, cast, or call failure returns a normal result and opens the
documented `ms-settings:sound` page through a separate launcher. No other
feature code references the policy COM declaration.

The Audio header exposes two allowlisted Settings actions. **Sound settings**
uses `ms-settings:sound` for the general Windows **System > Sound** page, while
**Volume mixer** uses `ms-settings:apps-volume` for Windows' full per-app mixer.
Sound devices, default-endpoint properties, and device-specific pages remain
outside this milestone.

The current read-only implementation enumerates active render/capture
endpoints, separately tracks console, multimedia, and communications defaults,
and reads active sessions plus their current volume/mute state. A dedicated
background MTA thread owns the Core Audio notification registrations. Device
callbacks request a session-subscription rebuild; session-created, state,
disconnect, and volume callbacks request an inventory refresh. Callbacks remain
nonblocking, and a 350 ms debounce collapses Windows notification bursts into a
single UI refresh. The watcher unregisters every callback before releasing its
COM objects. The manual Refresh button remains available if registration is
unsupported or Windows Audio is unavailable.

## Device adapter

The M4 adapter uses SetupAPI to enumerate only Plug and Play devnodes carrying
the `DIGCF_PRESENT` flag. It reads only properties needed for the UI:

- Stable device/interface identifier retained in memory.
- Friendly name.
- Device class.
- Present/connected and enabled/problem state where exposed.
- Transport/bus information used to classify Bluetooth versus wired.
- Container ID, retained only in memory for grouping.

Interfaces with the same Windows container ID are grouped into one physical
device. Known hub, enumerator, protocol-service, and generic interface names
are hidden. Bluetooth takes precedence for a container that exposes both
Bluetooth and USB interfaces. Common Bluetooth profile prefixes/suffixes
(`LE_`, `Stereo`, and hands-free audio) are removed, then exact normalized
names are collapsed to avoid presenting separate Windows profiles as separate
devices. The tradeoff is that two simultaneously present Bluetooth devices
with exactly the same friendly name can appear as one row.

Status comes from `CM_Get_DevNode_Status`: started devnodes are shown as
connected, other present devnodes as present, and nonzero problem codes as
needing attention. Battery state is not part of M4 because SetupAPI does not
provide one uniform battery property across Bluetooth device types.

The WPF window listens for `WM_DEVICECHANGE` on its existing native window
handle. A 450 ms debounce collapses hardware-event bursts before a background
SetupAPI refresh while Devices is visible. Hidden changes only mark the
inventory dirty for the next Devices activation. The hook and pending refresh
are removed on process exit; there is no polling thread or resident service.

Wireless power state uses the supported `Windows.Devices.Radios` boundary.
Reading Wi-Fi and Bluetooth state does not request permission. A user-initiated
toggle calls `Radio.RequestAccessAsync` and proceeds to `SetStateAsync` only
when Windows returns `Allowed`; the application never requests radio access at
startup. State is refreshed when Devices opens, after a requested change, and
on manual refresh, without adding a polling thread.

If radio control is denied by the user, Windows, hardware, or policy, the UI
reports that result and opens the matching allowlisted Settings page. Pair,
remove, troubleshoot, and driver actions remain outside the app. Device links
are fixed to `ms-settings:network-wifi`, `ms-settings:bluetooth`,
`ms-settings:network-airplanemode`, `ms-settings:network-status`, and
`ms-settings:connecteddevices`.

## Threading and lifetime

- Initialize COM on the required apartment.
- Marshal platform callbacks into an application event stream, then update WPF
  observable state on the dispatcher.
- Dispose COM references and unregister callbacks deterministically.
- Debounce bursts of device-change notifications before refreshing.
- Cancel panel refresh work whenever the widget hides.
- Start the Core Audio notification thread only while Audio is visible and
  stop it when Audio is left or the widget hides.

## Security and privacy

- No elevation manifest.
- No PnP device disable, driver installation, or arbitrary command execution;
  wireless state changes use only the permission-gated Windows radio API.
- Display arguments and Settings URIs are fixed allowlists.
- No network client or telemetry package.
- Local diagnostic logs exclude device serial numbers and full PnP identifiers.

## Performance budgets

Targets for a release, framework-dependent build:

- Interactive window within 500 ms on a warm start and 1.5 s on a cold start.
- Working set below 100 MB after the first device refresh.
- No polling loop while idle; use Windows notifications.
- UI remains responsive while enumerating devices.

Display topology loads for the initial panel. Audio and device inventory load
only when their panel is selected. The audio watcher exists only while Audio is
visible; device notifications received while hidden defer enumeration until
Devices is selected again.

Measure these targets in M5 rather than treating them as guaranteed by the
framework choice.

The Windows Sandbox E2E harness has an opt-in performance mode. It measures
fresh normal-window and overlay startup, repeated feature navigation, inspector
transitions, refresh completion, and every safe deterministic feature action.
An edition-aware manifest is a parity gate: every interactive feature needs
timed stages or an explicit exclusion, and a missing Paid/Free stage fails the
run. A background guest sampler records private memory, working set, handles,
threads, and CPU time every 100 milliseconds across the same action workload.
Warm-up samples and diagnostic screenshots remain in the evidence package but
are excluded from reported stage percentiles. Paid and Free runs use separate
fresh processes and can be combined into one HTML/CSV comparison. Provisional
latency and resource budgets are informational unless the caller explicitly
enables enforcement, avoiding release failures caused by a temporarily busy
host before a stable baseline is established. All Windows mutations use the
simulated E2E services, so validation cannot change host hardware or settings.

## Deployment variants

- **Lite** is a framework-dependent, single-file Windows x64 executable. It
  relies on the shared .NET 10 Desktop Runtime and minimizes app payload.
- **Portable** is a compressed, self-contained, single-file Windows x64
  executable. It trades download size for zero runtime setup.
- Lite and Portable are public Free artifacts. Paid standalone artifacts are
  internal validation outputs until an enforceable non-Store license path is
  designed.
- The Lite and Portable publish profiles default to `WinQuickSwitchEdition=Free`
  and write `win-x64-free-lite` / `win-x64-free-portable` outputs. Paid
  standalone validation must opt in explicitly and uses `win-x64-paid-*` paths.
- Store packages are built independently for Free and Paid with separate
  manifests, identities, descriptions, and artifact directories.
- Both profiles disable trimming and ReadyToRun. WPF and the current COM
  declarations are not being treated as trim-safe, while ReadyToRun generally
  increases publish size.

## Feature-complete widget workspace overlay

The resident process can opt into `OverlayWorkspaceWindow`, a borderless,
topmost workspace covering only the monitor nearest the pointer. A dimmed
backdrop and fixed right launcher provide Display, Audio, Devices, Options,
Paid Profiles, and About. Audio owns favorite endpoint management; there is no
duplicate Favorites launcher or card. Home remains an internal quick-workspace
route for `Ctrl+0`, but is omitted from the launcher because it duplicated the
workspace itself. The first layout composes the
essential cards (Display, Audio, and Devices) in both editions.
Profiles, About, and Options remain launcher-first features until a user adds
them as quick widgets. The launcher opens complete feature panels without requiring the
main window; removed quick cards are restored explicitly from the inspector.

Cards move continuously with the pointer and retain exact fractional bounds.
The host clamps them to the usable monitor area, preserves z-order, restores
custom bounds per monitor, and exposes accessible icon controls for refresh,
feature-specific size reset, minimize/restore where supported, and removal.
Bottom-right resizing preserves the opposite edges and stops growing at the
workspace boundary. Recommended presets are large enough to show the
feature's common content; an exact legacy preset migrates while a user-resized
card is preserved. It opens a 520–900 DIP inspector for the same complete
feature panel used by the conventional window. `Ctrl+0` returns to the quick
workspace, `Ctrl+1` through
`Ctrl+6` use the existing edition-aware numbering, and Escape closes an
inspector before dismissing the workspace. There is intentionally no overlay
Pin control: cards are session-scoped and disappear when the workspace closes;
the existing standalone floating Display, Audio, and Devices widgets remain a
separate option.

`OverlayWorkspaceSettings` stores a versioned layout per
`MONITORINFOEX.szDevice`, including card visibility, bounds, collapsed state,
z-order, and inspector width. Missing monitors use the current monitor's
default arrangement and clamp safely. The legacy sidebar-width field remains
readable for JSON migration, while new saves use `InspectorWidth`.
`MONITORINFOEXW` is marshalled with an explicit Unicode layout so its native
size and device name are valid. If pointer lookup is unavailable, placement
falls back to the owner window's monitor and finally one monitor at virtual
origin; it never substitutes the full virtual-screen rectangle.

Layout schema version 6 records the shared-content measurement version while
retaining `HasCustomSize` for JSON compatibility. Generated first-run cards fit
once after the initial Ready, Empty, or Error state and are then arranged into
non-overlapping monitor-safe cells. Restored and manually resized geometry is
authoritative across refresh, theme changes, and reopen. Drag handles carry the
stable widget ID directly instead of depending on a fragile visual-parent
hierarchy.

`MainWindow` remains the application-lifetime coordinator and owner of services,
refresh gates, settings, profiles, and mutations. Every surface creates its own
feature-view instance through `FeatureViewFactory`; WPF controls are never
reparented. These instances consume the same cached immutable
`QuickSwitchState`, typed commands, revision stream, and reference-counted
surface demand. Reading coordinator state never queries Windows or inspects a
hidden legacy control. Platform refreshes copy inventories into coordinator
caches before publishing a new revision, so an older snapshot cannot mutate
under an open view. The workspace never creates a second display, audio,
device, or profile pipeline. Audio/display/device demand is shared between the
normal window and workspace, so hot-plug and notification refreshes cannot race
two independent watchers.
`QuickSwitchCommandCatalog` is also the single identity boundary for persisted
global shortcuts, dynamic Paid profile Apply commands, native tray actions,
and workspace launcher/footer actions. The native tray decodes numeric menu
IDs only long enough to resolve a catalog command and defers execution until
the popup menu has closed. The workspace emits typed navigation commands and
rejects unavailable edition routes before loading an inspector. Card move,
resize, fit, collapse, and removal remain local typed presentation actions and
never call platform services or the shared coordinator.
The Devices card renders the shared connected-device inventory, and concurrent
refresh requests wait for the active refresh before updating the card.
Native Windows Settings links close the topmost workspace before launching.
This remains a best-effort desktop overlay: ordinary windows and
borderless-windowed games are supported, while exclusive-fullscreen
applications may suppress topmost WPF windows.

The retired `SidebarOverlayWindow` implementation has been removed. The
`MainWindow.ToggleSidebarOverlay` compatibility entry point now creates only
the workspace shell. The legacy sidebar-width setting remains deserializable
solely so older JSON can migrate its value into the current inspector width.

The resident window's Display panel is the compact home surface. It keeps the
four display actions in the first view, adds an at-a-glance summary sourced from
the existing display, audio, and device refresh paths, and links to the overlay
or detailed Audio and Devices panels. It does not create a second platform
service path, so display placement and rotation safeguards remain centralized.

The first detachable surface is `FloatingDisplayWidgetWindow`. It receives
display actions and layout notifications from `MainWindow`, but never creates a
second display service or topology watcher. Its size, position, pin state, and
open state use the existing atomic widget-settings store. On restore, the host
clamps the window to the nearest current monitor work area; a missing monitor
identity therefore cannot leave an unreachable window or trigger a display
topology mutation. Audio and Devices floating surfaces can use the same host
contract in a later increment.

## Test strategy

- Unit tests for view models, grouping, allowlists, and Windows-result mapping.
- Contract tests for adapters with fake platform backends.
- Windows integration tests for endpoint/session enumeration and topology
  detection.
- Manual hardware matrix for projection changes, Bluetooth profiles, USB
  headsets, hot-plugging, high DPI, and accessibility.
- Every release test is run for both editions. Free tests assert that profile
  storage, profile hotkeys, profile navigation, and profile tray actions are
  unavailable; Paid tests retain the complete profile suite.
- Windows Sandbox runs the real WPF surfaces in a disposable desktop. A
  compile-time E2E composition replaces Windows adapters with deterministic
  display, audio, device, radio, taskbar, startup, Settings, and profile
  services. UI Automation can therefore exercise mutations safely and verify
  them through a JSONL service journal while exporting dark/light screenshots.
- The same harness has a separate performance path that repeats safe navigation
  and refresh scenarios, records median/P95/max latency, and samples memory,
  handles, threads, and CPU. Screenshots are diagnostics only and never occur
  inside a timed stage.
- Production builds exclude `src/WinQuickSwitch/E2E`; the simulated runtime is
  compiled only when `WinQuickSwitchE2E=true`.

Display and default-endpoint mutations should never run in ordinary unit-test
suites. Mark them as attended hardware tests.

## Known risks

### v1.1.1.0 stable-key shortcut extension

Existing `WidgetHotkeyAction` fields and native IDs are retained. New audio
commands use catalog string keys in `WidgetSettings.CommandShortcuts` and a
separate native ID range (0x4000-0x4FFF); the registrar resolves the key back to
a typed command. Duplicate owner checks and transactional rollback cover
legacy, profile and keyed registrations together. Unknown keys are discarded
during settings normalization; no Profiles keys are admitted through this map.

The shared Options view has one local search/expansion state per surface.
Filtering collapses existing row containers instead of replacing ItemsSource,
preserving WPF automation children. Shortcut values and the volume-step setting
are published through coordinator state. No search operation executes commands.

`AudioQuickActionExecutor` resolves fresh default endpoints through existing
services, clamps volume and serializes mutations with a non-queueing gate and
150 ms repeat guard. It does not alter default roles or display topology.
Production construction receives Windows services; Sandbox receives only its
simulated endpoint service. Saved chords start unassigned for all new actions.

### Workspace construction and release evidence

Workspace widget descriptors use `DeferredFeatureContent`: a hidden card does
not create its shared view until first measurement. Fitting measures the actual
child, while later measurements and visibility changes keep that same instance.
Single-panel navigation constructs only its selected descriptor rather than
all workspace feature views. No display-topology service changes are involved.

`tools/Test-ReleaseArtifacts.ps1` checks the final executable versions and
Paid/Free Store manifests, verifies upload/package hashes, and rejects the
simulated E2E service marker in production packages without launching them.
Automated evidence and attended hardware/Store acceptance are tracked
separately in `docs/RELEASE_ACCEPTANCE_1_1_0.md`.

### Platform risks

| Risk | Response |
| --- | --- |
| Default-audio selection relies on a compatibility-sensitive COM contract | Isolate it, integration-test supported Windows builds, and retain a Settings fallback |
| Bluetooth headsets expose several profiles and endpoints | Group by container first, normalize only known profile labels, and document the identical-name tradeoff |
| Device enumeration returns historical or duplicate records | Filter for present devices and preserve raw-to-normalized traceability |
| Projection changes can make the active display disappear | Use only the four Windows modes and make risky actions explicit |
| WPF styling can drift from Windows 11 | Keep the UI compact and accessible; avoid recreating the full Settings design |
| Framework-dependent deployment needs a runtime | Detect the runtime in packaging or provide a larger self-contained artifact |
| A global hotkey conflicts with another application | Keep other bindings active, reject the changed binding, and show the conflict in Options |

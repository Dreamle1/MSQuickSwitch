# Architecture

## Summary

Use a single-process, Windows-only WPF application targeting .NET 10. WPF keeps
the binary and dependency model modest, starts quickly, supports accessibility,
and can call both COM and Win32 APIs directly. The application is
framework-dependent by default and has no background service.

The project targets the generic `net10.0-windows` TFM. The implemented platform
features use direct Win32 and COM interop, so compiling against a versioned
Windows SDK would copy large WinRT support assemblies that the app does not
use.

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

This preserves native move, resize, maximize, snap, system-menu, and caption
button behavior without another UI package or custom hit-testing layer.

The default window is 660 pixels wide and sizes vertically to its content,
capped by the working area. Outer/card spacing is compact, the title bar is the
only app-name heading, and endpoint/session/device lists own their vertical
scrolling. Horizontal scrolling is disabled in the compact tables; friendly
audio names trim visually while the full name remains in the bound model and
automation text.

## Display adapter

Version 1 supports the four global projection topologies exposed by `Win + P`:
internal, clone, extend, and external. Start by calling the Windows
`DisplaySwitch.exe` operation with an argument allowlist. Do not accept raw
command text from the UI.

If reliable current-topology detection is needed, query the active display
configuration through `QueryDisplayConfig`; keep that native mapping separate
from the action request.

The current implementation reads all display paths through
`QueryDisplayConfig`, counts active and available targets, and classifies the
four global projection modes. A hybrid topology that combines duplicate and
extended paths is reported as custom/mixed instead of being forced into one of
the four modes.

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
SetupAPI refresh. The hook and pending refresh are removed on window close;
there is no polling thread or resident service.

The first release is read-only. Pair, remove, troubleshoot, and driver actions
open the allowlisted `ms-settings:bluetooth` or
`ms-settings:connecteddevices` page.

## Threading and lifetime

- Initialize COM on the required apartment.
- Marshal platform callbacks into an application event stream, then update WPF
  observable state on the dispatcher.
- Dispose COM references and unregister callbacks deterministically.
- Debounce bursts of device-change notifications before refreshing.
- Cancel in-flight refresh work when the window closes.

## Security and privacy

- No elevation manifest.
- No device enable/disable, driver installation, or arbitrary command
  execution.
- Display arguments and Settings URIs are fixed allowlists.
- No network client or telemetry package.
- Local diagnostic logs exclude device serial numbers and full PnP identifiers.

## Performance budgets

Targets for a release, framework-dependent build:

- Interactive window within 500 ms on a warm start and 1.5 s on a cold start.
- Working set below 100 MB after the first device refresh.
- No polling loop while idle; use Windows notifications.
- UI remains responsive while enumerating devices.

Audio and device inventory load when the main window starts. Their watchers
exist only for the lifetime of that window and do no work while Windows emits
no relevant notifications.

Measure these targets in M5 rather than treating them as guaranteed by the
framework choice.

## Deployment variants

- **Lite** is a framework-dependent, single-file Windows x64 executable. It
  relies on the shared .NET 10 Desktop Runtime and minimizes app payload.
- **Portable** is a compressed, self-contained, single-file Windows x64
  executable. It trades download size for zero runtime setup.
- Both profiles disable trimming and ReadyToRun. WPF and the current COM
  declarations are not being treated as trim-safe, while ReadyToRun generally
  increases publish size.

## Test strategy

- Unit tests for view models, grouping, allowlists, and Windows-result mapping.
- Contract tests for adapters with fake platform backends.
- Windows integration tests for endpoint/session enumeration and topology
  detection.
- Manual hardware matrix for projection changes, Bluetooth profiles, USB
  headsets, hot-plugging, high DPI, and accessibility.

Display and default-endpoint mutations should never run in ordinary unit-test
suites. Mark them as attended hardware tests.

## Known risks

| Risk | Response |
| --- | --- |
| Default-audio selection relies on a compatibility-sensitive COM contract | Isolate it, integration-test supported Windows builds, and retain a Settings fallback |
| Bluetooth headsets expose several profiles and endpoints | Group by container first, normalize only known profile labels, and document the identical-name tradeoff |
| Device enumeration returns historical or duplicate records | Filter for present devices and preserve raw-to-normalized traceability |
| Projection changes can make the active display disappear | Use only the four Windows modes and make risky actions explicit |
| WPF styling can drift from Windows 11 | Keep the UI compact and accessible; avoid recreating the full Settings design |
| Framework-dependent deployment needs a runtime | Detect the runtime in packaging or provide a larger self-contained artifact |

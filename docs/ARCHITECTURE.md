# Architecture

## Summary

Use a single-process, Windows-only WPF application targeting .NET 10. WPF keeps
the binary and dependency model modest, starts quickly, supports accessibility,
and can call both COM and Win32 APIs directly. The application is
framework-dependent by default and has no background service.

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

## Display adapter

Version 1 supports the four global projection topologies exposed by `Win + P`:
internal, clone, extend, and external. Start by calling the Windows
`DisplaySwitch.exe` operation with an argument allowlist. Do not accept raw
command text from the UI.

If reliable current-topology detection is needed, query the active display
configuration through `QueryDisplayConfig`; keep that native mapping separate
from the action request.

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

Version 1 changes endpoints for explicit roles only. The UI should make clear
whether it is changing console/multimedia, communications, or both.

## Device adapter

Use Windows device enumeration/Plug and Play information to build a present
device inventory and a watcher for changes. Query only properties needed for
the UI:

- Stable device/interface identifier retained in memory.
- Friendly name.
- Device class.
- Present/connected and enabled/problem state where exposed.
- Transport/bus information used to classify Bluetooth versus wired.
- Battery percentage when a battery property is actually available.

One physical headset can expose multiple endpoints and device interfaces.
Normalize cautiously: preserve the raw items internally and group only when a
container or parent identifier proves that they belong together.

The first release is read-only. Pair, remove, troubleshoot, and driver actions
open the relevant Windows Settings page.

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

Measure these targets in M5 rather than treating them as guaranteed by the
framework choice.

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
| Bluetooth headsets expose several profiles and endpoints | Show roles/transports and group only from reliable parent/container data |
| Device enumeration returns historical or duplicate records | Filter for present devices and preserve raw-to-normalized traceability |
| Projection changes can make the active display disappear | Use only the four Windows modes and make risky actions explicit |
| WPF styling can drift from Windows 11 | Keep the UI compact and accessible; avoid recreating the full Settings design |
| Framework-dependent deployment needs a runtime | Detect the runtime in packaging or provide a larger self-contained artifact |

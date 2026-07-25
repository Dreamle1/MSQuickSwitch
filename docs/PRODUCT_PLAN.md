# Product plan

## Product statement

WinQuickSwitch gives a Windows 11 user one compact place to switch display and
audio configurations and inspect connected devices. It should feel like a
fast control panel, not another full Settings application.

## Assumption

The phrase "window mode aka Ctrl + P" is interpreted as the Windows projection
menu opened by `Win + P`. The four projection modes are the first display
feature. Per-monitor positioning, resolution, refresh rate, HDR, and virtual
desktops are outside the first release.

## Goals

1. Reach the most-used controls in one or two clicks.
2. Start quickly and use little memory while open.
3. Work without administrator rights for normal operations.
4. Make hardware state clear before a user changes it.
5. Prefer documented Windows APIs and clearly isolate compatibility-sensitive
   code.

## Non-goals for version 1

- Replacing the complete Windows Settings application.
- Pairing, forgetting, enabling, disabling, or uninstalling devices.
- Installing drivers or repairing hardware.
- Audio routing rules that automatically follow applications.
- Per-monitor layout, resolution, refresh-rate, brightness, HDR, or color
  management.
- Remote control, cloud sync, accounts, telemetry, or update services.
- A permanent Windows service.

## Version 1 user experience

The main window has three compact sections:

### Display

- Show the current topology when it can be determined reliably.
- Offer PC screen only, duplicate, extend, and second screen only.
- Ask for confirmation only for actions likely to make the active display
  disappear.
- Show success or a useful error after Windows completes the request.

### Audio

- Show the current default playback and recording endpoints.
- List active playback sessions by application.
- Change per-application volume and mute state.
- Choose a playback or recording endpoint.
- Refresh automatically when sessions or endpoints appear or disappear.

### Devices

- Group connected devices as Bluetooth, USB/wired, and unknown/other.
- Show friendly name, class, connection/presence state, and battery percentage
  when Windows supplies it.
- Update the list when a device connects or disconnects.
- Link to the appropriate Windows Settings page for pairing or advanced device
  management.

## Milestones

### M0 - planning and shell

- Repository, scope, architecture, WPF shell, and build instructions.
- Decisions and risks are explicit.

Exit: another developer can clone the repository and identify the next slice
without needing an oral handoff.

### M1 - projection modes

- [x] Add an `IDisplayModeService`.
- [x] Invoke the built-in Windows display switch operation without a shell window.
- [x] Detect active/available paths and the current global topology.
- [x] Disable duplicate/extend choices that Windows reports as unavailable.
- [x] Add focused automated tests around command selection and result handling.
- Manually test with one monitor and two monitors.

Exit: all four `Win + P` modes can be requested and failures remain recoverable.

### M2 - read-only audio inventory

- [x] Enumerate active render and capture endpoints.
- [x] Identify the current default endpoint for console, multimedia, and
  communications roles.
- [x] Enumerate active application audio sessions.
- [x] Subscribe to device and session change notifications.

Exit: the UI stays accurate while applications and headsets come and go.

### M3 - audio controls

- [x] Add per-session volume and mute controls.
- [x] Add endpoint selection through the isolated policy adapter.
- [x] Treat a communications-default change separately from the general default.
- [x] Provide a safe fallback that opens the relevant Settings page if endpoint
  selection is unsupported on a Windows build.

Exit: session controls work for ordinary desktop apps and endpoint switching
either succeeds or degrades clearly.

### M4 - connected devices

- Enumerate present Bluetooth and wired Plug and Play devices.
- Normalize duplicate physical devices that expose several interfaces.
- Watch for add, remove, and update events.
- Add Settings deep links; keep pairing and device removal outside the app.

Exit: the list updates live and does not label historical/non-present devices
as connected.

### M5 - polish and release

- Keyboard navigation, screen-reader names, high contrast, and 100-200% DPI.
- Global hotkey is opt-in and configurable.
- Cold-start and memory measurements on a release build.
- MSIX and portable framework-dependent packaging experiments.
- Signed release artifacts and a documented uninstall path.

Exit: a tested release candidate runs without elevation on supported Windows 11
systems.

## Acceptance criteria

- Normal startup and all version 1 controls work without elevation.
- No network request occurs during normal use.
- Closing the window exits the process unless the user explicitly enables a
  tray option in a later release.
- The display action never invents a fifth topology or targets an individual
  monitor.
- The audio list distinguishes playback endpoints, recording endpoints, and
  application sessions.
- Per-application volume updates do not change the master device volume.
- The device page distinguishes present devices from previously paired devices.
- Unsupported operations remain disabled or open Windows Settings with a clear
  explanation.
- Failures are visible to the user and logged locally without device serial
  numbers or other unnecessary identifiers.

## Validation matrix

Test at minimum:

- Laptop display only.
- Laptop plus HDMI/DisplayPort monitor.
- USB headset with playback and microphone endpoints.
- Bluetooth headset that exposes separate media and communications profiles.
- Built-in speakers and microphone.
- At least two applications with active audio sessions.
- Device connect/disconnect while the app is open.
- Standard account, high contrast, and 150% DPI.

## Open product decisions

1. Final product name and icon.
2. Whether a tray mode or global hotkey belongs in version 1.1.
3. Whether changing all three Windows audio roles together should be the
   default behavior.
4. Resolved: publish both a small framework-dependent Lite build and a larger
   self-contained Portable build.

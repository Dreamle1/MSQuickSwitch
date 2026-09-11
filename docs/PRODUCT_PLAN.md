# Product plan

## Product statement

Current candidate: **v1.1.0.0**, covering shared-overlay UI stabilization and
release validation. Physical hardware checks and Store submission remain
pending; see [acceptance checklist](RELEASE_ACCEPTANCE_1_1_0.md). Expanded
keybinds and device workspace work remain the subsequent v1.1.1.0/v1.1.2.0
milestones, not a reason to delay this stabilization candidate indefinitely.
Development has now started on [v1.1.1.0](MILESTONE_1_1_1.md); its source version
is separate from the frozen v1.1.0.0 test artifacts.

WinQuickSwitch gives a Windows 10 or Windows 11 user one compact, summonable place to switch
display and audio configurations and inspect connected devices. It should feel
like a fast control widget, not another full Settings application.

## Assumption

The phrase "window mode aka Ctrl + P" is interpreted as the Windows projection
menu opened by `Win + P`. The four projection modes are the first display
feature. Per-monitor positioning, resolution, refresh rate, HDR, and virtual
desktops are outside the first release.

## Free and Paid edition contract

The product will be shipped from one source tree as two editions. The existing
Store product remains the Paid edition so current customers, settings, profiles,
and its listing are preserved. A separate `WinQuickSwitch Free` Store product
will provide the core utility without named profiles.

### Free edition

- Display projection switching: PC screen only, Duplicate, Extend, and Second
  screen only.
- Safe exact-layout Extend restoration and authoritative Win+P mode detection.
- Audio endpoint, session-volume, device, Wi-Fi/Bluetooth, and taskbar controls.
- Display, panel, and favorite endpoint keybinds.
- Four playback and four microphone favorites.
- Tray, startup, theme, Windows Settings links, EULA, and privacy policy.
- No Profiles panel, profile persistence, profile hotkeys, profile tray actions,
  or profile application/undo.

### Paid edition

Paid includes every Free feature plus named profiles, profile persistence,
combined display/audio/taskbar actions, profile hotkeys, pinned profile tray
actions, profile duplication/editing, and profile undo. Future automatic or
scheduled profile behavior is also Paid-only.

### Edition invariants

- Direct switching and ordinary keybinds are never paywalled.
- Free must not load, save, register, or dispatch profile functionality.
- Paid retains the existing `%LOCALAPPDATA%\WinQuickSwitch` data path.
- Free uses an edition-specific data path and may export core settings for a
  one-time Paid import.
- Only one edition may own the resident process and global hotkeys at a time.
- Public standalone Lite/Portable artifacts are Free-only until a separate
  licensing mechanism exists for Paid artifacts.

### Proposed future Free Lite monetization

Advertising is being considered only as a future, narrowly scoped Free Lite
experiment. It is **not implemented or enabled**. Paid must remain completely
ad-free; Free Portable and Microsoft Store packages also remain ad-free during
the experiment. The proposal, provider risks, privacy boundaries, build gating,
performance budgets, testing, and rollout gates are documented in the
[Free Lite advertising plan](FREE_LITE_ADVERTISING_PLAN.md).

The current no-network acceptance criterion remains authoritative until a
provider passes the WPF compatibility, embedded-format, privacy, policy, and
performance gates. A future approved ad-enabled Free Lite build would receive
only a disclosed, provider-specific network exception while its normal main
window is visible. Paid and every ad-disabled build would retain the no-network
requirement.

## Goals

1. Reach the most-used controls in one or two clicks.
2. Start quickly and use little memory while open.
3. Work without administrator rights for normal operations.
4. Make hardware state clear before a user changes it.
5. Prefer documented Windows APIs and clearly isolate compatibility-sensitive
   code.
6. Stay effectively idle while hidden and return with one global shortcut.

## Shared overlay UI milestone (v1.0.26.0)

- The main window, right-side launcher, workspace widgets, and supported
  floating windows create the same complete reusable feature controls.
- Display, Audio, Devices, Options, Profiles, and About share one coordinator,
  state model, typed command path, watcher demand, and visual theme.
- Display, Options, and About fit fixed content; Audio, Devices, and Paid
  Profiles support persistent manual resizing and a Fit content action.
- Default workspace composition is Display, Audio, and Devices. Options,
  Profiles, and About are restored only when the user opens them.
- Refresh, hot-plug, and theme changes update existing controls without
  changing card geometry or interrupting a drag/resize gesture.

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

The resident widget has four compact panels and shows one at a time:

### Display

- Show the current topology when it can be determined reliably.
- Offer PC screen only, duplicate, extend, and second screen only.
- Apply display-mode actions immediately from the widget or their global
  shortcuts, without an extra confirmation prompt.
- Show success or a useful error after Windows completes the request.

### Audio

- Show the current default playback and recording endpoints.
- List active playback sessions by application.
- Change per-application volume and mute state.
- Choose a playback or recording endpoint.
- Save up to four favorite playback outputs and assign direct global shortcuts.
- Refresh automatically when sessions or endpoints appear or disappear.

### Devices

- Group connected devices as Bluetooth, USB/wired, and unknown/other.
- Show friendly name, class, connection/presence state, and battery percentage
  when Windows supplies it.
- Update the list when a device connects or disconnects.
- Show current Wi-Fi and Bluetooth radio state and allow a permission-gated
  user-initiated on/off request.
- Link to the appropriate Windows Settings page for pairing or advanced device
  management.

### Taskbar

- Show the current taskbar visibility mode.
- Turn taskbar auto-hide on or off with explicit Show and Hide actions.
- Link to Windows taskbar, display, and notification settings for the broader
  behavior controls that Windows owns.

## Milestones

### M0 - planning and shell

- Repository, scope, architecture, WPF shell, and build instructions.
- Decisions and risks are explicit.

Exit: another developer can clone the repository and identify the next slice
without needing an oral handoff.

### M1 - projection modes

- [x] Add an `IDisplayModeService`.
- [x] Apply allowlisted projection topologies directly through
  `SetDisplayConfig` for internal, clone, and external, plus the Windows
  projection command for Extend. Capture complete Extend paths and modes before
  leaving that topology, then restore the exact in-memory layout directly. Use
  the PC-screen-only bridge only when an exact snapshot is unavailable.
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
- [x] Add persisted favorite playback outputs with direct global shortcuts.
- [x] Link to the general Windows **System > Sound** page.
- [x] Link to the Windows per-application Volume mixer while retaining the
  lightweight in-widget app volume and mute controls.

Exit: session controls work for ordinary desktop apps and endpoint switching
either succeeds or degrades clearly.

### M4 - connected devices

- [x] Enumerate present Bluetooth and USB-wired Plug and Play devices.
- [x] Normalize duplicate physical devices that expose several interfaces.
- [x] Watch for add, remove, and update events.
- [x] Add Settings deep links; keep pairing and device removal outside the app.
- [x] Add permission-gated Wi-Fi and Bluetooth radio toggles with Settings
  fallback, without elevation or a resident polling service.

Exit: the list updates live and does not label historical/non-present devices
as connected.

### M5 - polish and release

- [x] Add the single-instance resident window lifecycle.
- [x] Add a default `Win + Shift + Q` show/hide shortcut.
- [x] Open beside the pointer and remain inside the nearest monitor work area.
- [x] Add direct panel and menu keyboard navigation.
- [x] Suspend panel refreshes and the Core Audio watcher while hidden.
- [x] Make show/hide and direct-panel global shortcuts configurable, with
  conflict-safe disabled options.
- [x] Add a persisted dark/light theme option, including the native title bar.
- [x] Add optional per-user start-with-Windows behavior that launches hidden.
- [x] Add configurable global shortcuts for all four projection modes.
- [x] Add an explicit Unset action for clearing any configured shortcut.
- [x] Add a native notification-area icon with Open and Exit actions.
- [x] Add taskbar Show/Hide controls and related Windows Settings links.
- Complete screen-reader, high-contrast, and 100-200% DPI validation.
- Cold-start and memory measurements on a release build.
- MSIX and portable framework-dependent packaging experiments.
- Signed release artifacts and a documented uninstall path.

Exit: a tested release candidate runs without elevation on supported Windows 10
and Windows 11 systems.

## Acceptance criteria

- Normal startup and all version 1 controls work without elevation.
- No network request occurs during normal use.
- Closing, `Esc`, and click-away hide the resident widget; the visible
  **Quit** action exits the process.
- A second launch reveals the existing instance and exits without creating
  another resident process.
- The display action never invents a fifth topology or targets an individual
  monitor.
- The audio list distinguishes playback endpoints, recording endpoints, and
  application sessions.
- Per-application volume updates do not change the master device volume.
- The device page distinguishes present devices from previously paired devices.
- The Options panel can read taskbar visibility, toggle auto-hide explicitly,
  and open the related Windows Settings pages.
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

1. Resolved: retain `WinQuickSwitch` for the existing Paid product and reserve
   `WinQuickSwitch Free` as the new Free product.
2. Whether changing all three Windows audio roles together should be the
   default behavior.
3. Resolved: publish both a small framework-dependent Lite build and a larger
   self-contained Portable build for Free; Paid standalone artifacts remain
   internal until licensing is defined.

## M6 - favorites and profiles (Paid)

The next implementation phase combines the existing quick audio favorites
with named, partial desk profiles. A favorite is one fast endpoint action; a
profile is a named bundle whose omitted fields leave the current state alone.

### M6.1 - profile foundation

- [x] Add a versioned profile catalog with atomic local persistence.
- [x] Normalize duplicate IDs, invalid endpoints, unsupported taskbar states,
  invalid volume values, and excess pinned profiles.
- [ ] Replace fixed hotkey action IDs with dynamic built-in, favorite, and
  profile targets.
- [x] Unregister dynamic profile hotkeys during reapply and shutdown.
- [x] Restore the previous working registration set when a live shortcut
  replacement conflicts or fails.
- [x] Normalize duplicate profile shortcuts and report cross-category owners.
- [x] Add an edition-aware typed command catalog and route existing global
  hotkeys, shared shortcut rows, profile Apply, native tray actions, and the
  overlay launcher/footer through it.
- [ ] Preserve existing settings and favorite-output shortcuts during migration.

### M6.2 - favorites

- [x] Keep four playback favorites and add four microphone favorites.
- [x] Add editable aliases and explicit ordering.
- [x] Add role selection and explicit remove behavior.
- [x] Keep disconnected favorites saved for later reconnection.
- [x] Expose favorites in the Audio panel and shortcut editor.
- [x] Expose favorites in the Profiles editor and tray menu.

### M6.3 - profiles v1

- [x] Add a Profiles panel with save-current, pin, apply, and delete actions.
- [x] Add profile editing and duplication.
- [x] Store optional display topology, playback/recording endpoints, and
  taskbar state values.
- [x] Store and apply microphone mute and master volume values.
- [x] Apply display first, wait for topology settlement, refresh audio, then
  apply endpoint and taskbar changes.
- [x] Report partial success by component when an endpoint or taskbar change
  cannot be completed.
- [x] Provide an in-session undo action.
- [x] Give profiles optional dynamic global shortcuts with collision-safe registration.
- [x] Keep global projection actions immediate; reserve timed rollback for
  future custom per-monitor display profiles.

### M6 acceptance criteria

- Existing settings migrate without losing shortcuts or favorite outputs.
- A disconnected endpoint remains in a favorite or profile until the user
  removes it.
- A profile can omit any component without changing that component's state.
- Profile hotkeys remain hidden on success and reveal the Profiles panel on a
  warning or failure.
- Profile application is covered by fake platform backends; real hardware
  mutations remain attended checks.

## M7 - compact UI and optional overlay

M7 improves presentation without changing the safe display and audio
boundaries. The first increment adds an opt-in dimmed right-sidebar overlay;
the compact home surface and detachable floating widgets follow as separate
increments.

### M7.1 - sidebar overlay foundation

- [x] Persist an overlay enable switch, dimming opacity, and sidebar width.
- [x] Keep the overlay disabled by default for existing installations.
- [x] Add a dedicated optional overlay hotkey without renumbering existing
  hotkey IDs.
- [x] Add a monitor-aware borderless overlay with a right-side feature hub.
- [x] Close through Esc, the same shortcut, the close button, or backdrop click.
- [x] Provide a direct **Open main window** action so the overlay never blocks
  access to the complete Audio, Devices, Options, Profiles, or About panels.
- [x] Add compact Display, Audio, Devices, Options, Profiles (Paid), and About
  tabs. Route actions through the MainWindow coordinator and open the full
  panel when an action needs the complete inventory or editor.

### M7.2 - compact home surface

- [x] Make the main window action-first with four display-mode tiles, audio
  favorites, device status, and shortcut hints.
- [x] Keep detailed settings pages reachable through secondary navigation.
- [x] Preserve native resize, minimize, maximize, keyboard focus, and edition
  gating; add an explicit normal-window pin toggle.

### M7.3 - optional floating widgets

- [x] Detach the Display surface into a draggable, resizable floating widget.
- [x] Add lightweight floating Audio and Devices surfaces for favorites and
  wireless controls.
- [x] Persist its size, position, and pinned state with safe work-area clamping
  when a monitor is removed.
- [x] Add reset-layout and reattach actions.

### M7 acceptance criteria

- Overlay presentation alone never changes display topology, placement, or
  rotation.
- Free and Paid both expose the overlay; Profiles remain Paid-only.
- Existing settings and shortcuts load unchanged with the overlay off.
- Manual validation covers mixed DPI, negative-coordinate monitors, monitor
  hot-plugging, keyboard-only use, and borderless-windowed applications.

## M8 - feature-complete widget workspace

M8 replaces the button-list sidebar with an Xbox-style workspace while keeping
the normal window as a fallback. The first implementation slice is now in the
product: the edition-aware feature catalog, typed overlay command/result
contracts, monitor-scoped layout persistence, snapping/clamping geometry, and
the right-launcher workspace shell are shared by Free and Paid.

### M8.1 - parity contract and workspace shell

- [x] Add one edition-aware feature descriptor catalog and parity gate.
- [x] Add typed quick-switch commands, result severity, refresh scopes, and
  smart-close dispositions.
- [x] Add a dimmed monitor workspace with quick-workspace routing, launcher navigation, cards,
  drag/resize/snap, z-order, hide/restore, feedback, and inspector hosting.
- [x] Add `Ctrl+0`, `Ctrl+1` through `Ctrl+6`, Escape, backdrop-close, and
  keyboard launcher navigation.
- [x] Persist per-monitor card layouts and inspector width with safe migration
  from the legacy sidebar-width field.
- [x] Keep display mutations on the existing authoritative transition path;
  workspace layout never changes topology, placement, or rotation.
- [x] Remove the redundant Home launcher entry, enlarge feature-specific card
  presets, render connected devices in the quick card, and reserve feedback
  banners for meaningful results, warnings, and failures.
- [x] Add layout schema v2 so preset sizes and user-resized sizes persist
  correctly, restore drag movement, and keep Profiles/About out of the default
  quick-widget composition.

### M8.2 - shared feature views and coordinator extraction

- [x] Extract immutable `QuickSwitchState` snapshots and an
  `IQuickSwitchCoordinator` from the remaining `MainWindow` control state.
- [x] Host the existing full Display, Audio, Devices, Options/Favorites,
  Profiles, and About views directly inside the inspector instead of action
  lists.
- [x] Add complete app-volume, endpoint alias/order, profile management,
  confirmation, and Undo controls to the overlay inspector.
- [x] Add revision-aware cancellation so stale refresh results cannot replace a
  newly selected inspector.

### M8.3 - release validation

- [ ] Complete attended keyboard, screen-reader, high-contrast, mixed-DPI,
  monitor-removal, hot-plug, tray, and Free/Paid validation.
- [x] Replace the compatibility sidebar only after inspector parity and the
  automated gate pass together.
- [x] Publish fresh Free/Paid Lite/Portable and Store-test artifacts.
- [ ] Update the store screenshots to show the workspace.

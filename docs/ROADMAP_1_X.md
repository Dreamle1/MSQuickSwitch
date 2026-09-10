# WinQuickSwitch 1.x roadmap

## Versioning policy

WinQuickSwitch will use a stable `1.1` release line for the three planned
increments:

- `1.1.0.0` delivers overlay and UI stabilization.
- `1.1.1.0` delivers command and keybind expansion.
- `1.1.2.0` delivers the device workspace and capability-aware Bluetooth work.
- Later fixes may continue as `1.1.3.0`, `1.1.4.0`, and so on.
- The fourth component remains `0` for normal Store and public builds.
- `2.0.0.0` is reserved for a breaking settings, compatibility, licensing, or
  product-model change.
- Free and Paid packages use the same product version for each shared release.

Each release must pass the complete automated suite, Free/Paid Release builds,
package validation, and the attended Windows checks relevant to its changes.
Release candidates also receive isolated Paid/Free Sandbox performance runs.
The baseline report tracks startup, navigation, refresh latency, private-memory
growth, handles, and threads; budgets become blocking only after repeatable host
baselines have been accepted.

## v1.1.0.0 - Overlay and UI stabilization

Goal: make the current window, sidebar, workspace, and floating surfaces feel
consistent and reliable before adding more device mutations.

Current progress: the shared state pipeline now gives Audio and Devices
latest-request-wins refresh leases. A newer surface refresh cancels the older
request, waits for the existing platform read to release its gate, and checks
cancellation before applying or publishing an inventory snapshot. The public
candidate now targets v1.1.0.0; publication remains gated on complete acceptance.
The coordinator now publishes one cached immutable state boundary without
reading hidden WPF lists, buttons, or status labels, and the unused compact and
fallback workspace UI builders have been removed.
The retired compatibility sidebar is now removed as well. Shared feature
refresh failures remain inside the resident app with a visible Retry state;
launcher shortcuts focus the first usable card control; common controls meet a
40-DIP target; PerMonitorV2 is declared explicitly; and light/high-contrast
selection text follows the active system palette. Generated first-run layouts
fit and arrange once, while restored user geometry remains authoritative.
Overlay dismissal now restores the prior foreground application, inspectors
receive keyboard focus, Ctrl+0 returns to the workspace, status feedback is a
screen-reader live region, resize targets meet the 40-DIP minimum, and guarded
async lifecycle entry points keep the resident app recoverable after failures.
The isolated Paid/Free suite now covers edition-aware Ctrl+number navigation in
both the conventional window and overlay, launcher arrow navigation, Escape
inspector dismissal, first-start overlay preference, live theme changes, and a
920x700 constrained workspace. The shared catalog also compresses Free overlay
numbering so Ctrl+5 opens About instead of targeting the unavailable Profiles
feature. Physical mixed-DPI/high-contrast, screen-reader, hot-plug, tray, and
Store-install checks remain the release boundary.
The isolated suite also audits every visible keyboard control for an accessible
UI Automation name and renders main-window and constrained-overlay Options in
the system-color High Contrast palette. A screenshot-level contrast gate now
detects checked labels that exist in automation but disappear visually. The
shared checkbox template was corrected so checked content resolves the live
palette directly. The real Windows High Contrast toggle and screen-reader
experience remain attended acceptance checks.

### Overlay shell

- Remove the **Quick workspace** title and subtitle from overlay mode.
- Reclaim the header space for the widget canvas while retaining the overlay
  close control and the persistent right launcher.
- Keep the overlay scoped to the pointer's active monitor and restore focus to
  the previously active application when it closes.
- Preserve widget position, size, visibility, and z-order without changing
  geometry during refresh, theme changes, or reopen.

### Phase 1 - shell cleanup and lifecycle

- Remove the visible **Quick workspace** heading/subtitle and its unused header
  space while preserving the overlay close control.
- Remove or update obsolete Quick workspace wording in accessibility names,
  tooltips, README content, and compatibility overlay surfaces.
- Make open, close, reopen, global-shortcut, tray, and main-window handoff use
  one overlay lifecycle coordinator.
- Restore focus to the previously active application after dismissal and ensure
  no hidden topmost window remains capable of blocking input.
- Make startup honor the saved main-window/overlay preference on the first
  invocation, not only after one open/close cycle.

### Phase 2 - authoritative widget persistence

- Store geometry only after drag/resize completion and immediately before
  overlay close.
- Restore saved geometry before making cards visible, then clamp only when the
  active monitor's usable work area makes the saved bounds impossible.
- Never reconstruct or resize a card because of inventory refresh, theme
  change, inspector navigation, or watcher notification.
- Preserve hidden-widget bounds and z-order so reopening a removed widget does
  not behave like a new widget.
- Add a versioned migration for legacy monitor identities and geometry without
  changing display topology.

### Phase 3 - shared visual system and responsive layout

- Convert remaining hard-coded colors to dynamic theme resources, including
  ComboBox popups, sliders, list selection, disabled controls, scrollbars, and
  native title bars.
- Standardize feature headers, action placement, icon tooltips, card padding,
  minimum hit targets, focus indicators, error banners, and scroll ownership.
- Rework fixed table/column assumptions in Audio, Devices, and Options so labels
  wrap or columns adapt instead of clipping.
- Measure every surface in DIPs and re-evaluate monitor bounds when crossing
  monitors with different scale factors.
- Keep long inventories internally scrollable while card chrome and primary
  actions remain visible.

### Phase 4 - accessibility and recovery

- Add automation names/help text for icon-only actions, device rows, sliders,
  shortcut fields, launcher items, and widget chrome.
- Define a complete keyboard path through launcher, widgets, inspectors,
  dialogs, tray-opened windows, and dismissal.
- Respect high contrast, reduced animation, text scaling, and visible focus.
- Add guarded exception handling around asynchronous view loading so a failed
  panel can show Retry without making the resident app unreachable.
- Repair tray Open/Exit sequencing and verify process exit when an overlay,
  inspector, or floating widget is active.

### UI upgrades and fixes

- Finish the shared feature-view migration so the main window, sidebar,
  workspace cards, and floating windows use the same controls and coordinator.
- Standardize toolbar order, icon meaning, minimum control size, spacing,
  typography, focus states, scroll behavior, and empty/loading/error states.
- Complete live light/dark/high-contrast theme updates for popups, combo boxes,
  sliders, lists, and simultaneously open widgets.
- Make responsive layouts work at 100%, 150%, and 200% DPI and on narrow or
  negative-coordinate monitors.
- Finish the remaining coordinator extraction and revision-aware refresh
  cancellation so stale hardware results cannot replace newer UI state.
- Validate tray Open/Exit, startup directly into the configured surface, and
  overlay recovery after an exception or interrupted refresh.

### v1.1.0.0 acceptance gates

- No duplicated feature UI builders remain for supported shared surfaces.
- Opening, closing, refreshing, or changing theme does not resize a widget.
- The overlay has no Quick workspace heading and wastes no equivalent blank
  header area.
- Keyboard-only, screen-reader, high-contrast, mixed-DPI, monitor-removal,
  hot-plug, tray, and Free/Paid checks pass.

### v1.1.0.0 scope boundary

- No new hardware mutations, Bluetooth device actions, automation rules, or
  display-topology behavior are added in this release.
- Existing settings remain compatible; a migration may add fields but must not
  discard shortcuts, profiles, widget layouts, or edition-specific data.

## v1.1.1.0 - Command and keybind expansion

Goal: make every safe, repeatable quick action available from one typed command
catalog and the shortcut editor.

Current progress: registration reliability is now in place before the command
catalog expands. Reapplying or disposing bindings unregisters both built-in and
dynamic Paid profile IDs. A failed live reconfiguration restores the last
working native registration set, duplicate profile shortcuts normalize to one
owner, and cross-category duplicate profile bindings report both the existing
owner and the profile that needs attention. Profile native IDs are derived
from stable profile IDs rather than list position, and internal overlay
navigation chords are reserved. Phase 2 now has one edition-aware typed
descriptor catalog for every persisted built-in hotkey and dynamic Paid
profile Apply command. Global WM_HOTKEY dispatch, shared Options rows, and
profile Apply controls resolve through it instead of maintaining parallel
action switches. Native tray Open, Exit, and favorite selections now resolve
through the same descriptors after the Windows menu closes. The overlay
launcher and footer emit typed navigation, Open, and Exit commands; widget
geometry remains local presentation behavior. Development source now targets
v1.1.1.0 while the v1.1.0.0 candidate artifacts remain frozen. The initial
editor slice groups built-in commands into searchable, collapsible catalog
categories, preserving row identity on refresh and per-surface expansion.
See [milestone progress](MILESTONE_1_1_1.md) for completed and outstanding work.

### Command foundation

- Complete the dynamic command registry for built-in actions, favorites, and
  profiles instead of adding more fixed action IDs.
- Fix global-hotkey cleanup so reapplying settings and closing the app also
  unregister every dynamically assigned profile hotkey.
- Normalize duplicate persisted profile shortcuts before registration and
  report which profile needs attention instead of partially failing startup.
- Use the same command descriptors for buttons, tray actions, overlay actions,
  profile steps, and global shortcuts.
- Add conflict detection, reserved-combination validation, clear error text,
  Unset, Reset section, and Reset all actions.
- Register and unregister shortcuts without restarting the app.
- Preserve existing shortcut values during settings migration.

### Phase 1 - registration reliability

- [x] Fix dynamic profile-hotkey cleanup before expanding the registered command
  count; reapply and disposal must unregister built-in and profile IDs.
- [x] Normalize duplicate persisted shortcuts before registration and retain the
  last known working set if part of a new configuration fails.
- [x] Replace positional expansion with stable command keys and explicit persisted
  IDs while preserving all existing bindings during migration.
- [x] Reserve internal overlay navigation keys separately from global shortcuts so
  `Ctrl+0` and edition-aware `Ctrl+1...6` cannot be assigned ambiguously.

### Phase 2 - shared command catalog

- [x] Define one edition-aware descriptor for label, category, availability,
  default binding, execution, result, and whether a successful action dismisses
  the overlay.
- [ ] Route shortcut editor rows, global registration, tray actions, overlay
  buttons, profile steps, and command tests through that catalog.
  Shortcut rows, global dispatch, profile actions, native tray actions, and the
  overlay launcher/footer are complete. Remaining target-specific audio,
  device, and taskbar actions still need descriptors. Floating windows now host
  the shared typed feature views directly; their retired string-action fallback
  has been removed.
- Separate commands requiring a current endpoint/device/profile target from
  fixed commands and disable them with an explanation when their target is not
  available.

### Phase 3 - shortcut editor redesign

- Organize shortcuts into collapsible General, Navigation, Display, Audio,
  Devices, Taskbar, Favorites, and Paid Profiles sections.
- Add search, conflict highlighting, current owner details, Unset, Reset
  section, and Reset all.
- Capture one combination at a time, reject unsupported or Windows-reserved
  combinations before saving, and keep the prior binding when registration
  fails.
- Show profile/favorite aliases while persisting stable IDs so rename and
  reorder operations do not break bindings.

### New shortcut targets

- Open or toggle the overlay, main window, Display, Audio, Devices, and Options.
- Apply each display projection mode.
- Apply any configured playback or microphone favorite.
- Select Default, Communications, or Both roles for an eligible audio endpoint.
- Toggle master output mute and microphone mute; adjust master output volume in
  user-configurable steps.
- Toggle Wi-Fi, Bluetooth, and taskbar auto-hide.
- Refresh Display, Audio, Devices, or all active surfaces.
- Paid only: apply any saved profile, apply pinned profile slots, move to the
  next or previous pinned profile, and undo the most recent profile application.

Actions that may open consent UI, have ambiguous targets, or remove data are
not global-shortcut targets.

### Phase 4 - execution feedback and accessibility

- Give every command a Busy/Success/Warning/Error result and announce important
  failures through an accessible status region.
- Keep volume-step commands rate-limited so holding a shortcut cannot overwhelm
  the Core Audio refresh pipeline.
- Apply the existing smart-close policy consistently: successful switching may
  dismiss the overlay, while warnings and failures keep the relevant surface
  visible.
- Document defaults and provide a printable/exportable shortcut summary without
  exporting device identifiers.

### v1.1.1.0 acceptance gates

- Every visible shortcut target maps to a typed command with an edition-aware
  availability rule and result message.
- Free registers no profile command or profile hotkey.
- Duplicate shortcuts cannot be saved, and failed registrations preserve the
  previous working shortcut.
- Profile and favorite shortcuts survive rename, reorder, disconnect, restart,
  and Free/Paid settings migration.

### v1.1.1.0 scope boundary

- Shortcuts execute one allowlisted action; arbitrary macros, command lines,
  scripts, multi-step user-authored workflows, and destructive actions remain
  unsupported.
- Bluetooth per-device shortcuts wait for the capability model in `v1.1.2.0`.

## v1.1.2.0 - Device workspace and Bluetooth actions

Goal: make the Devices view easier to inspect while adding only Bluetooth
actions that Windows and the individual device expose reliably.

### Device organization

- Add sortable columns for Name, Connection, Type, and Status.
- Support ascending/descending sort, search, and filters for Bluetooth, wired,
  connected/present, disconnected/paired, audio, input, and other devices.
- Add optional grouping and collapse state without changing the underlying
  inventory order.
- Persist the user's sort, filter, and grouping choices per edition.
- Keep stable device IDs internally; never use a friendly name as an action
  identifier.

### Phase 1 - device data contract

- Extend the immutable device model with an authoritative connection-status
  enum, pairing/presence information when Windows supplies it, and explicit
  action capabilities.
- Stop collapsing separate same-name Bluetooth devices; deduplicate by stable
  Windows identity/profile relationships and use names only for presentation.
- Keep raw hardware identifiers out of visible errors, logs, telemetry, and
  exported settings.
- Update watchers in place so hot-plug events preserve selection, sort, filter,
  scroll position, and active pointer gestures.

### Phase 2 - sorting, search, filtering, and grouping

- Make column headers keyboard-accessible sort controls with a visible
  ascending/descending indicator and stable ID tie-breaking.
- Add a fast name search plus filters for transport, connection/presence state,
  and device type.
- Add optional grouping with remembered collapsed state and a one-action Clear
  filters control.
- Persist presentation preferences separately from the device inventory so a
  device refresh cannot overwrite them.

### Phase 3 - read-only Bluetooth capability discovery

- Enumerate paired and currently connected Bluetooth devices alongside the
  present Plug and Play inventory without labeling every paired device as
  connected.
- Subscribe to supported connection-status events and reconcile them with an
  authoritative refresh after changes.
- Classify each row as Connect supported, Disconnect supported, Settings only,
  or Status only before enabling an action.
- Audit the Free and Paid package manifests for the documented Bluetooth
  capability and Store disclosure requirements.

### Bluetooth connect and disconnect

- Distinguish **connected**, **paired but disconnected**, **available**, and
  **unsupported/unknown** instead of treating all present devices as connected.
- Show Connect or Disconnect only when the platform adapter reports that the
  operation is supported for that device and profile.
- Keep pairing/unpairing separate from connection state and require explicit
  confirmation for unpairing if it is added later.
- Run every action as an explicit user request with Busy, Success, Warning,
  Error, timeout, cancellation, and post-action authoritative refresh states.
- Use Windows Settings as the fallback for audio, HID, controller, or vendor
  devices that do not expose a safe general-purpose programmatic action.
- Never disable/uninstall devices, restart drivers, or simulate Device Manager
  actions.

### Phase 4 - guarded Bluetooth actions

- Add a new platform interface for per-device actions; shared views and hotkeys
  never call Windows Bluetooth APIs directly.
- Implement only operations proven by capability discovery for the specific
  Bluetooth profile. Do not present closing a local API object as a guaranteed
  hardware disconnect.
- Add a short timeout, cancellation, retry, duplicate-action suppression, and
  an authoritative post-action status check.
- When Windows or the device profile does not offer a reliable operation, open
  Bluetooth Settings with a precise explanation.
- Keep Pair/Unpair outside the first `v1.1.2.0` scope unless a separate attended
  design covers consent, confirmation, credential prompts, and recovery.

Windows exposes a readable Bluetooth connection status and pairing APIs, while
connection control is profile-specific. Bluetooth LE GATT devices may support a
maintained session, but that is not a universal connection switch for every
paired headset, keyboard, mouse, or controller. The UI must therefore be
capability-aware rather than promise Connect/Disconnect for every row.

### Edition policy

- Sorting, filtering, status, and supported manual Connect/Disconnect actions
  are core device controls and remain available in Free and Paid.
- Paid profiles may reference a supported Bluetooth action only after the
  manual action is proven reliable and undo/error semantics are defined.

### v1.1.2.0 acceptance gates

- Sort/filter state survives restart and hot-plug updates without losing the
  current selection or rebuilding the host widget.
- Device actions are fake-backed in automated tests and require attended tests
  across representative BLE, Bluetooth Classic audio, HID, and controller
  devices before release.
- Unsupported devices show an explanation and Settings fallback, not a button
  that silently fails.
- No device serial number or unnecessary hardware identifier is logged.

### v1.1.2.0 scope boundary

- The release does not promise a programmatic Connect or Disconnect action for
  every Bluetooth device class.
- Pairing, unpairing, forgetting, disabling, uninstalling, driver management,
  and elevation remain outside scope.
- Device-triggered profile automation remains a later Paid feature.

## Future large release - Paid profile automation

Goal: make Paid profiles proactive without weakening manual control.

- Optional triggers for monitor topology, audio endpoint availability,
  supported Bluetooth presence/connection, sign-in, and schedules.
- Priority rules, cooldowns, dry run, manual override, action history, and Undo.
- A clear preview of what a rule will change before it is enabled.
- Automation remains entirely local and disabled by default.
- Free retains manual switching and all ordinary keybinds.

## Future large release - Advanced display and audio exploration

This phase starts only after platform prototypes and safety tests succeed:

- Saved per-monitor resolution, refresh rate, orientation, and placement
  profiles with timed confirmation and exact rollback.
- Monitor brightness or physical-monitor controls where DDC/CI support is
  available and reliable.
- Application-aware audio routing rules where supported by documented Windows
  interfaces.
- Profile import/export with a preview, schema validation, and no cloud account.

These are research candidates, not Store promises. Any display work must retain
the existing safeguards that preserve monitor placement and rotation.

## Recommended implementation order

1. Ship `v1.1.0.0` as a stabilization release and update Store screenshots.
2. Build the shared command registry, then ship the keybind expansion as
   `v1.1.1.0`.
3. Prototype Bluetooth operations behind a capability-reporting platform
   interface before exposing buttons; ship proven device work as `v1.1.2.0`.
4. Collect real-world feedback before assigning Paid automation to a later
   minor version.
5. Keep advanced display/audio work experimental until rollback and hardware
   compatibility gates are satisfied.

## Cross-release dependencies and release procedure

- `v1.1.0.0` must finish the shared coordinator/view lifecycle before
  `v1.1.1.0` adds more commands that can update several visible surfaces.
- `v1.1.1.0` must finish stable typed command routing before `v1.1.2.0` exposes
  any device mutation through the UI or shortcuts.
- Sorting and read-only Bluetooth discovery may be prototyped independently,
  but mutation controls cannot ship before capability and attended-test gates.
- Every release updates the centralized product version, native and Store
  manifests, package build defaults, About text, documentation, and artifact
  directory labels together.
- Every release produces matching Paid and Free Release builds, validates the
  Store upload/MSIX packages, runs all non-mutating automated tests, and records
  attended Windows results before artifacts are linked.
- Store screenshots are refreshed only when the release changes a visible
  surface, and must use sanitized device/profile names.

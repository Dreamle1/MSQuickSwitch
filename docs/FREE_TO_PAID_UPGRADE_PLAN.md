# Free Lite to Paid upgrade placement plan

Status: **planning only**. This document does not add an upgrade control,
change product behavior, or enable advertising. It defines the first-party
promotion to evaluate before any third-party advertising provider.

## Recommendation

Start with one static, privacy-preserving **Upgrade to Paid** card in the normal
Free Lite main window. The card promotes WinQuickSwitch's own Paid edition and
opens its Microsoft Store product page only after an explicit user click.

This should come before the external advertising experiment because it can test
whether a promotional placement is useful without adding an SDK, WebView2,
third-party content, background networking, personalized advertising, or new
data sharing.

The initial rollout is Free Lite only. Free Portable and the Free Microsoft
Store package remain unchanged until the Free Lite result is reviewed. Paid
never displays an upgrade card.

## Product and privacy contract

This is a first-party house promotion, not a third-party Sponsored placement:

- Label it **Upgrade to Paid**, not **Sponsored**.
- Bundle all text and visual assets with the application.
- Make no network request to render, rotate, configure, or measure the card.
- Add no advertising SDK, WebView, cookie, identifier, analytics, remote
  configuration, or click beacon.
- Open the Paid Microsoft Store page only after the user activates the button.
- Send no device, display, audio, profile, shortcut, or application state.
- Do not include a tracking parameter in the initial link.
- Do not claim scarcity, a discount, or a price that the app cannot verify.

The Store app independently handles its page request after launch. Before
shipping, add a short disclosure to the Free privacy text: the Free Lite app may
show a first-party link to the Paid edition, WinQuickSwitch does not track
whether the link is clicked, and Microsoft processes Store activity under
Microsoft's own terms.

The current no-background-network product contract remains intact. A
user-initiated handoff to the Microsoft Store is treated like existing links to
Windows Settings: it is not background WinQuickSwitch network activity.

## Placement and interaction

### Location

Use one compact card in the normal Free Lite main-window shell, below the
feature content and above the window footer/status area. It remains outside the
reusable Display, Audio, Devices, Options, and About views so those shared views
stay edition-neutral.

Do not show the card in:

- the overlay workspace or right launcher;
- workspace cards or floating widgets;
- tray menus or notifications;
- startup, shutdown, error, or confirmation dialogs;
- the Paid edition;
- any screen while the normal main window is hidden.

The Free edition has no Profiles surface. The card may describe Profiles as a
Paid benefit, but it must not create an empty or disabled Profiles route in Free.

### Proposed content

Title:

> Save complete desk setups with WinQuickSwitch Paid

Body:

> Create named profiles that combine display, audio, volume, microphone, and
> taskbar settings. Pin profiles, apply them with shortcuts, and undo the last
> applied setup.

Primary action:

> View Paid in Microsoft Store

Secondary action:

> Not now

The copy must be checked against the current edition contract before release.
It must not imply that direct display switching, audio controls, favorites, or
ordinary keybinds require Paid.

### Frequency and dismissal

- Show at most one upgrade card at a time.
- Never use a popup, animation that steals attention, countdown, interstitial,
  modal dialog, startup takeover, or notification.
- **Not now** hides the card immediately and for 30 days.
- Store only `PaidUpgradeDismissedUntilUtc` in Free Lite's local settings.
- Do not reset the dismissal on ordinary updates or settings migration.
- Provide an Options control named **Show Paid upgrade suggestion** so a user
  can disable the card indefinitely or restore it after dismissal.
- Keyboard focus must follow ordinary tab order. The card must not receive
  initial focus when the window opens.

## Store link contract

Use the Paid product's 12-character Microsoft Store Product ID:

`ms-windows-store://pdp/?ProductId=<PaidStoreProductId>`

If the Store URI cannot be launched, offer the same user-initiated product page
in the default browser:

`https://apps.microsoft.com/store/detail/<PaidStoreProductId>`

Do not use the Paid package family name or application ID as the primary link.
Do not use `mode=mini`; Microsoft no longer supports that popup activation mode
as of February 2026.

The repository currently records the Free Store ID but not the Paid Store ID.
Before implementation:

1. Copy the Paid 12-character Store ID from Partner Center's Product identity
   page.
2. Verify it against the live Paid listing.
3. Record it once in an edition-aware product configuration source.
4. Add an automated exact-value test and an attended click-through test.

The Store ID is public configuration, not a secret, but it must not be guessed
from a package identity. Failure to launch either URI leaves WinQuickSwitch
open and shows one non-blocking local error with a copyable web address.

## Architecture

Keep this feature independent from the proposed `IAdvertisingService`. A house
promotion does not initialize or reserve an external advertising boundary.

Suggested product-owned components:

- `PaidUpgradePromotionViewModel`: immutable title/body/action state and local
  dismissal state.
- `IStoreProductLauncher`: opens an allowlisted Store Product ID and returns a
  typed success/failure result.
- `WindowsStoreProductLauncher`: Windows shell implementation using the Store
  URI with an explicit browser fallback.
- `FakeStoreProductLauncher`: deterministic test implementation that records
  the requested Product ID without opening Microsoft Store.
- `ProductConfiguration.ShowPaidUpgradePromotion`: true only for the approved
  Free Lite build configuration.

The view model receives only edition/build configuration and the store
launcher. It must not receive `IQuickSwitchCoordinator`, device inventories,
audio state, profiles, shortcuts, or monitor information.

### Build gating

The initial card is enabled only when:

`Edition=Free` + `Distribution=Lite` + `PaidUpgradePromotion=Enabled`

Paid, Free Portable, Free Store, and ordinary promotion-disabled builds render
no card and register no upgrade action. Build validation rejects an enabled
promotion for Paid.

Unlike a third-party provider, sharing the small product-owned launcher class
with other builds is acceptable, but Paid must have no reachable upgrade-card
route or promotion configuration. No advertising-provider dependencies are
introduced in any output.

## Measurement without tracking

The initial experiment deliberately has no in-app impression or click
telemetry. Evaluate it using:

- aggregate Microsoft Store Paid acquisitions before and after the rollout;
- Free Lite download counts already available from its distribution channel;
- support feedback and Store reviews;
- UI/performance regression evidence.

These signals do not establish exact attribution. Document that limitation
rather than adding silent analytics. If exact campaign attribution becomes
important, plan it separately, confirm Microsoft's supported mechanism, update
the privacy disclosure, and obtain user consent where required before adding
it.

## Performance budgets

Because the card is static and local, its budgets are intentionally strict
against an otherwise identical promotion-disabled Free Lite build:

| Area | Budget |
| --- | --- |
| Core main-window ready | Existing 2,500 ms gate still passes; median regression no more than 25 ms and p95 regression no more than 50 ms |
| Private memory after render | No more than 1 MiB retained increase |
| Handles and threads | No retained increase |
| Visible and hidden idle CPU | No measurable sustained increase |
| Background network | Exactly zero additional requests |
| Hidden resident state | No timer, refresh loop, or promotion-owned process |

The Store launch itself is a user action and is measured separately from core
startup. The promotion must not probe Store availability at startup.

## Testing

### Deterministic tests

- Free Lite creates exactly one upgrade card when it is eligible and not
  dismissed.
- Paid, Free Portable, Free Store, and promotion-disabled configurations never
  create the card or register its command.
- Build validation rejects `Edition=Paid` with promotion enabled.
- The action requests the exact configured Paid Store Product ID through
  `FakeStoreProductLauncher`.
- A failed Store launch produces a recoverable result and does not close or
  disable WinQuickSwitch.
- **Not now** persists the 30-day dismissal using an injected clock.
- Disabling the suggestion persists indefinitely; restoring it clears the
  dismissal without changing other settings.
- Ordinary refreshes and theme changes do not reconstruct the card or reset its
  dismissal.
- The promotion model has no dependency on coordinator or hardware state.

### UI and accessibility tests

- Verify the exact title, benefit copy, primary action, and **Not now** control.
- Verify light, dark, and high-contrast themes at 100%, 150%, and 200% DPI.
- Verify narrow-window wrapping, localization expansion, keyboard navigation,
  focus order, screen-reader name/help text, and visible focus indicators.
- Verify absence from overlay, widgets, launcher, tray, notifications, and Paid.
- Verify hiding/reopening the main window respects dismissal and does not alter
  widget geometry.

The Sandbox E2E test uses `FakeStoreProductLauncher`; it must not open the live
Store or generate acquisitions during automation. Perform one attended test of
the real Store URI on supported Windows versions after the Paid Store ID is
recorded.

### Privacy and performance tests

- Capture process connections before any click and prove the card generates no
  DNS or network activity.
- Scan logs/settings and verify only the dismissal timestamp/boolean is stored.
- Compare 20-run startup and complete E2E resource measurements with the card
  enabled and disabled.
- Verify hidden-state CPU, timers, handles, threads, and processes are unchanged.

## Luna implementation stages

Implement these stages sequentially after this planning PR is approved. Use a
new implementation branch from the approved base; do not implement production
changes on the planning branch. Each stage is one reviewable commit, must keep
all earlier tests green, and must stop at its exit gate before the next stage
begins. Later stages may adjust files owned by earlier stages only when their
new responsibility requires it.

### Stage 0 - human product-identity checkpoint

Owner: product owner; no Luna code changes.

- Copy the Paid edition's 12-character Product ID from Partner Center.
- Verify both the Store URI and browser URL open the live Paid listing.
- Confirm the Paid listing accurately describes the proposed benefits.
- Approve the card copy and confirm the experiment is Free Lite only.

Exit: record the verified public Product ID and approved copy in the issue or
implementation task. Luna must never guess the ID from the package identity,
package family name, or application ID. Stages 1 and 2 may be developed with a
fake value, but Stage 3 cannot complete without this checkpoint.

### Stage 1 - distribution and build gating

Responsibility: establish a safe build identity before any promotion behavior
exists.

Primary ownership:

- `src/WinQuickSwitch/Features/Product/ProductConfiguration.cs`
- `src/WinQuickSwitch/WinQuickSwitch.csproj`
- `src/WinQuickSwitch/Properties/PublishProfiles/Lite.pubxml`
- `src/WinQuickSwitch/Properties/PublishProfiles/Portable.pubxml`
- `tools/Build-StorePackage.ps1`
- product-configuration tests in `tests/WinQuickSwitch.Tests/Program.cs`

Add an explicit `Lite`, `Portable`, or `Store` distribution to product
configuration and a build-time promotion flag. The only eligible combination
is `Free + Lite + promotion enabled`. Build validation must reject promotion
enablement for Paid, Portable, or Store. Do not render UI or add Store-launch
behavior in this stage.

Required proof:

- Free Lite can be eligible when explicitly enabled.
- Free Portable, Free Store, and every Paid build are ineligible.
- Invalid combinations fail build/configuration validation.
- Existing Free/Paid feature and release-artifact tests remain green.

Exit: distribution can be observed and tested without inspecting an artifact's
file name. Suggested commit: `feat: add Free Lite distribution gating`.

### Stage 2 - local promotion state and persistence

Responsibility: implement deterministic promotion eligibility and user choice,
without WPF UI or operating-system launching.

Primary ownership:

- new product-owned promotion models under
  `src/WinQuickSwitch/Features/Product/`
- `src/WinQuickSwitch/Features/Widget/WidgetSettings.cs`
- `src/WinQuickSwitch/Features/Widget/JsonWidgetSettingsStore.cs`
- focused state/persistence tests in `tests/WinQuickSwitch.Tests/Program.cs`

Add `ShowPaidUpgradeSuggestion` and `PaidUpgradeDismissedUntilUtc` as
backward-compatible local settings. Implement the 30-day dismissal with an
injected clock and a promotion model that depends only on product
configuration, local settings, and time. It must not depend on the shared
quick-switch coordinator or hardware/application inventories.

Required proof:

- Dismissal persists, expires after 30 days, and tolerates clock boundaries.
- Permanent disable survives restart; restore changes only promotion state.
- Old settings migrate without losing unrelated values.
- The saved JSON contains no new device, endpoint, profile, shortcut, session,
  or hardware data.

Exit: all visibility decisions are unit-testable without constructing a WPF
window. Suggested commit: `feat: persist paid upgrade suggestion state`.

### Stage 3 - isolated Store launcher

Responsibility: add a user-initiated, allowlisted handoff boundary.

Primary ownership:

- new `IStoreProductLauncher` under `src/WinQuickSwitch/Features/Product/`
- new Windows implementation under
  `src/WinQuickSwitch/Platform/Windows/Store/`
- a fake implementation for unit and Sandbox tests
- focused launcher tests in `tests/WinQuickSwitch.Tests/Program.cs`

The production implementation uses only the verified Product ID, tries the
`ms-windows-store` URI after an explicit command, and returns a typed failure
that permits the browser fallback. It must never probe Store availability at
startup, add tracking parameters, or accept arbitrary URLs from UI state.

Required proof:

- The fake records the exact Product ID without launching another process.
- Store and fallback URIs are exact and contain no dynamic user/app data.
- Both launch failures are recoverable and leave the app usable.
- Nothing calls the launcher during construction, startup, refresh, or hide.

Exit: the real launcher passes an attended click-through to the correct Paid
listing; automated tests use only the fake. Suggested commit:
`feat: add isolated Store product launcher`.

### Stage 4 - main-window upgrade card

Responsibility: render the approved card in the one authorized surface.

Primary ownership:

- new `PaidUpgradePromotionView.xaml` and code-behind under
  `src/WinQuickSwitch/Features/Product/`
- the normal-window host in `src/WinQuickSwitch/MainWindow.xaml`
- composition/lifecycle changes in `src/WinQuickSwitch/MainWindow.xaml.cs`
  or `src/WinQuickSwitch/MainWindow.SharedUi.cs`
- focused UI contract tests

Add a third, auto-sized main-window grid row below the feature viewer and host
the card there. Keep the card outside reusable feature views. Bind it to the
Stage 2 model and Stage 3 launcher, and remove it from layout when ineligible or
dismissed. The CTA and **Not now** are the only actions.

Required proof:

- Exactly one card appears in eligible Free Lite.
- It is absent from Paid, Free Portable, Free Store, overlay, widgets, tray,
  notifications, Profiles, and startup/error dialogs.
- It does not steal initial focus or reconstruct on refresh/theme changes.
- Keyboard navigation, screen-reader names, text wrapping, light/dark/high
  contrast, narrow layout, and 100/150/200 percent DPI are usable.

Exit: fake-launcher UI tests pass and screenshots show no overflow or feature
content regression. Suggested commit: `feat: add Free Lite paid upgrade card`.

### Stage 5 - Options preference integration

Responsibility: expose predictable disable/restore controls without leaking the
promotion into other surfaces.

Primary ownership:

- `src/WinQuickSwitch/Features/SharedUi/OptionsFeatureView.xaml`
- `src/WinQuickSwitch/Features/SharedUi/OptionsFeatureViewModel.cs`
- `src/WinQuickSwitch/Features/SharedUi/QuickSwitchState.cs`
- the smallest required command/composition changes in
  `src/WinQuickSwitch/MainWindow.SharedUi.cs`
- focused Options/state tests

Add **Show Paid upgrade suggestion** only for the eligible Free Lite normal
main-window Options surface. The shared view factory already knows its surface;
use that surface context so the preference does not appear in overlay or
floating Options instances. Do not route promotion state through display,
audio, device, or profile refreshes.

Required proof:

- Disable hides the card immediately and persists indefinitely.
- Re-enable clears the permanent disable while preserving a still-active
  temporary dismissal; it must not silently reset the 30-day choice.
- The setting is absent from all excluded products and surfaces.
- Updating it does not reconstruct feature views or affect widget geometry.

Exit: the behavior is consistent from click through restart. Suggested commit:
`feat: add paid upgrade option control`.

### Stage 6 - Sandbox E2E and visual coverage

Responsibility: make every approved and excluded surface observable without
opening the live Store.

Primary ownership:

- `src/WinQuickSwitch/E2E/SandboxE2eRuntime.cs`
- `tools/WindowsSandbox/FeatureCoverage.json`
- `tools/WindowsSandbox/Run-SandboxUiSuite.ps1`
- `tools/WindowsSandbox/Launch-WinQuickSwitchSandbox.ps1`
- Sandbox contract tests

Teach Sandbox scenarios the distribution and promotion flags, inject the fake
launcher, and cover visible, clicked, dismissed, restarted, disabled, and
restored states. Capture Free Lite light/dark screenshots plus absence evidence
for Paid, Free Portable, Free Store, overlay, and widgets. Automated runs must
never open Microsoft Store or a browser.

Exit: the complete Sandbox suite passes and produces reviewable screenshots for
both presence and absence contracts. Suggested commit:
`test: cover Free Lite promotion in Sandbox`.

### Stage 7 - performance, privacy, and binary gates

Responsibility: prove the local card keeps the zero-background-cost contract.

Primary ownership:

- `tools/WindowsSandbox/Run-SandboxPerformanceComparison.ps1`
- `tools/WindowsSandbox/Test-PerformanceAnalysis.ps1`
- `tools/Test-ReleaseArtifacts.ps1`
- the minimum architecture/privacy documentation updates required by results

Compare otherwise identical enabled and disabled Free Lite builds over the
20-run protocol. Inspect pre-click connections, settings, binaries,
dependencies, handles, threads, timers, child processes, idle CPU, startup, and
retained memory. Add artifact checks that excluded builds cannot enable or
route to the card; source-string absence alone is not sufficient if shared code
is intentionally compiled into multiple outputs.

Exit: every budget in this plan passes, pre-click additional network activity
is exactly zero, and Paid/excluded Free outputs are proven behaviorally
promotion-free. Suggested commit:
`test: add promotion performance and privacy gates`.

### Stage 8 - attended validation and limited release readiness

Responsibility: prepare evidence for the human rollout decision, not silently
publish the feature.

Primary ownership:

- release checklist and implementation log
- Store/privacy copy only after legal/product review
- versioned artifact evidence generated by the existing release tools

Run the real Store handoff once on supported Windows, validate offline/failure
behavior, repeat the theme/DPI/accessibility checks, and build every Free/Paid
and Lite/Portable/Store artifact. Produce a promotion-disabled rollback build.
Do not publish to users without explicit product-owner approval.

Exit: H0-H3 evidence and every go/no-go gate are ready for review. Suggested
commit: `docs: record Free Lite house placement validation`.

### Luna execution rules

- Work on one numbered stage at a time and report its commit, changed files,
  tests, and remaining human checks before starting the next.
- Never combine Stage 1 gating with UI work; excluded-build safety must be
  reviewable first.
- Never use a guessed Store ID or open the real Store from automated tests.
- Do not introduce `IAdvertisingService`, an ad SDK, WebView2, tracking, remote
  content, or background refresh as part of these stages.
- Preserve unrelated user changes and do not rewrite shared files outside the
  listed responsibility without explaining why.
- A failed exit gate stops the sequence. Fix and rerun that stage before
  continuing.

## Rollout

### H0 - identity and copy readiness

- Record and verify the Paid Store Product ID.
- Confirm the Paid listing is live in intended markets and accurately describes
  every promoted feature.
- Approve final card copy, layout, privacy sentence, and fallback behavior.

Exit: a reviewer can follow both proposed links to the correct Paid product.

### H1 - fake-launcher implementation

- Add the build gate, static card, local dismissal, Options control, launcher
  abstraction, and fake implementation.
- Keep the real launcher disabled in automated configurations.
- Complete deterministic, UI, privacy, and performance tests.

Exit: all behavior is validated without opening Microsoft Store or adding
network activity.

### H2 - development and attended Store-link validation

- Enable the real Store launcher only in a local Free Lite development build.
- Test Store-app and browser-fallback paths, offline/failure behavior, themes,
  DPI, screen readers, and repeated show/hide.
- Confirm Paid and all excluded Free artifacts remain unchanged.

Exit: the correct Paid listing opens only after a deliberate click.

### H3 - limited Free Lite rollout

- Publish a clearly versioned Free Lite build to a limited distribution group.
- Keep a promotion-disabled rollback build.
- Review aggregate acquisition trends, support feedback, crashes, and resource
  results without adding telemetry.

Exit: continue only if users are not confused or disrupted and all technical
gates remain green.

### H4 - decision before external advertising

- Decide whether the house placement is useful enough by itself.
- Consider extending the first-party card to Free Portable or Free Store as a
  separate product decision and certification review.
- Resume the external advertising Phase A spike only if additional
  monetization is still justified.

## Go/no-go gates

- [ ] Correct Paid Store Product ID is recorded and independently verified.
- [ ] Paid listing is live and promoted feature copy is accurate.
- [ ] Only the normal Free Lite main window can render the card.
- [ ] Direct switching and ordinary keybinds are never presented as Paid-only.
- [ ] No network, tracking, SDK, WebView, child process, or background work is
      introduced before a user activates the Store link.
- [ ] Dismissal and permanent opt-out behave predictably and persist locally.
- [ ] Offline and failed Store launches leave the app fully usable.
- [ ] Accessibility, theme, DPI, narrow-window, and localization tests pass.
- [ ] Startup, memory, handles, threads, CPU, and network budgets pass.
- [ ] Paid and excluded Free builds are proven free of promotion UI and routes.
- [ ] Privacy and product text accurately disclose the first-party Store link.
- [ ] A promotion-disabled rollback build is ready.

## Research reference

- [Microsoft Store Product ID links and Web Installer guidance](https://learn.microsoft.com/en-us/windows/apps/distribute-through-store/how-to-use-store-web-installer-for-distribution)
- [Microsoft Store URI scheme](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-store-app)

Re-check the Store URI guidance and the live Paid Product ID immediately before
implementation and release.

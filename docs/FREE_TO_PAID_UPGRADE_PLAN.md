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

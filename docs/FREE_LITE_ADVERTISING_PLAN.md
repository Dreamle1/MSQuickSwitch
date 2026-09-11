# Proposed Free Lite advertising experiment

Status: **planning only**. Advertising is not implemented or enabled by this
document. No production advertising SDK, provider package, placement ID,
network permission, consent flow, or ad-enabled release is added by this plan.

Research reviewed: September 10, 2026. Provider documentation, terms, and Store
policies must be re-checked before any implementation or release decision.

## Decision summary

The recommendation is to approve **Phase A only**: a disposable compatibility
and provider-policy spike. Liftoff Monetize/Vungle is a candidate to evaluate,
not an approved production dependency.

Three facts make a provider commitment premature:

1. Liftoff's current Windows SDK documentation lists Win32, WinUI 3, and
   WinForms, but not WPF. WinQuickSwitch therefore needs a WPF/WinForms or HWND
   hosting proof before the provider can be selected.
2. The current Windows integration guide documents loading and playing an ad,
   but does not document an embedded banner or inline placement. WinQuickSwitch
   will not use interstitial, fullscreen, rewarded, or startup ads, so Liftoff
   must confirm and demonstrate a compliant embedded placement.
3. Liftoff's published terms allow the SDK to collect device make/model and
   device identifiers, among other app and performance data. WinQuickSwitch's
   proposed privacy boundary forbids sending hardware identifiers. Liftoff is a
   no-go unless a documented SDK configuration or written provider commitment
   can enforce that boundary and network inspection supports it.

If any of these conditions cannot be met, evaluate another Windows desktop ad
provider against the same requirements or abandon the experiment. Do not relax
the product's privacy, UX, or performance gates simply to accommodate a
provider.

Google AdSense is not an alternative for the desktop application. Google says
AdSense ads may not be integrated into software applications, including
desktop applications. A WebView does not turn a desktop app into an eligible
AdSense website.

## 1. Feasibility and experiment scope

The first experiment is intentionally narrow:

| Build | Advertising contract during experiment |
| --- | --- |
| Free Lite, explicit ad-enabled experiment build | Eligible only after all go/no-go gates pass |
| Free Lite, ordinary/ad-disabled build | No advertising; null service only |
| Free Portable | Ad-free |
| Free Microsoft Store packages | Ad-free |
| Paid Lite, Portable, and Store packages | Completely ad-free |

The initial candidate is Liftoff Monetize/Vungle's Windows SDK. The Phase A
spike must answer all of the following before provider approval:

- Can its WinForms SDK be hosted safely inside the existing WPF main window,
  or can its Win32 integration target a child HWND owned by WPF?
- Does the provider actually support a persistent embedded banner or inline
  placement on Windows? A load/play-only fullscreen experience is unacceptable.
- Can the placement resize, theme, suspend, resume, and dispose without focus,
  z-order, DPI, accessibility, or shutdown defects?
- Can WebView2 absence be handled by disabling ads, without an automatic
  download, installer launch, elevation prompt, or startup failure?
- Can hardware identifiers and all WinQuickSwitch feature state be excluded
  from provider requests and stored provider state?
- Are Liftoff's current publisher terms, Microsoft Store policies, supported
  regions, content controls, and test-ad rules acceptable?

The spike belongs in an isolated development project or an explicitly
non-production build configuration. It must not add the SDK to ordinary Free,
Paid, Portable, or Store dependency graphs.

## 2. Architecture

### Product-owned boundary

Introduce a provider-neutral `IAdvertisingService` only in Phase B. The
interface should expose product concepts rather than Liftoff types, for example:

- asynchronous initialize with cancellation and a bounded timeout;
- availability/state changes (`Disabled`, `Initializing`, `Ready`,
  `Unavailable`, `Suspended`, `Failed`);
- create/attach one main-window sponsored placement;
- suspend/resume based on surface visibility;
- explicit teardown/disposal;
- privacy/consent configuration applied before initialization.

`NullAdvertisingService` is the default for Paid and every non-ad build. It
does no work, opens no sockets, starts no child process, and returns
`Disabled`. A deterministic `FakeAdvertisingService` is used by tests.

The provider adapter, if eventually approved, is isolated under:

`Platform/Windows/Advertising`

No shared view model, coordinator state, feature inventory, profile model, or
widget contract may reference provider SDK types.

### Build-time gating

Use build properties that describe edition, distribution, and advertising
independently. A provider build is valid only when all conditions are true:

`Edition=Free` + `Distribution=Lite` + `AdvertisingProvider=Liftoff`

All other combinations compile only `NullAdvertisingService`. Validation must
fail the build if a provider is requested for Paid, Portable, or Store output.
Do not rely on a runtime `if` statement to protect Paid binaries.

Provider package references, native DLLs, WebView2 assets, placement
configuration, initialization code, and provider names must be absent from
Paid and ad-disabled outputs. Ads must never be initialized in Paid binaries.

Provider initialization runs out of the core startup path, cannot block the UI
thread, uses a bounded timeout, and fails closed to an ad-free UI. Core display,
audio, device, option, tray, shortcut, and profile behavior never depends on
advertising state.

## 3. UI contract

The only proposed placement is one clearly labeled **Sponsored** region in the
normal main window. It must be visually distinct from product navigation and
controls, keyboard reachable without becoming a focus trap, and described
correctly to accessibility tools.

Advertising is prohibited in:

- the overlay workspace;
- workspace cards and floating widgets;
- Profiles and all profile workflows;
- tray menus and notifications;
- startup, shutdown, error, confirmation, or consent dialogs.

Interstitial, fullscreen, rewarded, auto-playing video/audio, startup,
notification, and deceptive native-lookalike ads are prohibited. The provider
must not cover, shift, imitate, or intercept WinQuickSwitch controls.

When an ad is unavailable, offline, blocked, timed out, unconsented, or cannot
be hosted, remove the complete Sponsored region from layout. Do not leave a
blank frame, spinner, error panel, retry loop, or reserved gap. Availability
changes may update the ad region but must not reconstruct feature views or
change overlay/widget geometry.

## 4. Lifecycle and performance budgets

Initialize only after the normal main window has rendered and is usable. Do
not initialize for hidden startup, overlay-only startup, global-hotkey actions,
floating-widget launches, or a resident process that has not shown the main
window.

When the main window hides, immediately stop refresh and playback activity.
After a short disposal grace period, tear down the placement and provider-owned
WebView/child processes unless the spike proves a lower-cost suspension that
generates no requests or CPU activity. Reopening may initialize again without
affecting core state.

WebView2 is optional. Detect it before provider initialization. If missing,
record only a non-sensitive local diagnostic and use the null/unavailable path.
WinQuickSwitch must not download, install, repair, or prompt to install
WebView2. If Liftoff requires automatic runtime installation as a contractual
condition, Liftoff fails the provider gate.

Measure the ad-enabled Free Lite build against an otherwise identical
ad-disabled Free Lite build on the existing Sandbox performance workload. The
initial budgets are:

| Area | Required budget |
| --- | --- |
| Core UI ready | Existing 2,500 ms gate still passes; median regression no more than 100 ms and p95 regression no more than 150 ms |
| Visible ad steady-state memory | Combined WinQuickSwitch and provider/WebView2 private memory no more than 100 MiB above ad-disabled baseline |
| Hidden retained memory | Within 15 MiB of ad-disabled baseline after 30 seconds, with no provider-owned child process retained unless explicitly justified |
| Visible idle CPU | Five-minute combined average no more than 0.5% of one logical CPU, with no sustained refresh loop |
| Hidden idle CPU | Ten-minute combined average no more than 0.1% of one logical CPU after the suspension grace period |
| Hidden network | Zero provider requests after a 10-second suspension grace period |

Budgets are release gates, not targets to loosen after integration. Count the
application and all provider/WebView2 child processes. Repeat measurements at
least 20 times under the same host and power conditions; report median, p95,
maximum, retained deltas, handles, threads, CPU time, and child-process count.

## 5. Privacy and consent

The current privacy policy states that WinQuickSwitch has no advertising,
tracking, or publisher-operated data transmission. An ad-enabled build cannot
ship under that wording. Before Phase D, publish and surface a revised policy
that identifies the provider and explains:

- every data field the provider or its partners receive automatically;
- purposes, recipients, retention, storage, cookies/local state, and regions;
- personalized versus contextual advertising behavior;
- advertising-ID use and how Windows advertising-ID settings are respected;
- consent, withdrawal, opt-out, deletion/contact, and age requirements;
- when network activity starts and stops.

The app must never send these WinQuickSwitch values to an advertising provider,
whether as targeting, context, diagnostics, custom parameters, or log data:

- device names, endpoint names, or device inventory details;
- profile names, profile contents, favorites, or shortcuts;
- application audio-session names, routing, volumes, or mute state;
- monitor topology or other WinQuickSwitch settings;
- hardware identifiers.

Do not expose coordinator state or feature models to the advertising service.
Do not add custom analytics or publisher telemetry for the experiment. Provider
dashboard aggregates may be used only after their data sources are documented.

Define regional consent and interest-based-ad requirements with qualified legal
review before implementation. Default to contextual/non-personalized ads until
the consent and advertising-ID rules are settled. Consent must be captured or
communicated before provider initialization when required, remain revocable,
and never be bundled with core app use.

Paid remains advertising-network-free. It must not contain the SDK, initialize
an advertising component, contact an advertising endpoint, or share data with
an advertising provider.

## 6. Proposed product-contract change

The current acceptance criterion is:

> No network request occurs during normal use.

Keep that criterion unchanged while advertising remains a proposal. If Phase D
is approved, replace it with:

> Paid, Free Portable, Microsoft Store, and ad-disabled builds make no network
> requests during normal use. An explicitly ad-enabled Free Lite build may
> contact only the disclosed advertising provider endpoints while its normal
> main window is visible and advertising is permitted. Advertising traffic is
> isolated from WinQuickSwitch feature state, stops while hidden, and never
> blocks core functionality.

The exception is build-specific and provider-specific. It does not authorize
general telemetry, update checks, accounts, cloud sync, remote configuration,
or network access in other editions or distributions.

## 7. Testing and evidence

### Deterministic automated tests

- Use `FakeAdvertisingService` for ready, unavailable, offline, timeout,
  provider-error, consent-denied, suspend/resume, and disposal paths.
- Prove the core UI reaches ready before fake-provider initialization begins.
- Prove provider failure never changes or disables core commands.
- Prove the Sponsored region is labeled and disappears without a layout gap
  when unavailable.
- Prove no ad surface exists in overlay, widgets, Profiles, tray, startup, or
  notifications.
- Prove hiding the main window suspends and tears down advertising within the
  defined grace periods.

### Edition and artifact gates

- Compile a matrix covering Paid/Free, Lite/Portable/Store, and provider
  enabled/disabled combinations.
- Fail invalid provider-enabled combinations at build time.
- Inspect resolved packages, assemblies, native assets, symbols, strings, and
  startup wiring to prove Paid and ad-disabled artifacts cannot instantiate or
  load a provider.
- Run binaries behind a deny-all proxy/firewall and prove Paid, Portable,
  Store, and ad-disabled builds produce zero advertising traffic.

### UI, failure, and network tests

- Extend hidden Windows Sandbox UI checks for dark/light/high-contrast themes,
  keyboard and screen-reader naming, 100/150/200% DPI, narrow windows, offline
  behavior, provider failure, and clean disappearance.
- Use provider test mode and test placement IDs only. Automated tests must not
  request or click production ads or generate billable impressions.
- Capture DNS and connection metadata for the application and all provider
  child processes. Fail on any domain outside a reviewed allowlist.
- Review provider documentation and, where lawful and permitted, a controlled
  development capture to verify request fields. Encrypted traffic must not be
  described as payload-verified unless it was actually inspected.
- Scan local files, WebView storage, registry changes, and logs for prohibited
  WinQuickSwitch state and identifiers.
- Run offline, DNS-failure, TLS-failure, blocked-content, missing-WebView2,
  slow-provider, malformed-response, suspend/resume, and shutdown scenarios.

### Performance tests

Extend the existing E2E performance report with an ad-enabled/ad-disabled Free
Lite comparison. Include core startup, ad-ready latency separately, memory for
the full process tree, idle CPU, hidden network quiescence, repeated show/hide,
provider failure, and 20-cycle retained-resource trends. Paid performance must
remain unchanged and still pass its current gates.

## 8. Rollout phases

### Phase A - provider/WPF compatibility spike

- Keep the SDK outside production project references.
- Prove WPF/WinForms or child-HWND hosting, embedded placement availability,
  DPI/theme/accessibility behavior, WebView2 detection, teardown, and test mode.
- Obtain provider answers for supported format, data minimization, endpoints,
  consent, Store use, licensing, and ongoing Windows support.
- Produce a short spike report with screenshots, process/network observations,
  dependency inventory, and a provider go/no-go recommendation.

Exit: all three major risks—WPF hosting, embedded format, and prohibited data—
have evidence-backed answers. Otherwise stop or evaluate another provider.

### Phase B - fake-provider architecture and UI placeholder

- Add `IAdvertisingService`, null and fake implementations, build-matrix
  validation, and the main-window Sponsored host.
- Add no production SDK or external advertising traffic.
- Complete deterministic lifecycle, UI, edition, and performance tests.

Exit: the architecture fails closed and every non-ad build is proven unchanged.

### Phase C - real provider behind a development build flag

- Add the isolated provider adapter only to an explicit development
  configuration.
- Use test ads, reviewed consent settings, domain allowlisting, process-tree
  resource accounting, and missing-WebView2/offline failure tests.
- Update draft privacy and Store disclosures, but do not distribute the build
  to ordinary users.

Exit: all privacy, policy, network, artifact, UI, and performance gates pass.

### Phase D - limited Free Lite rollout

- Update and publish the privacy policy and Free Lite disclosure before users
  receive the build.
- Release only the Free Lite ad-enabled artifact to a small controlled channel;
  do not use an app-owned analytics or remote-config service.
- Monitor provider-reported aggregate operations, crashes, Store feedback, and
  resource/support regressions. Keep an ad-disabled Free Lite rollback build.
- Paid, Free Portable, and Store outputs remain ad-free.

Exit: expand only after a defined observation period with no gate regression.
Any privacy, policy, crash, startup, hidden-activity, or UX regression triggers
rollback to the ad-disabled build.

## 9. Go/no-go checklist

Advertising remains **no-go** until every item is complete:

- [ ] WPF hosting is stable across supported Windows versions and DPI modes.
- [ ] Liftoff confirms a non-fullscreen embedded Windows placement compatible
      with the one Sponsored-region UI contract.
- [ ] Store and provider terms have been reviewed for the planned distribution.
- [ ] The privacy policy and in-app/Store disclosures are updated before release.
- [ ] Provider data behavior, recipients, storage, endpoints, and consent are
      documented.
- [ ] Network and storage evidence shows no sensitive WinQuickSwitch state or
      hardware identifier is transmitted.
- [ ] Consent, advertising-ID, interest-based-ad opt-out, age, and regional
      requirements are implemented and reviewed.
- [ ] Startup, memory, idle CPU, hidden network, and retained-resource budgets
      pass under the complete process tree.
- [ ] Offline and provider-failure experiences are functionally and visually
      unchanged from ad-disabled Free Lite.
- [ ] Paid artifacts are proven free of provider packages, native assets,
      initialization paths, advertising strings/configuration, and advertising
      network traffic.
- [ ] Free Portable and Store artifacts pass the same ad-free proof.

## Research references

- [Liftoff/Vungle Windows SDK integration](https://support.vungle.com/hc/en-us/articles/45026606409371-Integrate-Vungle-SDK-for-Win32-WinUI-3-or-WinForms)
- [Liftoff/Vungle Windows SDK section](https://support.vungle.com/hc/en-us/sections/115000243471-Windows-Universal-Windows-Platform-UWP)
- [Liftoff publisher terms and data clauses](https://publisher.vungle.com/LICENSE.html)
- [Vungle Windows bidding data flow](https://support.vungle.com/hc/en-us/articles/32582711398555-In-App-Bidding-With-Vungle-SDK-for-Windows)
- [Google AdSense program policies](https://support.google.com/adsense/answer/48182?hl=en)
- [Microsoft Store policies, advertising section](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies#1010-advertising-conduct-and-content)
- [Microsoft WebView2 Runtime distribution and detection](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)

Provider documentation and terms can change. Re-check every linked source and
record the exact SDK version and review date before Phase C and again before
Phase D.

# Microsoft Store licensing guide

This guide describes the intended commercial licensing model for
WinQuickSwitch. It is operational guidance, not legal advice.

## Recommended release model

Publish WinQuickSwitch as a paid, one-time-purchase MSIX application through
Microsoft Store.

This divides responsibility cleanly:

- Microsoft Store handles the customer's purchase entitlement, account-linked
  acquisition, package hosting, Store signing, installation, and updates.
- The WinQuickSwitch EULA defines what the customer may do with the app and
  prohibits unauthorized sharing, redistribution, resale, sublicensing, and
  license bypass.
- The proprietary `LICENSE.txt` protects the source code and repository. It is
  not a substitute for the customer-facing EULA.

A purchase is a license to use the app. It does not transfer ownership of the
app or source code to the customer.

## Before submission

1. Choose the legal publisher name that will own the app and receive Store
   payouts. Use the same identity consistently in Partner Center, the Store
   listing, the EULA, support information, and privacy policy.
2. Replace `[PUBLISHER LEGAL NAME]` in `LICENSE.txt` and `docs/EULA.md`.
3. Replace `[SUPPORT EMAIL OR SUPPORT URL]` in `docs/EULA.md`.
4. Have a qualified lawyer review the EULA, especially the governing-law,
   warranty, liability, refund, and consumer-rights sections for target markets.
5. Publish the final EULA at a stable HTTPS URL and make it available from the
   Store listing or inside the packaged app.
6. Publish a privacy policy at a stable HTTPS URL. Even if the current release
   does not transmit personal data, the policy should plainly describe local
   settings and state whether telemetry or network collection occurs.
7. Inventory third-party libraries, icons, fonts, and other assets and retain
   their required notices and licenses.

## Store submission flow

1. Open a Windows developer account in Partner Center.
2. Reserve the WinQuickSwitch product name.
3. Create an **MSIX or PWA app** submission and package the WPF application as
   MSIX. The current `artifacts/win-x64-lite/WinQuickSwitch.exe` is an
   unpackaged direct-download build and is not the planned Store package.
4. In **Pricing and availability**, choose a one-time paid price, markets,
   visibility, and whether to offer a trial.
5. Upload the MSIX package and complete the Store listing, age rating,
   screenshots, product declarations, support details, and privacy information.
6. Include the final license terms where Partner Center or the listing permits,
   and ensure the app's About page points customers to the same final terms.
7. Add clear certification notes explaining the app's display, audio, wireless,
   startup, and taskbar actions so testers can exercise it safely.
8. Submit for certification and address any report before publication.

Microsoft recommends MSIX for typical Windows apps because it can provide
Store hosting, signing, commerce, controlled rollout, and update handling.

## How purchase enforcement works

For a simple paid app, the Store normally prevents users without an entitlement
from acquiring the Store package. The customer signs in to Microsoft Store,
buys the app, and receives an account-linked entitlement. Store installation
limits and device association are controlled by Microsoft terms, so the EULA
does not impose a conflicting one-device rule.

The EULA is a separate legal layer. It gives the buyer limited use rights and
provides contractual remedies against redistribution or license bypass. A text
warning does not technically prevent copying by itself; Store packaging and
entitlement checks provide the technical layer.

If WinQuickSwitch later offers a free trial, subscription, durable add-on, or
feature upgrade, use `Windows.Services.Store.StoreContext` to read the active
app or add-on license before enabling paid features. A packaged desktop app
must initialize Store context for its desktop window as required by the Windows
Store API. Entitlement checks should fail safely and avoid locking out a valid
buyer during a temporary network or Store-service problem.

Do not build a custom license-key server for the first paid Store release
unless the product needs accounts or cross-store sales. It adds account,
privacy, support, security, outage, and recovery obligations that the Store can
avoid for this phase.

## Acceptance options

There are two practical ways to present the EULA:

- **Store/listing notice:** provide the EULA before acquisition and reference it
  in the About page. This has the least app friction.
- **First-run acceptance:** show the complete terms or a clear link and require
  the user to select **I agree** before first use. Save the accepted EULA version
  and date locally. This creates stronger evidence of assent but adds a startup
  step and requires a path to decline and close the app.

For a commercial release, use a versioned EULA and record the version, not
personal identity. Present a revised agreement again only when changes are
material. Legal counsel should confirm the acceptance design for sales markets.

## Current repository status

Completed:

- Proprietary source and repository notice.
- Customer-facing Store EULA draft.
- Commercial licensed-use wording in the About page.
- Verified Release build, automated checks, and Lite EXE publication.

Still required for Store release:

- Final publisher identity and support contact.
- Legal review and hosted final EULA.
- Privacy policy.
- MSIX packaging, Store identity, package assets, and Store certification test.
- A decision on Store-listing notice versus first-run acceptance.

## Official references

- [Get started with Microsoft Store](https://learn.microsoft.com/en-us/windows/apps/publish/get-started)
- [Upload MSIX app packages](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/upload-app-packages)
- [Set app pricing and availability](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/price-and-availability)
- [Get license info for apps and add-ons](https://learn.microsoft.com/en-us/windows/uwp/monetize/get-license-info-for-apps-and-add-ons)
- [Microsoft Store policies](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies-and-code-of-conduct)

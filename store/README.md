# WinQuickSwitch Microsoft Store package

This directory contains the checked-in inputs for the x64 MSIX Store packages.
Generated packages stay under the ignored
`artifacts/store-msix/<edition>/v<version>` directories.

## Store listings and artwork

The complete English (Canada) Partner Center materials are checked in under
`store/Listing/en-CA`:

- `STORE_LISTING_PAID.md` and `STORE_LISTING_FREE.md`
- Paid and Free privacy policies plus the EULA
- Sanitized Desktop screenshots
- Exact 2160 x 2160 Box art and 1440 x 2160 Poster art

Keep the public product names and package identities in these listing files
aligned with Partner Center before uploading a new package.

## Product identity

- Package name: `Dreamle.WinQuickSwitch`
- Publisher: `CN=E047B488-2EDF-444A-8C22-4FF1BD29B2B8`
- Publisher display name: `Dreamle`
- Expected package family name: `Dreamle.WinQuickSwitch_sth8w7gs4yt8p`
- Application ID: `WinQuickSwitch`

The configured Free product uses these reserved values:

- Package name: `Dreamle.WinQuickSwitch-Free`
- Publisher: `CN=E047B488-2EDF-444A-8C22-4FF1BD29B2B8`
- Publisher display name: `Dreamle`
- Reserved display name: `WinQuickSwitch - Free`
- Expected package family name: `Dreamle.WinQuickSwitch-Free_sth8w7gs4yt8p`
- Application ID: `WinQuickSwitch`

These values must remain identical to the Partner Center product identity.
Changing the package name or publisher creates a different Windows package
identity and will break Store association and updates.

The checked-in manifest carries the Paid identity. The build script selects the
configured identity, publisher, and display name for the requested edition.
It rejects identity overrides that do not match those reserved values.

## Build

From the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Build-StorePackage.ps1 -Edition Paid
```

The builder reads the default version from `Directory.Build.props` and accepts
an explicit `-Version` override when needed. The configured Free product can
be built with its reserved identity and display name:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Build-StorePackage.ps1 -Edition Free
```

The build:

1. Generates deterministic placeholder package logos.
2. Publishes the selected Free or Paid edition as a self-contained,
   non-single-file x64 Release build.
3. Selects the matching identity, display name, and description, then inserts
   the requested four-part package version into `AppxManifest.xml`.
4. Creates an unsigned `.msix` with the installed Windows SDK `MakeAppx.exe`.
5. Wraps the package in a `.msixupload` file for Partner Center.

Expected outputs:

- `artifacts/store-msix/paid/v<central-version>/WinQuickSwitch_<central-version>_x64.msix`
- `artifacts/store-msix/paid/v<central-version>/WinQuickSwitch_<central-version>_x64.msixupload`

Free artifacts use `artifacts/store-msix/free/v<version>` and the
`WinQuickSwitch-Free_<version>_x64` filename stem. The builder refuses a Free
build whose identity or publisher does not match the configured reserved
product.

When replacing a rejected Partner Center package, first choose **Delete** on
the invalid uploaded package, then select the newly versioned `.msixupload`
file. **Retry** only validates the bytes already uploaded and does not select
the replacement file.

Microsoft Store signs an accepted MSIX submission. The unsigned local package
is intended for structural validation and Partner Center upload; it is not the
same as a normally installable production package.

## Current limitations before submission

- Replace the generated `WQ` package logos with final branded artwork and
  create the separate Store-listing artwork required by Partner Center.
- The existing **Start WinQuickSwitch when I sign in** setting still uses the
  unpackaged per-user `Run` registration. Add a package-aware startup task
  before Store certification so updates do not invalidate the registered path.
- Complete a signed local install/uninstall test or a private Store-flight test.
- Run the Windows App Certification Kit and fix any reported package issues.
- Host the final EULA and privacy policy at stable HTTPS URLs.

## Versioning

MSIX versions have four numeric parts, but Microsoft Store app submissions
require the fourth (revision) component to be zero. Increment the third
component for replacement packages and updates; for example, follow `1.0.0.0`
with `1.0.1.0`, not `1.0.0.1`. Every Store update must use a version higher
than the previously submitted package. The builder passes the requested
version into the executable metadata as well as the manifest, so the package,
file properties, and output folder remain aligned. Keep release tags,
assembly versions, documentation, and the MSIX version aligned before public
release.

## Why this is manual

The current machine has the Windows SDK and `MakeAppx.exe`, but does not have
the Visual Studio MSIX Packaging Tools workload. The script follows the manual
Microsoft packaging flow and does not require installing an additional Visual
Studio workload.

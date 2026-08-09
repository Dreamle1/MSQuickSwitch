# WinQuickSwitch Microsoft Store package

This directory contains the checked-in inputs for the x64 MSIX Store package.
Generated packages stay under the ignored `artifacts/store-msix` directory.

## Product identity

- Package name: `Dreamle.WinQuickSwitch`
- Publisher: `CN=E047B488-2EDF-444A-8C22-4FF1BD29B2B8`
- Publisher display name: `Dreamle`
- Expected package family name: `Dreamle.WinQuickSwitch_sth8w7gs4yt8p`
- Application ID: `WinQuickSwitch`

These values must remain identical to the Partner Center product identity.
Changing the package name or publisher creates a different Windows package
identity and will break Store association and updates.

## Build

From the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Build-StorePackage.ps1 -Version 1.0.0.0
```

The build:

1. Generates deterministic placeholder package logos.
2. Publishes a self-contained, non-single-file x64 Release build.
3. Inserts the requested four-part package version into `AppxManifest.xml`.
4. Creates an unsigned `.msix` with the installed Windows SDK `MakeAppx.exe`.
5. Wraps the package in a `.msixupload` file for Partner Center.

Expected outputs:

- `artifacts/store-msix/WinQuickSwitch_1.0.0.0_x64.msix`
- `artifacts/store-msix/WinQuickSwitch_StoreUpload.msixupload`

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

MSIX versions have four numeric parts. Every Store update must use a version
higher than the previously submitted package. Keep release tags, assembly
versions, documentation, and the MSIX version aligned before public release.

## Why this is manual

The current machine has the Windows SDK and `MakeAppx.exe`, but does not have
the Visual Studio MSIX Packaging Tools workload. The script follows the manual
Microsoft packaging flow and does not require installing an additional Visual
Studio workload.

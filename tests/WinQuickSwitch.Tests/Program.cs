using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WinQuickSwitch.Features.Audio;
using WinQuickSwitch.Features.Devices;
using WinQuickSwitch.Features.Display;
using WinQuickSwitch.Features.Legal;
using WinQuickSwitch.Features.Overlay;
using WinQuickSwitch.Features.Profiles;
using WinQuickSwitch.Features.Product;
using WinQuickSwitch.Features.SharedUi;
using WinQuickSwitch.Features.Taskbar;
using WinQuickSwitch.Features.Widget;
using WinQuickSwitch.Platform.Windows;
using WinQuickSwitch.Platform.Windows.Audio;
using WinQuickSwitch.Platform.Windows.Devices;
using WinQuickSwitch.Platform.Windows.Display;
using WinQuickSwitch.Platform.Windows.Overlay;

namespace WinQuickSwitch.Tests;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        List<(string Name, Func<Task> Run)> tests =
        [
            ("PC screen only uses the internal topology flag", () => MapsMode(DisplayMode.PcScreenOnly, WindowsDisplayModeService.SdcTopologyInternal)),
            ("Duplicate uses the clone topology flag", () => MapsMode(DisplayMode.Duplicate, WindowsDisplayModeService.SdcTopologyClone)),
            ("Extend uses the Windows projection command", ExtendUsesWindowsProjectionCommand),
            ("Leaving Extend captures its exact layout", LeavingExtendCapturesLayout),
            ("Captured Extend layout bypasses the projection command", CapturedExtendLayoutBypassesProjectionCommand),
            ("Failed captured layout falls back to projection", FailedCapturedLayoutFallsBackToProjection),
            ("Second screen only uses the external topology flag", () => MapsMode(DisplayMode.SecondScreenOnly, WindowsDisplayModeService.SdcTopologyExternal)),
            ("Zero SetDisplayConfig result succeeds", ZeroDisplayConfigResultSucceeds),
            ("Non-zero SetDisplayConfig result fails", NonZeroDisplayConfigResultFails),
            ("Native display failure becomes a result", NativeDisplayFailureBecomesResult),
            ("Cancellation is preserved", CancellationIsPreserved),
            ("Unknown modes are rejected before the native call", UnknownModeIsRejected),
            ("Direct display call runs away from the UI thread", DisplayCallRunsOffCallingThread),
            ("Single internal display is classified as PC screen only", SingleInternalDisplayIsClassified),
            ("Single external display is classified as second screen only", SingleExternalDisplayIsClassified),
            ("Shared display source is classified as duplicate", SharedSourceIsClassifiedAsDuplicate),
            ("Distinct display sources are classified as extend", DistinctSourcesAreClassifiedAsExtend),
            ("Windows external topology overrides extended paths", WindowsExternalTopologyOverridesExtendedPaths),
            ("Windows topology IDs map to projection modes", WindowsTopologyIdsMapToProjectionModes),
            ("Mixed display sources are not mislabeled", MixedSourcesAreNotMislabeled),
            ("Inactive available display enables multi-display choices", AvailableInactiveDisplayEnablesChoices),
            ("No active display produces an unreliable snapshot", NoActiveDisplayIsUnreliable),
            ("Paid product configuration enables Profiles", PaidProductConfigurationEnablesProfiles),
            ("Free product configuration disables Profiles", FreeProductConfigurationDisablesProfiles),
            ("Overlay feature catalog keeps main and overlay parity", OverlayFeatureCatalogKeepsParity),
            ("Overlay launcher omits the redundant Home entry", OverlayLauncherOmitsHome),
            ("Overlay keyboard numbering follows edition boundaries", OverlayKeyboardNumberingFollowsEditionBoundaries),
            ("Overlay card presets migrate to content-sized defaults", OverlayCardPresetsMigrate),
            ("Free overlay composition excludes Profiles", FreeOverlayCompositionExcludesProfiles),
            ("Shared view factory covers every enabled feature surface", SharedViewFactoryCoversEnabledFeatures),
            ("Free shared view factory excludes Profiles", FreeSharedViewFactoryExcludesProfiles),
            ("Shared feature views reject stale coordinator revisions", SharedFeatureViewsRejectStaleRevisions),
            ("Shared feature refresh failures stay recoverable", SharedFeatureRefreshFailuresStayRecoverable),
            ("Shared coordinator state ignores legacy WPF controls", SharedCoordinatorStateIgnoresLegacyControls),
            ("Legacy workspace UI builders are removed", LegacyWorkspaceBuildersAreRemoved),
            ("Latest refresh gate cancels superseded work", LatestRefreshGateCancelsSupersededWork),
            ("Disposing a stale refresh keeps the newest lease active", DisposingStaleRefreshKeepsNewestLease),
            ("Shared WPF views expose accessibility contracts", OptionsSharedViewRendersShortcutTemplate),
            ("Shortcut search preserves categories, rows and local expansion", ShortcutSearchPreservesSections),
            ("Stable audio shortcut settings preserve legacy bindings", AudioCommandSettingsRoundTrip),
            ("Shared audio shortcut rows expose typed capture and step controls", AudioCommandEditorUsesStableKeys),
            ("Audio state exposes app volume sessions", AudioStateExposesAppVolumeSessions),
            ("Audio volume editing preserves the live slider row", AudioVolumeEditingPreservesLiveSliderRow),
            ("Overlay command metadata keeps smart-close rules", OverlayCommandMetadataKeepsSmartCloseRules),
            ("Typed command catalog maps every persisted hotkey action", TypedCommandCatalogMapsPersistedActions),
            ("Typed command catalog enforces profile edition boundaries", TypedCommandCatalogEnforcesProfileEdition),
            ("Expanded fixed shortcuts map to typed commands", ExpandedFixedShortcutsMapTypedCommands),
            ("Overlay launcher commands follow edition boundaries", OverlayLauncherCommandsFollowEdition),
            ("Overlay shell routes launcher and footer actions as typed commands", OverlayShellRoutesTypedCommands),
            ("Overlay geometry actions remain local presentation behavior", OverlayGeometryActionsRemainLocal),
            ("Global hotkeys route through the typed command catalog", GlobalHotkeysRouteThroughTypedCatalog),
            ("Panel navigation follows the selected edition", PanelNavigationFollowsEdition),
            ("Widget settings use edition-specific default paths", WidgetSettingsUseEditionSpecificDefaultPaths),
            ("Free profile feature does not load or save profile data", FreeProfileFeatureDoesNotLoadOrSaveData),
            ("Paid profile feature loads and exposes profile shortcuts", PaidProfileFeatureLoadsAndExposesShortcuts),
            ("Second screen only uses a safe bridge before Extend", () => TransitionPlanMatches(DisplayMode.SecondScreenOnly, DisplayMode.Extend, false, DisplayMode.PcScreenOnly, DisplayMode.Extend)),
            ("Duplicate uses a safe bridge before Extend", () => TransitionPlanMatches(DisplayMode.Duplicate, DisplayMode.Extend, false, DisplayMode.PcScreenOnly, DisplayMode.Extend)),
            ("Captured layout restores Extend directly", () => TransitionPlanMatches(DisplayMode.SecondScreenOnly, DisplayMode.Extend, true, DisplayMode.Extend)),
            ("PC screen only transitions directly to Extend", () => TransitionPlanMatches(DisplayMode.PcScreenOnly, DisplayMode.Extend, false, DisplayMode.Extend)),
            ("Non-Extend transitions remain direct", () => TransitionPlanMatches(DisplayMode.Extend, DisplayMode.Duplicate, false, DisplayMode.Duplicate)),
            ("Display transition returns an already-settled mode immediately", DisplayTransitionReturnsImmediately),
            ("Display transition waits for Windows topology to settle", DisplayTransitionWaitsForExpectedMode),
            ("Display transition waits for a stable topology snapshot", DisplayTransitionWaitsForStableTopology),
            ("Partial extend topology remains unsettled", PartialExtendTopologyRemainsUnsettled),
            ("Display transition settle checks are bounded", DisplayTransitionChecksAreBounded),
            ("Accepted display request must reach the requested topology", AcceptedDisplayRequestMustSettle),
            ("Display transition retries one external override", DisplayTransitionRetriesExternalOverride),
            ("Failed preparatory display mode stops the transition", FailedDisplayBridgeStopsTransition),
            ("Audio endpoint labels use the friendly name", AudioEndpointLabelsUseFriendlyName),
            ("Audio endpoint roles have clear active descriptions", AudioEndpointRolesAreClear),
            ("Audio session volume is formatted and clamped", AudioSessionVolumeIsFormatted),
            ("Audio refresh notifications are debounced", AudioRefreshNotificationsAreDebounced),
            ("Disposed audio debounce cancels pending work", DisposedAudioDebounceCancelsWork),
            ("Session volume delegates the requested level", SessionVolumeDelegatesRequestedLevel),
            ("Invalid session volume is rejected", InvalidSessionVolumeIsRejected),
            ("Session mute delegates the requested state", SessionMuteDelegatesRequestedState),
            ("Endpoint volume controls validate and delegate", EndpointVolumeControlsValidateAndDelegate),
            ("General endpoint selection updates both default roles", GeneralEndpointSelectionUpdatesBothRoles),
            ("Communications endpoint selection updates only calls", CommunicationsEndpointSelectionUpdatesCalls),
            ("Both endpoint selection updates all roles", BothEndpointSelectionUpdatesAllRoles),
            ("Unsupported endpoint selection preserves Settings fallback", UnsupportedEndpointSelectionPreservesFallback),
            ("Volume mixer uses the exact Windows Settings URI", VolumeMixerUsesExactSettingsUri),
            ("Physical device interfaces are grouped by container", PhysicalDeviceInterfacesAreGrouped),
            ("Bluetooth takes precedence for mixed-interface devices", BluetoothTakesPrecedence),
            ("Bluetooth profile names collapse into one device", BluetoothProfilesCollapse),
            ("Generic device infrastructure is hidden", GenericDeviceInfrastructureIsHidden),
            ("Unrelated USB devices remain separate", UnrelatedUsbDevicesRemainSeparate),
            ("Device status labels are human readable", DeviceStatusLabelsAreHumanReadable),
            ("Device Settings shortcuts use exact Windows URIs", DeviceSettingsShortcutsUseExactUris),
            ("Taskbar state maps the Windows auto-hide flag", TaskbarStateMapsAutoHideFlag),
            ("Taskbar controls delegate the requested state", TaskbarControlsDelegateState),
            ("Taskbar Settings shortcuts use exact Windows URIs", TaskbarSettingsShortcutsUseExactUris),
            ("Taskbar API failures remain nonfatal", TaskbarApiFailuresRemainNonfatal),
            ("Wireless radio states remain independent", WirelessRadioStatesRemainIndependent),
            ("Wireless radio control delegates the desired state", WirelessRadioControlDelegatesState),
            ("Denied wireless radio control fails clearly", DeniedWirelessRadioControlFailsClearly),
            ("Wireless radio API failures remain nonfatal", WirelessRadioApiFailuresRemainNonfatal),
            ("Dark title bar uses the application palette", DarkTitleBarUsesAppPalette),
            ("Light title bar uses the application palette", LightTitleBarUsesAppPalette),
            ("High contrast palette follows Windows system colors", HighContrastPaletteUsesSystemColors),
            ("Embedded legal documents include publisher details", EmbeddedLegalDocumentsIncludePublisher),
            ("Legal documents render without Markdown markers", LegalDocumentsRenderPlainText),
            ("Widget shortcuts validate and format supported chords", WidgetShortcutsValidateAndFormat),
            ("Widget settings remove duplicate shortcuts", WidgetSettingsRemoveDuplicates),
            ("Display and favorite shortcuts map to distinct actions", DisplayAndFavoriteShortcutsMapToActions),
            ("Favorite outputs normalize invalid and duplicate devices", FavoriteOutputsNormalizeDevices),
            ("Favorite inputs normalize and map actions", FavoriteInputsNormalizeAndMapActions),
            ("Reset shortcuts preserves favorite output devices", ResetShortcutsPreservesFavorites),
            ("Widget settings persist without dependencies", WidgetSettingsPersist),
            ("Expanded fixed shortcuts persist and reset safely", ExpandedFixedShortcutsPersistAndReset),
            ("Older widget settings migrate with new shortcuts empty", OlderWidgetSettingsMigrate),
            ("Floating widget settings normalize safely", FloatingWidgetSettingsNormalize),
            ("Floating widget settings persist", FloatingWidgetSettingsPersist),
            ("Free widget settings strip persisted Profiles layouts", FreeWidgetSettingsStripProfilesLayouts),
            ("Overlay settings default safely and normalize bounds", OverlaySettingsDefaultAndNormalize),
            ("Overlay settings persist across JSON round trips", OverlaySettingsPersist),
            ("Overlay workspace defaults adapt to the work area", OverlayWorkspaceDefaultsAdaptToWorkArea),
            ("Overlay workspace placements snap and clamp safely", OverlayWorkspacePlacementsSnapAndClamp),
            ("Overlay bottom-right resize stops at edges without shifting", OverlayResizeStopsAtEdgesWithoutShifting),
            ("Overlay workspace content fit honors monitor caps", OverlayWorkspaceContentFitHonorsMonitorCaps),
            ("Overlay workspace default cards never overlap", OverlayWorkspaceDefaultCardsNeverOverlap),
            ("Rendered-size defaults pack without minimum-size overlap", OverlayFittedDefaultsPackWithoutOverlap),
            ("Overlay workspace finds a free slot for restored cards", OverlayWorkspaceFindsFreeSlot),
            ("Overlay workspace restores saved dimensions regardless of custom-size state", OverlayWorkspaceRestoresSavedDimensionsRegardlessOfCustomSize),
            ("Overlay workspace preserves intentional widget overlap", OverlayWorkspacePreservesIntentionalOverlap),
            ("Overlay workspace preserves custom bounds and removal state", OverlayWorkspacePreservesCustomBounds),
            ("Overlay workspace does not use a stale virtual-screen fallback", OverlayWorkspaceDoesNotUseStaleVirtualScreenFallback),
            ("Overlay workspace migrates generated v1 layouts", OverlayWorkspaceMigratesGeneratedV1Layout),
            ("Overlay workspace migrates legacy content-fit state", OverlayWorkspaceMigratesLegacyContentFitState),
            ("Overlay workspace settings normalize and round trip", OverlayWorkspaceSettingsNormalizeAndRoundTrip),
            ("Legacy sidebar width migrates to the overlay inspector", LegacySidebarWidthMigratesToInspector),
            ("Profile catalogs normalize duplicates and pinned profiles", ProfileCatalogNormalizes),
            ("Profile catalogs persist independently", ProfileCatalogPersists),
            ("Profile actions normalize unsupported values", ProfileActionsNormalize),
            ("Startup registration uses a quoted hidden-start command", StartupRegistrationUsesHiddenCommand),
            ("Startup registration recognizes only the current executable", StartupRegistrationRecognizesCurrentExecutable),
            ("Startup registration can be removed safely", StartupRegistrationCanBeRemoved),
            ("Startup registration does not remove another edition", StartupRegistrationDoesNotRemoveAnotherEdition),
            ("Startup registration failures remain nonfatal", StartupRegistrationFailuresRemainNonfatal),
            ("Global hotkeys register and resolve actions", GlobalHotkeysRegisterAndResolve),
            ("Global hotkeys register and resolve the overlay action", GlobalHotkeysRegisterAndResolveOverlay),
            ("Global hotkeys register and resolve profiles", GlobalHotkeysRegisterAndResolveProfiles),
            ("Global hotkey conflicts stay isolated", GlobalHotkeyConflictsStayIsolated),
            ("Dynamic profile hotkeys unregister on reapply and disposal", DynamicProfileHotkeysUnregister),
            ("Failed hotkey reapply retains the previous working set", FailedHotkeyReapplyRetainsPreviousBindings),
            ("Profile hotkeys report duplicate binding owners", ProfileHotkeysReportDuplicateOwners),
            ("Profile hotkey IDs remain stable across reorder", ProfileHotkeyIdsRemainStableAcrossReorder),
            ("Tray callbacks decode version 4 notifications", TrayCallbacksDecodeVersion4Notifications),
            ("Overlay monitor interop uses Unicode MONITORINFOEX", OverlayMonitorInteropUsesUnicodeMonitorInfo),
            ("Widget opens below and right of the pointer", WidgetOpensBelowPointer),
            ("Widget flips away from monitor edges", WidgetFlipsAtMonitorEdges),
            ("Widget stays inside negative-coordinate work areas", WidgetStaysInsideWorkArea),
            ("Growing widget clamps only when it crosses a work-area edge", GrowingWidgetClampsAtEdges),
        ];

        tests.AddRange(AudioQuickActionTests.GetTests());
        tests.AddRange(CommandHotkeyTests.GetTests());

        if (args.Contains("--integration", StringComparer.OrdinalIgnoreCase))
        {
            tests.Add(("Live display topology can be read", LiveDisplayTopologyCanBeRead));
            tests.Add(("Live Extend layout capture respects the current topology", LiveExtendLayoutCaptureRespectsCurrentTopology));
            tests.Add(("Live Core Audio inventory can be read", LiveAudioInventoryCanBeRead));
            tests.Add(("Live audio notification watcher starts and stops", LiveAudioWatcherStartsAndStops));
            tests.Add(("Live connected-device inventory can be read", LiveDeviceInventoryCanBeRead));
            tests.Add(("Live wireless radio states can be read", LiveWirelessRadioStatesCanBeRead));
            tests.Add(("Live taskbar state can be read", LiveTaskbarStateCanBeRead));
        }

        int failed = 0;

        foreach ((string name, Func<Task> run) in tests)
        {
            try
            {
                await run();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {name}");
                Console.Error.WriteLine($"     {exception.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{tests.Count - failed}/{tests.Count} tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static Task TrayCallbacksDecodeVersion4Notifications()
    {
        nint packedRightClick = (1 << 16) | 0x0205;

        Equal(
            0x0205,
            WindowsTrayIcon.DecodeNotificationCode(packedRightClick));
        return Task.CompletedTask;
    }

    private static Task OverlayMonitorInteropUsesUnicodeMonitorInfo()
    {
        Equal(104, WindowsOverlayMonitor.MonitorInfoStructureSize);
        return Task.CompletedTask;
    }

    private static Task EmbeddedLegalDocumentsIncludePublisher()
    {
        LegalDocument eula = LegalDocuments.Get(LegalDocumentKind.Eula);
        LegalDocument privacy =
            LegalDocuments.Get(LegalDocumentKind.PrivacyPolicy);

        True(
            eula.DisplayText.Contains("Andrew Chang", StringComparison.Ordinal),
            "The embedded EULA should identify the publisher.");
        True(
            privacy.DisplayText.Contains(
                "andrew.chang0@outlook.com",
                StringComparison.Ordinal),
            "The embedded privacy policy should include the support contact.");
        return Task.CompletedTask;
    }

    private static Task LegalDocumentsRenderPlainText()
    {
        string displayText = LegalDocuments.ToDisplayText(
            "# Heading\n\nUse **important** and `local` settings.");

        True(
            displayText.StartsWith("Heading", StringComparison.Ordinal),
            "Markdown headings should be readable in the legal window.");
        True(
            !displayText.Contains("**", StringComparison.Ordinal) &&
            !displayText.Contains('`'),
            "Markdown emphasis markers should be removed.");
        return Task.CompletedTask;
    }

    private static Task SingleInternalDisplayIsClassified()
    {
        DisplayTopologySnapshot snapshot = DisplayTopologyClassifier.Classify(
        [
            DisplayPath(1, 0, 10, DisplayOutputTechnology.DisplayPortEmbedded, true, true),
        ]);

        Equal(DisplayMode.PcScreenOnly, snapshot.CurrentMode);
        True(snapshot.IsReliable, "A reported active path should be reliable.");
        return Task.CompletedTask;
    }

    private static Task SingleExternalDisplayIsClassified()
    {
        DisplayTopologySnapshot snapshot = DisplayTopologyClassifier.Classify(
        [
            DisplayPath(1, 0, 10, DisplayOutputTechnology.Hdmi, true, true),
        ]);

        Equal(DisplayMode.SecondScreenOnly, snapshot.CurrentMode);
        return Task.CompletedTask;
    }

    private static Task SharedSourceIsClassifiedAsDuplicate()
    {
        DisplayTopologySnapshot snapshot = DisplayTopologyClassifier.Classify(
        [
            DisplayPath(1, 0, 10, DisplayOutputTechnology.DisplayPortEmbedded, true, true),
            DisplayPath(1, 0, 11, DisplayOutputTechnology.Hdmi, true, true),
        ]);

        Equal(DisplayMode.Duplicate, snapshot.CurrentMode);
        Equal(2, snapshot.ActiveDisplayCount);
        return Task.CompletedTask;
    }

    private static Task DistinctSourcesAreClassifiedAsExtend()
    {
        DisplayTopologySnapshot snapshot = DisplayTopologyClassifier.Classify(
        [
            DisplayPath(1, 0, 10, DisplayOutputTechnology.DisplayPortEmbedded, true, true),
            DisplayPath(1, 1, 11, DisplayOutputTechnology.Hdmi, true, true),
        ]);

        Equal(DisplayMode.Extend, snapshot.CurrentMode);
        return Task.CompletedTask;
    }

    private static Task AvailableInactiveDisplayEnablesChoices()
    {
        DisplayTopologySnapshot snapshot = DisplayTopologyClassifier.Classify(
        [
            DisplayPath(1, 0, 10, DisplayOutputTechnology.DisplayPortEmbedded, true, true),
            DisplayPath(1, 1, 11, DisplayOutputTechnology.Hdmi, false, true),
        ]);

        Equal(1, snapshot.ActiveDisplayCount);
        Equal(2, snapshot.AvailableDisplayCount);
        True(snapshot.SupportsMultipleDisplays, "The inactive available target should count.");
        return Task.CompletedTask;
    }

    private static Task WindowsExternalTopologyOverridesExtendedPaths()
    {
        DisplayTopologySnapshot snapshot = DisplayTopologyClassifier.Classify(
        [
            DisplayPath(1, 0, 10, DisplayOutputTechnology.DisplayPortEmbedded, true, true),
            DisplayPath(1, 1, 11, DisplayOutputTechnology.Hdmi, true, true),
            DisplayPath(1, 2, 12, DisplayOutputTechnology.DisplayPortExternal, false, true),
        ], DisplayMode.SecondScreenOnly);

        Equal(DisplayMode.SecondScreenOnly, snapshot.CurrentMode);
        Equal(2, snapshot.ActiveDisplayCount);
        Equal(3, snapshot.AvailableDisplayCount);
        Contains("Second screen only", snapshot.Status);
        return Task.CompletedTask;
    }

    private static Task WindowsTopologyIdsMapToProjectionModes()
    {
        Equal(DisplayMode.PcScreenOnly, WindowsDisplayTopologyService.MapTopologyId(0x1));
        Equal(DisplayMode.Duplicate, WindowsDisplayTopologyService.MapTopologyId(0x2));
        Equal(DisplayMode.Extend, WindowsDisplayTopologyService.MapTopologyId(0x4));
        Equal(DisplayMode.SecondScreenOnly, WindowsDisplayTopologyService.MapTopologyId(0x8));
        True(
            WindowsDisplayTopologyService.MapTopologyId(0x10) is null,
            "An unknown Windows topology ID should preserve the path-based fallback.");
        return Task.CompletedTask;
    }

    private static Task MixedSourcesAreNotMislabeled()
    {
        DisplayTopologySnapshot snapshot = DisplayTopologyClassifier.Classify(
        [
            DisplayPath(1, 0, 10, DisplayOutputTechnology.DisplayPortEmbedded, true, true),
            DisplayPath(1, 0, 11, DisplayOutputTechnology.Hdmi, true, true),
            DisplayPath(1, 1, 12, DisplayOutputTechnology.DisplayPortExternal, true, true),
        ]);

        True(snapshot.CurrentMode is null, "A mixed topology should not be called duplicate or extend.");
        Contains("Custom or mixed", snapshot.Status);
        return Task.CompletedTask;
    }

    private static Task NoActiveDisplayIsUnreliable()
    {
        DisplayTopologySnapshot snapshot = DisplayTopologyClassifier.Classify(
        [
            DisplayPath(1, 0, 10, DisplayOutputTechnology.Hdmi, false, true),
        ]);

        True(!snapshot.IsReliable, "A snapshot without an active path is not reliable.");
        True(snapshot.CurrentMode is null, "An unreliable snapshot should not invent a mode.");
        return Task.CompletedTask;
    }

    private static Task PaidProductConfigurationEnablesProfiles()
    {
        ProductConfiguration configuration = ProductConfiguration.For(
            ProductEdition.Paid);

        Equal(ProductEdition.Paid, configuration.Edition);
        Equal("WinQuickSwitch", configuration.DisplayName);
        Equal("WinQuickSwitch", configuration.SettingsDirectoryName);
        True(configuration.IsPaid, "The Paid edition should identify as Paid.");
        True(
            configuration.HasFeature(ProductFeature.Profiles),
            "The Paid edition should enable Profiles.");
        return Task.CompletedTask;
    }

    private static Task FreeProductConfigurationDisablesProfiles()
    {
        ProductConfiguration configuration = ProductConfiguration.For(
            ProductEdition.Free);

        Equal(ProductEdition.Free, configuration.Edition);
        Equal("WinQuickSwitch Free", configuration.DisplayName);
        Equal("WinQuickSwitch Free", configuration.SettingsDirectoryName);
        True(configuration.IsFree, "The Free edition should identify as Free.");
        True(
            !configuration.HasFeature(ProductFeature.Profiles),
            "The Free edition should not enable Profiles.");
        return Task.CompletedTask;
    }

    private static Task OverlayFeatureCatalogKeepsParity()
    {
        True(
            FeatureCatalogParity.IsComplete(ProductConfiguration.Paid),
            "Paid feature descriptors must expose both main-window and overlay surfaces.");
        True(
            FeatureCatalogParity.IsComplete(ProductConfiguration.Free),
            "Free feature descriptors must expose both main-window and overlay surfaces.");
        return Task.CompletedTask;
    }

    private static Task OverlayLauncherOmitsHome()
    {
        True(
            FeatureDescriptorCatalog.GetAvailableFeatures(ProductConfiguration.Paid)
                .Any(feature => feature.Id == OverlayFeatureId.Home),
            "Home should remain available as the Ctrl+0 workspace route.");
        True(
            FeatureDescriptorCatalog.GetLauncherFeatures(ProductConfiguration.Paid)
                .All(feature => feature.Id != OverlayFeatureId.Home),
            "The right launcher should not show a redundant Home button.");
        True(
            FeatureDescriptorCatalog.GetLauncherFeatures(ProductConfiguration.Free)
                .All(feature => feature.Id != OverlayFeatureId.Home),
            "The Free right launcher should not show a redundant Home button.");
        return Task.CompletedTask;
    }

    private static Task OverlayKeyboardNumberingFollowsEditionBoundaries()
    {
        True(
            FeatureDescriptorCatalog.TryGetWorkspacePanelFromControlNumber(
                5,
                profilesAvailable: true,
                out OverlayWorkspacePanel paidFifth),
            "Paid Ctrl+5 should resolve to Profiles.");
        Equal(OverlayWorkspacePanel.Profiles, paidFifth);
        True(
            FeatureDescriptorCatalog.TryGetWorkspacePanelFromControlNumber(
                6,
                profilesAvailable: true,
                out OverlayWorkspacePanel paidSixth),
            "Paid Ctrl+6 should resolve to About.");
        Equal(OverlayWorkspacePanel.About, paidSixth);

        True(
            FeatureDescriptorCatalog.TryGetWorkspacePanelFromControlNumber(
                5,
                profilesAvailable: false,
                out OverlayWorkspacePanel freeFifth),
            "Free Ctrl+5 should resolve to About after Profiles is omitted.");
        Equal(OverlayWorkspacePanel.About, freeFifth);
        True(
            !FeatureDescriptorCatalog.TryGetWorkspacePanelFromControlNumber(
                6,
                profilesAvailable: false,
                out OverlayWorkspacePanel freeSixth),
            "Free Ctrl+6 should remain unassigned.");
        Equal(OverlayWorkspacePanel.Home, freeSixth);
        return Task.CompletedTask;
    }

    private static Task OverlayCardPresetsMigrate()
    {
        True(
            FeatureDescriptorCatalog.TryGetFeature(
                OverlayFeatureId.Devices,
                ProductConfiguration.Paid,
                out OverlayFeatureDescriptor? devices) &&
            devices is not null,
            "Devices should have an overlay feature descriptor.");
        True(
            devices!.DefaultWidth >= 600 && devices.DefaultHeight >= 500,
            "The Devices default should be large enough to show its inventory.");
        Equal(devices.DefaultWidth, devices.MinimumWidth);
        Equal(devices.DefaultHeight, devices.MinimumHeight);
        True(
            FeatureDescriptorCatalog.GetAvailableFeatures(ProductConfiguration.Paid)
                .Where(feature => feature.CanResize)
                .All(feature =>
                    feature.MinimumWidth == feature.DefaultWidth &&
                    feature.MinimumHeight == feature.DefaultHeight),
            "Every resizable widget should use its default size as its resize floor.");
        (double displayWidth, double displayHeight) =
            FeatureDescriptorCatalog.GetDefaultSize(OverlayWidgetType.Display);
        Equal(720d, displayWidth);
        Equal(420d, displayHeight);
        Equal(
            (760d, 600d),
            FeatureDescriptorCatalog.GetDefaultSize(OverlayWidgetType.Audio));
        Equal(
            (840d, 820d),
            FeatureDescriptorCatalog.GetDefaultSize(OverlayWidgetType.Devices));
        Equal(
            (720d, 380d),
            FeatureDescriptorCatalog.GetDefaultSize(OverlayWidgetType.Options));
        Equal(
            (720d, 320d),
            FeatureDescriptorCatalog.GetDefaultSize(OverlayWidgetType.Profiles));
        Equal(
            (600d, 280d),
            FeatureDescriptorCatalog.GetDefaultSize(OverlayWidgetType.About));
        True(
            displayWidth < devices.DefaultWidth &&
            displayHeight < devices.DefaultHeight,
            "Display should stay compact while Devices reserves room for its inventory.");

        IReadOnlyDictionary<OverlayFeatureId, bool> resizePolicy =
            FeatureDescriptorCatalog.GetAvailableFeatures(ProductConfiguration.Paid)
                .ToDictionary(feature => feature.Id, feature => feature.CanResize);
        True(!resizePolicy[OverlayFeatureId.Display], "Display must be fixed-size.");
        True(resizePolicy[OverlayFeatureId.Audio], "Audio must remain resizable for variable endpoints.");
        True(resizePolicy[OverlayFeatureId.Devices], "Devices must remain resizable for variable inventories.");
        True(!resizePolicy.ContainsKey(OverlayFeatureId.Favorites), "Favorites should be folded into Audio rather than exposed as a duplicate feature.");
        True(!resizePolicy[OverlayFeatureId.Options], "Options compact content is fixed-size.");
        True(!resizePolicy[OverlayFeatureId.About], "About compact content is fixed-size.");
        True(resizePolicy[OverlayFeatureId.Profiles], "Paid Profiles must remain resizable for variable entries.");

        True(
            FeatureDescriptorCatalog.TryUpgradeLegacyDefaultSize(
                OverlayFeatureId.Audio,
                420,
                280,
                out double width,
                out double height),
            "An untouched v1.0.16.0 Audio card should migrate to the new preset.");
        Equal(760d, width);
        Equal(600d, height);

        True(
            !FeatureDescriptorCatalog.TryUpgradeLegacyDefaultSize(
                OverlayFeatureId.Audio,
                515,
                333,
                out width,
                out height),
            "A user-resized card must not be treated as an old default.");
        Equal(515d, width);
        Equal(333d, height);
        return Task.CompletedTask;
    }

    private static Task FreeOverlayCompositionExcludesProfiles()
    {
        IReadOnlyList<OverlayFeatureDescriptor> features =
            FeatureDescriptorCatalog.GetAvailableFeatures(ProductConfiguration.Free);
        True(
            features.All(feature => feature.Id != OverlayFeatureId.Profiles),
            "Free overlay navigation must not compose a Profiles feature.");
        True(
            FeatureDescriptorCatalog.GetWidgetPanels(ProductConfiguration.Free)
                .All(panel => panel.Panel != WidgetPanel.Profiles),
            "Free overlay panel routes must not expose Profiles.");
        return Task.CompletedTask;
    }

    private static Task SharedFeatureViewsRejectStaleRevisions()
    {
        FakeQuickSwitchCoordinator coordinator = new();
        using RevisionTrackingFeatureViewModel viewModel = new(coordinator);
        viewModel.Attach();

        QuickSwitchState newest = QuickSwitchState.Empty with
        {
            Options = QuickSwitchState.Empty.Options with
            {
                StatusMessage = "newest",
            },
        };
        QuickSwitchState stale = QuickSwitchState.Empty with
        {
            Options = QuickSwitchState.Empty.Options with
            {
                StatusMessage = "stale",
            },
        };

        coordinator.PublishState(
            newest,
            QuickSwitchRefreshScope.Options,
            revision: 2);
        int applyCountAfterNewest = viewModel.ApplyCount;
        coordinator.PublishState(
            stale,
            QuickSwitchRefreshScope.Options,
            revision: 1);

        Equal("newest", viewModel.StatusMessage);
        Equal(applyCountAfterNewest, viewModel.ApplyCount);
        return Task.CompletedTask;
    }

    private static Task SharedCoordinatorStateIgnoresLegacyControls()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "WinQuickSwitch",
            "MainWindow.SharedUi.cs"));
        const string methodStart = "private QuickSwitchState BuildSharedState()";
        int start = source.IndexOf(methodStart, StringComparison.Ordinal);
        int end = source.IndexOf(
            "private IReadOnlyList<FavoriteAudioEndpointState>",
            start,
            StringComparison.Ordinal);
        True(start >= 0 && end > start, "The shared state projection should exist.");

        string projection = source[start..end];
        string[] forbiddenLegacyControls =
        [
            "PlaybackEndpointsList",
            "RecordingEndpointsList",
            "AudioSessionsList",
            "ConnectedDevicesList",
            "RefreshAudioButton",
            "RefreshDevicesButton",
            "DisplayModeButtons",
            "StatusText.Text",
            "_displayTopologyService.GetSnapshot",
        ];
        foreach (string control in forbiddenLegacyControls)
        {
            True(
                !projection.Contains(control, StringComparison.Ordinal),
                $"BuildSharedState must not read {control}.");
        }

        True(
            source.Contains(
                "IQuickSwitchCoordinator.State => _sharedState;",
                StringComparison.Ordinal),
            "State access should return the cached immutable snapshot.");
        True(
            source.Contains("_sharedState = state;", StringComparison.Ordinal),
            "Publishing should replace the cached snapshot atomically.");
        return Task.CompletedTask;
    }

    private static Task LegacyWorkspaceBuildersAreRemoved()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "WinQuickSwitch",
            "MainWindow.xaml.cs"));
        string[] removedBuilders =
        [
            "BuildCompactDisplayWidgetContent",
            "BuildCompactAudioWidgetContent",
            "BuildCompactDevicesWidgetContent",
            "BuildCompactWorkspaceActions",
            "BuildWorkspaceInspectorContent",
            "AddWorkspaceAudioInventory",
            "AddWorkspaceDeviceInventory",
        ];
        foreach (string builder in removedBuilders)
        {
            True(
                !source.Contains(builder, StringComparison.Ordinal),
                $"The legacy {builder} path should stay removed.");
        }

        string[] retiredSidebarSymbols =
        [
            "SidebarOverlayWindow",
            "SidebarOverlayPanel",
            "SidebarOverlayAction",
            "HandleOverlayPanelRequested",
            "CloseSidebarOverlay",
        ];
        foreach (string symbol in retiredSidebarSymbols)
        {
            True(
                !source.Contains(symbol, StringComparison.Ordinal),
                $"The retired {symbol} path should stay removed.");
        }

        string overlayDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "WinQuickSwitch",
            "Features",
            "Overlay");
        True(
            !File.Exists(Path.Combine(overlayDirectory, "SidebarOverlayWindow.xaml")) &&
            !File.Exists(Path.Combine(overlayDirectory, "SidebarOverlayWindow.xaml.cs")),
            "The retired sidebar window files should stay removed.");

        return Task.CompletedTask;
    }

    private static Task SharedFeatureRefreshFailuresStayRecoverable()
    {
        FakeQuickSwitchCoordinator coordinator = new()
        {
            RefreshException = new InvalidOperationException("inventory unavailable"),
        };
        using RevisionTrackingFeatureViewModel viewModel = new(coordinator);

        viewModel.Attach();

        True(
            viewModel.RefreshErrorVisibility == Visibility.Visible,
            "A failed background refresh should expose a visible recovery state.");
        True(
            viewModel.RefreshErrorMessage.Contains(
                "inventory unavailable",
                StringComparison.Ordinal),
            "The recovery state should explain the refresh failure.");

        coordinator.RefreshException = null;
        viewModel.RetryRefreshCommand.Execute(null);

        True(
            viewModel.RefreshErrorVisibility == Visibility.Collapsed,
            "A successful retry should clear the recovery state.");
        True(
            coordinator.RefreshCount == 2,
            "Retry should request a second refresh.");
        return Task.CompletedTask;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the WinQuickSwitch repository root.");
    }

    private static Task LatestRefreshGateCancelsSupersededWork()
    {
        using LatestRefreshGate gate = new();
        using LatestRefreshLease first = gate.Begin();
        using LatestRefreshLease second = gate.Begin();

        True(
            first.Token.IsCancellationRequested,
            "Starting a newer refresh should cancel the prior request.");
        True(second.IsCurrent, "The newest refresh should remain current.");
        True(
            !second.Token.IsCancellationRequested,
            "The newest refresh should not be cancelled by its predecessor.");
        return Task.CompletedTask;
    }

    private static Task DisposingStaleRefreshKeepsNewestLease()
    {
        using LatestRefreshGate gate = new();
        LatestRefreshLease first = gate.Begin();
        using LatestRefreshLease second = gate.Begin();

        first.Dispose();

        True(
            second.IsCurrent,
            "Disposing a stale lease must not clear the newer refresh.");
        second.ThrowIfSuperseded();
        return Task.CompletedTask;
    }

    private static Task SharedViewFactoryCoversEnabledFeatures()
    {
        FeatureViewFactory factory = new(
            new FakeQuickSwitchCoordinator(),
            ProductConfiguration.Paid);
        OverlayFeatureDescriptor[] features =
            FeatureDescriptorCatalog.GetAvailableFeatures(ProductConfiguration.Paid)
                .Where(feature => feature.MainPanel is not null)
                .ToArray();

        foreach (OverlayFeatureDescriptor feature in features)
        {
            True(
                factory.CanCreate(feature.Id),
                $"The shared factory should create {feature.Id} for every host.");
            True(
                typeof(FrameworkElement).IsAssignableFrom(
                    FeatureViewFactory.GetViewType(feature.Id)),
                $"{feature.Id} should map to one reusable WPF view type.");
        }

        Equal(6, features.Length);
        return Task.CompletedTask;
    }

    private static Task FreeSharedViewFactoryExcludesProfiles()
    {
        FeatureViewFactory factory = new(
            new FakeQuickSwitchCoordinator(),
            ProductConfiguration.Free);
        True(
            !factory.CanCreate(OverlayFeatureId.Profiles),
            "Free must not compose a Profiles shared view.");
        True(factory.CanCreate(OverlayFeatureId.Display), "Free should retain Display.");
        True(factory.CanCreate(OverlayFeatureId.Audio), "Free should retain Audio.");
        True(factory.CanCreate(OverlayFeatureId.Devices), "Free should retain Devices.");
        True(factory.CanCreate(OverlayFeatureId.Options), "Free should retain Options.");
        True(factory.CanCreate(OverlayFeatureId.About), "Free should retain About.");
        return Task.CompletedTask;
    }

    private static Task AudioCommandSettingsRoundTrip()
    {
        WidgetShortcut chord = new(WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt, 0x55);
        WidgetSettings original = WidgetSettings.Default.WithCommandShortcut("audio.volume-up", chord)
            with { AudioVolumeStepPercent = 7 };
        string json = System.Text.Json.JsonSerializer.Serialize(original);
        WidgetSettings restored = System.Text.Json.JsonSerializer.Deserialize<WidgetSettings>(json)!.Normalize(false);
        Equal(chord, restored.GetCommandShortcut("audio.volume-up"));
        Equal(original.ToggleWidget, restored.ToggleWidget);
        Equal(7, restored.AudioVolumeStepPercent);
        Equal(chord, original.GetCommandShortcut("audio.volume-up"));
        True(restored.ResetShortcuts().GetCommandShortcut("audio.volume-up") is null, "Reset clears keyed commands.");
        True(restored.WithCommandShortcut("profile:private", chord).Normalize(false).GetCommandShortcut("profile:private") is null,
            "Free cannot retain an unsupported profile command binding.");
        Equal(20, (restored with { AudioVolumeStepPercent = 99 }).Normalize().AudioVolumeStepPercent);
        Equal(1, (restored with { AudioVolumeStepPercent = 0 }).Normalize().AudioVolumeStepPercent);
        WidgetSettings legacy = System.Text.Json.JsonSerializer.Deserialize<WidgetSettings>("{\"UseDarkTheme\":true}")!.Normalize();
        Equal(5, legacy.AudioVolumeStepPercent);
        True(legacy.GetCommandShortcut("audio.volume-up") is null, "Older settings do not assign new chords.");
        foreach (QuickSwitchCommandDescriptor descriptor in QuickSwitchCommandCatalog.GetKeyedCommands())
        {
            True(descriptor.LegacyHotkeyAction is null, "New commands never renumber legacy actions.");
            True(descriptor.CreateCommand() is ExecuteAudioQuickActionCommand, "Audio keys create typed commands.");
            Equal(QuickSwitchRefreshScope.Audio, QuickSwitchCommandMetadata.GetDefaultRefreshScope(descriptor.CreateCommand()));
            Equal(OverlayDisposition.StayOpen, descriptor.DefaultDisposition);
        }
        return Task.CompletedTask;
    }

    private static Task AudioCommandEditorUsesStableKeys()
    {
        FakeQuickSwitchCoordinator coordinator = new();
        using OptionsFeatureViewModel model = new(coordinator, ProductConfiguration.Free);
        model.Attach();
        OptionsShortcutRowViewModel row = model.ShortcutRows.Single(item => item.CommandKey == "audio.volume-up");
        row.CaptureCommand.Execute(null);
        True(coordinator.Commands.Last() is CaptureCommandShortcutCommand { CommandKey: "audio.volume-up" }, "Capture uses the key.");
        row.ClearCommand.Execute(null);
        True(coordinator.Commands.Last() is ClearCommandShortcutCommand { CommandKey: "audio.volume-up" }, "Unset uses the key.");
        model.AudioVolumeStepPercent = 8;
        True(coordinator.Commands.Last() is SetAudioShortcutStepCommand { Percent: 8 }, "Step is a shared command.");
        coordinator.PublishState(coordinator.State with { Options = coordinator.State.Options with
        {
            CommandShortcutTexts = new Dictionary<string, string> { ["audio.volume-up"] = "Ctrl + Alt + U" },
            AudioVolumeStepPercent = 8,
        } }, revision: 12);
        // The earlier WPF test owns Application's dispatcher; explicitly attach
        // from the current snapshot rather than depending on its stopped pump.
        model.Detach();
        model.Attach();
        Equal("Ctrl + Alt + U", row.ShortcutText);
        Equal(8, model.AudioVolumeStepPercent);
        return Task.CompletedTask;
    }

    private static Task ShortcutSearchPreservesSections()
    {
        FakeQuickSwitchCoordinator coordinator = new();
        using OptionsFeatureViewModel model = new(coordinator, ProductConfiguration.Free);
        using OptionsFeatureViewModel otherSurface = new(coordinator, ProductConfiguration.Free);
        model.Attach();
        True(model.ShortcutSections.SelectMany(section => section.Rows)
            .SequenceEqual(model.ShortcutRows.OrderBy(row => row.Category)),
            "Every built-in shortcut belongs to its catalog category exactly once.");
        True(model.ShortcutSections.All(section => section.Title != "Profiles"),
            "Free never exposes a Profiles section.");
        ShortcutSectionViewModel display = model.ShortcutSections.Single(section => section.Title == "Display");
        display.IsExpanded = false;
        OptionsShortcutRowViewModel extend = display.Rows.Single(row => row.Action == WidgetHotkeyAction.Extend);
        model.ShortcutSearchText = "  eXtEnD  ";
        Equal(1, model.ShortcutSections.Sum(section => section.FilteredRows.Count));
        True(ReferenceEquals(extend, display.FilteredRows.Single()), "Search reuses the live command row.");
        True(display.Rows.Count > 1, "Filtering keeps the full ItemsSource and its automation containers stable.");
        True(display.Rows.Where(row => row != extend).All(row => row.SearchVisibility == Visibility.Collapsed),
            "Unmatched rows collapse without removing their containers.");
        True(display.IsExpanded, "A matching collapsed section opens during search.");
        Equal(string.Empty, otherSurface.ShortcutSearchText);
        model.ShortcutSearchText = string.Empty;
        True(!display.IsExpanded, "Clearing search restores the previous expansion choice.");
        var rowsBeforeRefresh = display.FilteredRows;
        coordinator.PublishState(coordinator.State, revision: 10);
        True(ReferenceEquals(rowsBeforeRefresh, display.FilteredRows), "Ordinary state refresh cannot recreate filtered rows.");
        model.ShortcutSearchText = "navigation";
        True(model.ShortcutSections.Where(section => section.Visibility == Visibility.Visible)
            .All(section => section.Title == "Navigation"), "Search matches catalog categories.");
        model.ShortcutSearchText = "does-not-exist";
        True(model.ShortcutSections.All(section => section.Visibility == Visibility.Collapsed),
            "No matches hides empty categories.");
        True(model.ShortcutSearchSummary.StartsWith("0 of "), "Empty search results are explained.");
        model.ShortcutSearchText = string.Empty;
        extend.SetShortcutText("Ctrl + Alt + E");
        model.ShortcutSearchText = "alt + e";
        True(display.FilteredRows.Contains(extend), "Search matches the assigned combination.");
        Equal(0, coordinator.Commands.Count);
        return Task.CompletedTask;
    }

    private static Task OptionsSharedViewRendersShortcutTemplate()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                App application = new();
                application.InitializeComponent();
                Grid fitRows = new();
                fitRows.Children.Add(new Border { Height = 40 });
                ScrollViewer fitBody = new()
                {
                    Content = new Border { Height = 200 },
                    Margin = new Thickness(0, 0, 18, 18),
                    Padding = new Thickness(2),
                    BorderThickness = new Thickness(1)
                };
                Grid.SetRow(fitBody, 1);
                fitRows.Children.Add(fitBody);
                Grid fitContainer = new();
                fitContainer.Children.Add(fitRows);
                Border fitCard = new()
                {
                    Child = fitContainer,
                    Padding = new Thickness(10),
                    BorderThickness = new Thickness(1)
                };
                Equal(286d, OverlayWorkspaceWindow.MeasureCompleteCardHeight(fitCard, 720));
                int deferredCreations = 0;
                Border measuredChild = new() { Width = 320, Height = 240 };
                DeferredFeatureContent deferred = new(() =>
                {
                    deferredCreations++;
                    return measuredChild;
                }) { Visibility = Visibility.Collapsed };
                deferred.Measure(new Size(720, 900));
                Equal(0, deferredCreations);
                deferred.Visibility = Visibility.Visible;
                deferred.Measure(new Size(720, 900));
                Equal(1, deferredCreations);
                Equal(new Size(320, 240), deferred.DesiredSize);
                deferred.Visibility = Visibility.Collapsed;
                deferred.Visibility = Visibility.Visible;
                deferred.InvalidateMeasure();
                deferred.Measure(new Size(800, 950));
                Equal(1, deferredCreations);
                True(ReferenceEquals(measuredChild, deferred.Child),
                    "Reopening or resizing a widget must reuse its shared visual instance.");
                FeatureViewFactory factory = new(
                    new FakeQuickSwitchCoordinator(),
                    ProductConfiguration.Paid);
                SharedFeatureViewInstance instance = factory.Create(
                    OverlayFeatureId.Options,
                    SharedFeatureSurface.MainWindow);
                SharedFeatureViewLifetime.Attach(instance.View);
                instance.View.Measure(new Size(720, 900));
                instance.View.Arrange(new Rect(0, 0, 720, 900));
                instance.View.UpdateLayout();
                Expander? generalShortcuts =
                    instance.View.FindName("GeneralShortcutsExpander") as Expander;
                True(
                    generalShortcuts is not null,
                    "Options should expose a show/hide control for keyboard shortcuts.");
                True(
                    generalShortcuts!.IsExpanded == false,
                    "Keyboard shortcuts should start collapsed to keep Options compact.");
                generalShortcuts.IsExpanded = true;
                TextBox search = (TextBox)instance.View.FindName("ShortcutSearchBox");
                Equal("Search command shortcuts", AutomationProperties.GetName(search));
                instance.View.UpdateLayout();
                SharedFeatureViewLifetime.Detach(instance.View);

                SharedFeatureViewInstance audioInstance = factory.Create(
                    OverlayFeatureId.Audio,
                    SharedFeatureSurface.MainWindow);
                SharedFeatureViewLifetime.Attach(audioInstance.View);
                audioInstance.View.Measure(new Size(900, 900));
                audioInstance.View.Arrange(new Rect(0, 0, 900, 900));
                audioInstance.View.UpdateLayout();
                SharedFeatureViewLifetime.Detach(audioInstance.View);

                ComboBox favoriteChoice = new()
                {
                    Style = (Style)application.Resources[typeof(ComboBox)],
                    ItemsSource = new[] { new FavoriteChoiceViewModel("", "Use current endpoint") },
                    DisplayMemberPath = "Name",
                    SelectedIndex = 0,
                    Width = 360
                };
                favoriteChoice.Measure(new Size(360, 60));
                favoriteChoice.Arrange(new Rect(0, 0, 360, 60));
                favoriteChoice.UpdateLayout();
                favoriteChoice.ApplyTemplate();
                ContentPresenter selection = (ContentPresenter)favoriteChoice.Template.FindName(
                    "SelectionContent", favoriteChoice);
                True(selection.ContentTemplateSelector is not null,
                    "The themed selection must use the DisplayMemberPath template selector, not object.ToString().");
                Equal(favoriteChoice.ItemTemplateSelector, selection.ContentTemplateSelector);
                True(ContainsRenderedText(selection, "Use current endpoint"),
                    "The selected favorite should render its friendly name.");
                True(ProfilesQuickSwitchState.Empty.StatusMessage != "No profiles saved yet.",
                    "The shared status must not contradict a populated profile inventory before refresh.");

                List<QuickSwitchCommand> widgetCommands = [];
                OverlayWorkspaceWindow overlay = new(
                    commandRequested: widgetCommands.Add,
                    monitorBounds: new OverlayMonitorBounds(0, 0, 1920, 1080));
                overlay.UpdateLauncherLayout(920);
                Button openMain = (Button)overlay.FindName("OpenMainLauncherButton");
                Button exit = (Button)overlay.FindName("ExitLauncherButton");
                Border launcher = (Border)overlay.FindName("LauncherPanel");
                TextBlock shortcutHint = (TextBlock)overlay.FindName("LauncherShortcutHint");
                Equal("▣", openMain.Content);
                Equal("×", exit.Content);
                Equal(new Thickness(6), launcher.Padding);
                Equal(Visibility.Collapsed, shortcutHint.Visibility);
                True(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(openMain)),
                    "Icon-only launcher actions retain their accessible names.");
                overlay.UpdateLauncherLayout(1440);
                Equal("Open main window", openMain.Content);
                Equal("Exit WinQuickSwitch", exit.Content);
                Equal(new Thickness(18), launcher.Padding);
                Equal(Visibility.Visible, shortcutHint.Visibility);
                foreach (OverlayWorkspacePanel panel in new[]
                    { OverlayWorkspacePanel.Display, OverlayWorkspacePanel.Audio,
                      OverlayWorkspacePanel.Devices, OverlayWorkspacePanel.Options })
                {
                    overlay.SetWidgets([new OverlayWorkspaceWidget(
                        panel.ToString(), panel, panel.ToString(), "")]);
                    Canvas canvas = (Canvas)overlay.FindName("WidgetCanvas");
                    Button? refresh = FindLogicalButton(canvas, $"Refresh {panel}");
                    if (panel == OverlayWorkspacePanel.Options)
                    {
                        True(refresh is null, "Static Options has no inventory refresh action.");
                        continue;
                    }
                    True(refresh is not null, $"{panel} has a header Refresh control.");
                    OverlayWorkspaceWidgetLayout[] beforeRefresh = overlay.CaptureLayout().ToArray();
                    refresh!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Equal(new RefreshOverlayFeatureCommand(
                        Enum.Parse<OverlayFeatureId>(panel.ToString())), widgetCommands.Last());
                    True(beforeRefresh.SequenceEqual(overlay.CaptureLayout()),
                        "Refreshing a widget must not fit, move, or reconstruct its geometry.");
                    True(ReferenceEquals(refresh, FindLogicalButton(canvas, $"Refresh {panel}")),
                        "Refresh preserves the existing card control instance.");
                }
                Thumb? inspectorResizeGrip =
                    overlay.FindName("InspectorResizeGrip") as Thumb;
                True(
                    inspectorResizeGrip is { Width: >= 40 },
                    "The inspector resize grip should provide a 40-DIP hit target.");
                TextBlock? feedbackText =
                    overlay.FindName("FeedbackText") as TextBlock;
                True(
                    feedbackText is not null,
                    "The overlay feedback region should be present.");
                Equal(
                    AutomationLiveSetting.Polite,
                    AutomationProperties.GetLiveSetting(feedbackText!));
                True(
                    !string.IsNullOrWhiteSpace(
                        AutomationProperties.GetName(feedbackText!)),
                    "The overlay feedback region should have an accessible name.");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException(
                $"Shared WPF view validation failed: {failure.Message}",
                failure);
        }

        return Task.CompletedTask;
    }

    private static Button? FindLogicalButton(DependencyObject root, string name)
    {
        if (root is Button button && AutomationProperties.GetName(button) == name)
        {
            return button;
        }
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject dependency &&
                FindLogicalButton(dependency, name) is Button found)
            {
                return found;
            }
        }
        return null;
    }

    private static bool ContainsRenderedText(DependencyObject root, string expected)
    {
        if (root is TextBlock text && text.Text == expected)
        {
            return true;
        }
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (ContainsRenderedText(VisualTreeHelper.GetChild(root, index), expected))
            {
                return true;
            }
        }
        return false;
    }

    private static Task AudioStateExposesAppVolumeSessions()
    {
        AudioEndpointInfo output = new(
            "output-id",
            "Speakers",
            AudioEndpointKind.Playback,
            true,
            true,
            false);
        AudioSessionInfo session = new(
            "session-id",
            "Test app",
            output.Name,
            0.72f,
            false);
        QuickSwitchState state = QuickSwitchState.Empty with
        {
            Audio = new AudioQuickSwitchState(
                QuickSwitchLoadState.Ready,
                [output],
                [],
                [session],
                [],
                [],
                false,
                "1 out · 0 in · 1 app")
        };
        AudioFeatureViewModel viewModel =
            new(new FakeQuickSwitchCoordinator(state));
        viewModel.Attach();
        Equal(1, viewModel.SessionRows.Count);
        True(viewModel.HasSessionRows, "The app-volume row should be visible.");
        Equal("1 active app volume session.", viewModel.SessionSummary);
        viewModel.Dispose();
        return Task.CompletedTask;
    }

    private static async Task AudioVolumeEditingPreservesLiveSliderRow()
    {
        AudioEndpointInfo output = new(
            "output-id",
            "Speakers",
            AudioEndpointKind.Playback,
            true,
            true,
            false);
        AudioSessionInfo session = new(
            "session-id",
            "Test app",
            output.Name,
            0.72f,
            false);
        QuickSwitchState state = QuickSwitchState.Empty with
        {
            Audio = new AudioQuickSwitchState(
                QuickSwitchLoadState.Ready,
                [output],
                [],
                [session],
                [],
                [],
                false,
                "1 out · 0 in · 1 app")
        };

        FakeQuickSwitchCoordinator coordinator = new(state);
        AudioFeatureViewModel viewModel = new(coordinator);
        viewModel.Attach();
        AudioSessionRowViewModel row = viewModel.SessionRows.Single();

        row.VolumePercent = 55;
        Equal(0, coordinator.Commands.Count);

        coordinator.PublishState(
            state with
            {
                Audio = state.Audio with
                {
                    Sessions = [session]
                }
            },
            QuickSwitchRefreshScope.Audio);

        True(
            ReferenceEquals(row, viewModel.SessionRows.Single()),
            "An audio refresh must not replace the slider row while it is being edited.");
        Equal(55, row.VolumePercent);
        Equal("55%", row.VolumeLabel);

        await row.CommitVolumeAsync();
        True(
            coordinator.Commands.OfType<SetAudioSessionVolumeCommand>().Any(command =>
                command.SessionId == session.Id && Math.Abs(command.Volume - 0.55f) < 0.001f),
            "The final slider value should be committed once after editing.");
        viewModel.Dispose();
    }

    private static Task OverlayCommandMetadataKeepsSmartCloseRules()
    {
        Equal(
            OverlayDisposition.CloseOverlay,
            QuickSwitchCommandMetadata.GetDefaultDisposition(
                new ApplyDisplayModeCommand(DisplayMode.Extend)));
        Equal(
            OverlayDisposition.CloseOverlay,
            QuickSwitchCommandMetadata.GetDefaultDisposition(
                new ApplyAudioFavoriteCommand(AudioEndpointKind.Playback, 0)));
        Equal(
            OverlayDisposition.CloseOverlay,
            QuickSwitchCommandMetadata.GetDefaultDisposition(
                new ApplyProfileCommand("profile")));
        Equal(
            OverlayDisposition.OpenInspector,
            QuickSwitchCommandMetadata.GetDefaultDisposition(
                new ExpandOverlayWidgetCommand(OverlayFeatureId.Audio)));
        Equal(
            OverlayDisposition.StayOpen,
            QuickSwitchCommandMetadata.GetDefaultDisposition(
                new SetOverlayOptionCommand(OverlayOption.DarkTheme, true)));
        return Task.CompletedTask;
    }

    private static Task TypedCommandCatalogMapsPersistedActions()
    {
        IReadOnlyList<QuickSwitchCommandDescriptor> descriptors =
            QuickSwitchCommandCatalog.GetBuiltIns(ProductConfiguration.Paid);
        Equal(Enum.GetValues<WidgetHotkeyAction>().Length, descriptors.Count);
        Equal(
            descriptors.Count,
            descriptors.Select(descriptor => descriptor.Key)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Equal(
            descriptors.Count,
            descriptors.Select(descriptor => descriptor.LegacyHotkeyAction)
                .Distinct()
                .Count());

        foreach (WidgetHotkeyAction action in Enum.GetValues<WidgetHotkeyAction>())
        {
            True(
                QuickSwitchCommandCatalog.TryGetLegacyHotkey(
                    action,
                    ProductConfiguration.Paid,
                    out QuickSwitchCommandDescriptor descriptor),
                $"{action} is missing from the typed command catalog.");
            True(
                descriptor.CreateCommand() is QuickSwitchCommand,
                $"{action} did not create a typed command.");
        }

        QuickSwitchCommandCatalog.TryGetLegacyHotkey(
            WidgetHotkeyAction.Display,
            ProductConfiguration.Paid,
            out QuickSwitchCommandDescriptor display);
        Equal(
            new OpenMainWindowCommand(OverlayFeatureId.Display),
            display.CreateCommand());
        QuickSwitchCommandCatalog.TryGetLegacyHotkey(
            WidgetHotkeyAction.ToggleWidget,
            ProductConfiguration.Paid,
            out QuickSwitchCommandDescriptor toggle);
        Equal(WidgetSettings.Default.ToggleWidget, toggle.DefaultShortcut);
        QuickSwitchCommandCatalog.TryGetLegacyHotkey(
            WidgetHotkeyAction.Extend,
            ProductConfiguration.Paid,
            out QuickSwitchCommandDescriptor extend);
        Equal(
            new ApplyDisplayModeCommand(DisplayMode.Extend),
            extend.CreateCommand());
        Equal(OverlayDisposition.CloseOverlay, extend.DefaultDisposition);
        QuickSwitchCommandCatalog.TryGetLegacyHotkey(
            WidgetHotkeyAction.FavoriteInput4,
            ProductConfiguration.Paid,
            out QuickSwitchCommandDescriptor microphone);
        Equal(
            new ApplyAudioFavoriteCommand(AudioEndpointKind.Recording, 3),
            microphone.CreateCommand());
        return Task.CompletedTask;
    }

    private static Task TypedCommandCatalogEnforcesProfileEdition()
    {
        ProfileDefinition profile = new(
            "work",
            "Workday setup",
            true,
            null,
            DisplayMode.Extend);
        QuickSwitchCommandDescriptor descriptor =
            QuickSwitchCommandCatalog.CreateProfile(profile);

        True(
            descriptor.IsAvailable(ProductConfiguration.Paid),
            "Paid should expose profile commands.");
        True(
            !descriptor.IsAvailable(ProductConfiguration.Free),
            "Free must reject profile commands.");
        Equal(new ApplyProfileCommand("work"), descriptor.CreateCommand());
        True(
            QuickSwitchCommandCatalog.GetBuiltIns(ProductConfiguration.Free)
                .All(candidate => candidate.Category != QuickSwitchCommandCategory.Profiles),
            "Free must not compose profile commands.");
        return Task.CompletedTask;
    }

    private static Task ExpandedFixedShortcutsMapTypedCommands()
    {
        IReadOnlyDictionary<WidgetHotkeyAction, QuickSwitchCommand> expected =
            new Dictionary<WidgetHotkeyAction, QuickSwitchCommand>
            {
                [WidgetHotkeyAction.Options] =
                    new OpenMainWindowCommand(OverlayFeatureId.Options),
                [WidgetHotkeyAction.ToggleWiFi] =
                    new ToggleWirelessRadioCommand(WirelessRadioKind.WiFi),
                [WidgetHotkeyAction.ToggleBluetooth] =
                    new ToggleWirelessRadioCommand(WirelessRadioKind.Bluetooth),
                [WidgetHotkeyAction.ToggleTaskbarAutoHide] =
                    new ToggleTaskbarAutoHideCommand(),
                [WidgetHotkeyAction.RefreshDisplay] =
                    new RefreshOverlayFeatureCommand(OverlayFeatureId.Display),
                [WidgetHotkeyAction.RefreshAudio] =
                    new RefreshOverlayFeatureCommand(OverlayFeatureId.Audio),
                [WidgetHotkeyAction.RefreshDevices] =
                    new RefreshOverlayFeatureCommand(OverlayFeatureId.Devices),
                [WidgetHotkeyAction.RefreshAll] =
                    new RefreshOverlayFeatureCommand(OverlayFeatureId.Home),
            };

        foreach ((WidgetHotkeyAction action, QuickSwitchCommand command) in expected)
        {
            True(
                QuickSwitchCommandCatalog.TryGetLegacyHotkey(
                    action,
                    ProductConfiguration.Paid,
                    out QuickSwitchCommandDescriptor descriptor),
                $"{action} should resolve through the command catalog.");
            Equal(command, descriptor.CreateCommand());
        }

        Equal(
            0x5157 + 16,
            WindowsGlobalHotkey.GetId(WidgetHotkeyAction.ToggleOverlay));
        Equal(
            0x5157 + 17,
            WindowsGlobalHotkey.GetId(WidgetHotkeyAction.Options));
        Equal(
            QuickSwitchRefreshScope.All,
            QuickSwitchCommandMetadata.GetDefaultRefreshScope(
                new RefreshOverlayFeatureCommand(OverlayFeatureId.Home)));
        return Task.CompletedTask;
    }

    private static Task OverlayLauncherCommandsFollowEdition()
    {
        IReadOnlyList<OverlayFeatureDescriptor> paidFeatures =
            FeatureDescriptorCatalog.GetLauncherFeatures(ProductConfiguration.Paid);
        IReadOnlyList<OverlayFeatureDescriptor> freeFeatures =
            FeatureDescriptorCatalog.GetLauncherFeatures(ProductConfiguration.Free);

        foreach (OverlayFeatureDescriptor feature in paidFeatures)
        {
            QuickSwitchCommandDescriptor descriptor =
                QuickSwitchCommandCatalog.CreateOverlayNavigation(feature.Id);
            True(
                descriptor.IsAvailable(ProductConfiguration.Paid),
                $"Paid launcher command {feature.Id} should be available.");
            Equal(
                new NavigateToOverlayFeatureCommand(feature.Id),
                descriptor.CreateCommand());
        }

        Equal(
            paidFeatures.Count,
            paidFeatures
                .Select(feature => QuickSwitchCommandCatalog
                    .CreateOverlayNavigation(feature.Id)
                    .Key)
                .Distinct(StringComparer.Ordinal)
                .Count());
        True(
            paidFeatures.Any(feature => feature.Id == OverlayFeatureId.Profiles),
            "Paid launcher should include Profiles.");
        True(
            freeFeatures.All(feature => feature.Id != OverlayFeatureId.Profiles),
            "Free launcher must exclude Profiles.");
        True(
            !QuickSwitchCommandCatalog
                .CreateOverlayNavigation(OverlayFeatureId.Profiles)
                .IsAvailable(ProductConfiguration.Free),
            "Free must reject a persisted or stale Profiles navigation command.");

        QuickSwitchCommandDescriptor home =
            QuickSwitchCommandCatalog.CreateOverlayNavigation(OverlayFeatureId.Home);
        Equal(
            new NavigateToOverlayFeatureCommand(OverlayFeatureId.Home),
            home.CreateCommand());
        True(
            paidFeatures.All(feature => feature.Id != OverlayFeatureId.Home),
            "Home should remain keyboard-routable without duplicating the launcher.");
        return Task.CompletedTask;
    }

    private static Task OverlayShellRoutesTypedCommands()
    {
        string repositoryRoot = FindRepositoryRoot();
        string overlaySource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "WinQuickSwitch",
            "Features",
            "Overlay",
            "OverlayWorkspaceWindow.xaml.cs"));
        string mainSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "WinQuickSwitch",
            "MainWindow.xaml.cs"));

        Contains("Action<QuickSwitchCommand>? _commandRequested", overlaySource);
        Contains("CreateOverlayNavigation(feature)", overlaySource);
        Contains("QuickSwitchCommandCatalog.OpenMainWindow.CreateCommand()", overlaySource);
        Contains("QuickSwitchCommandCatalog.ExitApplication.CreateCommand()", overlaySource);
        Contains("commandRequested: HandleWorkspaceCommandRequested", mainSource);
        Contains("command is NavigateToOverlayFeatureCommand", mainSource);
        Contains("command is ExitApplicationCommand", mainSource);
        True(
            !overlaySource.Contains("_panelRequested", StringComparison.Ordinal) &&
            !overlaySource.Contains("_openMainWindowRequested", StringComparison.Ordinal) &&
            !overlaySource.Contains("_exitRequested", StringComparison.Ordinal),
            "The overlay must not retain untyped launcher or footer callbacks.");
        Contains("_overlayNavigationRevision", mainSource);
        Contains("workspace.CurrentPanel != panel", mainSource);
        return Task.CompletedTask;
    }

    private static Task OverlayGeometryActionsRemainLocal()
    {
        string repositoryRoot = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "WinQuickSwitch",
            "Features",
            "Overlay",
            "OverlayWorkspaceWindow.xaml.cs"));

        Contains("OverlayWorkspaceActionKind.Collapse", source);
        Contains("OverlayWorkspaceActionKind.FitContent", source);
        Contains("OverlayWorkspaceActionKind.Remove", source);
        True(
            !source.Contains("IQuickSwitchCoordinator", StringComparison.Ordinal) &&
            !source.Contains("ExecuteAsync(", StringComparison.Ordinal),
            "Move, resize, fit, and remove must stay local to the workspace shell.");

        string floatingSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "WinQuickSwitch",
            "Features",
            "Widget",
            "FloatingQuickWidgetWindow.xaml.cs"));
        True(
            !floatingSource.Contains("Action<string>", StringComparison.Ordinal) &&
            !floatingSource.Contains("FloatingQuickAction", StringComparison.Ordinal),
            "Floating windows must host shared typed feature views without a legacy string-action layer.");
        return Task.CompletedTask;
    }

    private static Task GlobalHotkeysRouteThroughTypedCatalog()
    {
        string sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "WinQuickSwitch",
            "MainWindow.xaml.cs");
        string source = File.ReadAllText(sourcePath);

        Contains("QuickSwitchCommandCatalog.TryGetLegacyHotkey", source);
        Contains("descriptor.CreateCommand()", source);
        True(
            !source.Contains("switch (action)", StringComparison.Ordinal),
            "The window must not maintain a second global-hotkey action router.");
        return Task.CompletedTask;
    }

    private static Task PanelNavigationFollowsEdition()
    {
        IReadOnlyList<WidgetPanel> paidPanels =
            WidgetPanelNavigation.GetAvailablePanels(ProductConfiguration.Paid);
        Equal(6, paidPanels.Count);
        Equal(WidgetPanel.Profiles, paidPanels[4]);
        Equal(WidgetPanel.About, paidPanels[5]);
        Equal(
            WidgetPanel.Profiles,
            WidgetPanelNavigation.FromControlNumber(ProductConfiguration.Paid, 5));
        Equal(
            WidgetPanel.About,
            WidgetPanelNavigation.FromControlNumber(ProductConfiguration.Paid, 6));

        IReadOnlyList<WidgetPanel> freePanels =
            WidgetPanelNavigation.GetAvailablePanels(ProductConfiguration.Free);
        Equal(5, freePanels.Count);
        Equal(WidgetPanel.About, freePanels[4]);
        Equal(
            WidgetPanel.About,
            WidgetPanelNavigation.FromControlNumber(ProductConfiguration.Free, 5));
        Equal<WidgetPanel?>(
            null,
            WidgetPanelNavigation.FromControlNumber(ProductConfiguration.Free, 6));
        return Task.CompletedTask;
    }

    private static Task WidgetSettingsUseEditionSpecificDefaultPaths()
    {
        string paidPath = JsonWidgetSettingsStore.GetDefaultPath(
            ProductConfiguration.Paid);
        string freePath = JsonWidgetSettingsStore.GetDefaultPath(
            ProductConfiguration.Free);

        True(
            paidPath.EndsWith(
                Path.Combine("WinQuickSwitch", "settings.json"),
                StringComparison.OrdinalIgnoreCase),
            $"Paid settings should use its own folder: {paidPath}");
        True(
            freePath.EndsWith(
                Path.Combine("WinQuickSwitch Free", "settings.json"),
                StringComparison.OrdinalIgnoreCase),
            $"Free settings should use its own folder: {freePath}");
        True(
            !string.Equals(paidPath, freePath, StringComparison.OrdinalIgnoreCase),
            "Free and Paid settings must not share a file.");
        return Task.CompletedTask;
    }

    private static Task FreeProfileFeatureDoesNotLoadOrSaveData()
    {
        IProfileStore freeStore = ProfileFeature.CreateStore(
            ProductConfiguration.Free);
        True(
            freeStore is not JsonProfileStore,
            "Free composition must not construct the JSON profile store.");

        TrackingProfileStore store = new(ProfileCatalog.Empty);
        IProfileFeature feature = ProfileFeature.Create(
            ProductConfiguration.Free,
            store);

        True(!feature.IsEnabled, "The Free profile feature should be disabled.");
        Equal(0, feature.Load().Profiles.Count);
        Equal(0, store.LoadCallCount);
        Equal(0, feature.GetShortcutBindings(ProfileCatalog.Empty).Count);
        True(
            !feature.TrySave(ProfileCatalog.Empty, out string? errorMessage),
            "The Free profile feature must reject profile saves.");
        Contains("not available", errorMessage ?? string.Empty);
        Equal(0, store.SaveCallCount);
        return Task.CompletedTask;
    }

    private static Task PaidProfileFeatureLoadsAndExposesShortcuts()
    {
        ProfileDefinition profile = new(
            "work",
            "Work",
            true,
            new WidgetShortcut(WidgetHotkeyModifiers.Control, 0x57));
        ProfileCatalog catalog = new(
            ProfileCatalog.CurrentSchemaVersion,
            [profile]);
        TrackingProfileStore store = new(catalog);
        IProfileFeature feature = ProfileFeature.Create(
            ProductConfiguration.Paid,
            store);

        True(feature.IsEnabled, "The Paid profile feature should be enabled.");
        Equal(1, feature.Load().Profiles.Count);
        Equal(1, store.LoadCallCount);
        IReadOnlyList<ProfileShortcutBinding> bindings =
            feature.GetShortcutBindings(catalog);
        Equal(1, bindings.Count);
        Equal("work", bindings[0].ProfileId);
        Equal("Ctrl + W", bindings[0].Shortcut.DisplayText);
        True(
            feature.TrySave(catalog, out string? errorMessage),
            errorMessage ?? "The Paid profile feature should save profiles.");
        Equal(1, store.SaveCallCount);
        return Task.CompletedTask;
    }

    private static Task TransitionPlanMatches(
        DisplayMode? currentMode,
        DisplayMode requestedMode,
        bool canRestoreExtendedLayout,
        params DisplayMode[] expectedModes)
    {
        IReadOnlyList<DisplayMode> actualModes =
            DisplayModeTransitionPlan.Create(
                currentMode,
                requestedMode,
                canRestoreExtendedLayout);

        Equal(string.Join(',', expectedModes), string.Join(',', actualModes));
        return Task.CompletedTask;
    }

    private static Task LiveExtendLayoutCaptureRespectsCurrentTopology()
    {
        DisplayTopologySnapshot snapshot =
            new WindowsDisplayTopologyService().GetSnapshot();
        WindowsExtendedDisplayLayoutRestorer restorer = new();
        bool captured = restorer.TryCaptureCurrent();

        if (DisplayTransitionMonitor.IsModeSatisfied(snapshot, DisplayMode.Extend))
        {
            True(captured, "A complete current Extend layout should be capturable.");
            Equal(0, restorer.ValidateCaptured());
            Console.WriteLine("     exact Extend layout captured and validated without applying");
        }
        else
        {
            True(
                !captured,
                "A non-Extend or incomplete topology must not overwrite the saved layout.");
            Console.WriteLine("     exact layout capture safely skipped outside complete Extend");
        }

        return Task.CompletedTask;
    }

    private static async Task DisplayTransitionReturnsImmediately()
    {
        FakeDisplayTopologyService topology = new(
            Topology(DisplayMode.Extend));
        int delayCount = 0;

        DisplayTopologySnapshot snapshot =
            await DisplayTransitionMonitor.WaitForModeAsync(
                topology,
                DisplayMode.Extend,
                TimeSpan.FromMilliseconds(1),
                4,
                CancellationToken.None,
                (interval, cancellationToken) =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                });

        Equal(DisplayMode.Extend, snapshot.CurrentMode);
        Equal(1, topology.CallCount);
        Equal(0, delayCount);
    }

    private static async Task DisplayTransitionWaitsForExpectedMode()
    {
        FakeDisplayTopologyService topology = new(
            Topology(DisplayMode.Duplicate),
            Topology(null),
            Topology(DisplayMode.Extend));
        int delayCount = 0;

        DisplayTopologySnapshot snapshot =
            await DisplayTransitionMonitor.WaitForModeAsync(
                topology,
                DisplayMode.Extend,
                TimeSpan.FromMilliseconds(1),
                5,
                CancellationToken.None,
                (interval, cancellationToken) =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                });

        Equal(DisplayMode.Extend, snapshot.CurrentMode);
        Equal(3, topology.CallCount);
        Equal(2, delayCount);
    }

    private static async Task DisplayTransitionChecksAreBounded()
    {
        FakeDisplayTopologyService topology = new(
            Topology(DisplayMode.Duplicate));
        int delayCount = 0;

        DisplayTopologySnapshot snapshot =
            await DisplayTransitionMonitor.WaitForModeAsync(
                topology,
                DisplayMode.Extend,
                TimeSpan.FromMilliseconds(1),
                3,
                CancellationToken.None,
                (interval, cancellationToken) =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                });

        Equal(DisplayMode.Duplicate, snapshot.CurrentMode);
        Equal(3, topology.CallCount);
        Equal(2, delayCount);
    }

    private static async Task DisplayTransitionWaitsForStableTopology()
    {
        FakeDisplayTopologyService topology = new(
            Topology(DisplayMode.Extend, activeDisplayCount: 2, availableDisplayCount: 3),
            Topology(DisplayMode.Extend, activeDisplayCount: 3, availableDisplayCount: 3),
            Topology(DisplayMode.Extend, activeDisplayCount: 3, availableDisplayCount: 3),
            Topology(DisplayMode.Extend, activeDisplayCount: 3, availableDisplayCount: 3));
        int delayCount = 0;

        DisplayTopologySnapshot snapshot =
            await DisplayTransitionMonitor.WaitForModeAsync(
                topology,
                DisplayMode.Extend,
                TimeSpan.FromMilliseconds(1),
                maximumAttempts: 6,
                CancellationToken.None,
                (interval, cancellationToken) =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                },
                requiredStableSnapshots: 3);

        Equal(DisplayMode.Extend, snapshot.CurrentMode);
        Equal(3, snapshot.ActiveDisplayCount);
        Equal(4, topology.CallCount);
        Equal(3, delayCount);
    }

    private static async Task PartialExtendTopologyRemainsUnsettled()
    {
        FakeDisplayTopologyService topology = new(
            Topology(DisplayMode.Extend, activeDisplayCount: 2, availableDisplayCount: 3));
        int delayCount = 0;

        DisplayTopologySnapshot snapshot =
            await DisplayTransitionMonitor.WaitForModeAsync(
                topology,
                DisplayMode.Extend,
                TimeSpan.FromMilliseconds(1),
                maximumAttempts: 4,
                CancellationToken.None,
                (interval, cancellationToken) =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                },
                requiredStableSnapshots: 3);

        True(
            !DisplayTransitionMonitor.IsModeSatisfied(snapshot, DisplayMode.Extend),
            "Extend should remain unsettled while an available display is inactive.");
        Equal(4, topology.CallCount);
        Equal(3, delayCount);
    }

    private static async Task AcceptedDisplayRequestMustSettle()
    {
        FakeDisplayModeService display = new(canRestoreExtendedLayout: true);
        FakeDisplayTopologyService topology = new(
            Topology(DisplayMode.SecondScreenOnly));

        DisplayModeTransitionOutcome outcome =
            await DisplayModeTransitionExecutor.ExecuteAsync(
                display,
                topology,
                DisplayMode.SecondScreenOnly,
                DisplayMode.Extend,
                TimeSpan.Zero,
                bridgeMaximumAttempts: 2,
                finalMaximumAttempts: 2,
                requiredStableSnapshots: 1,
                recoveryApplyCount: 1,
                CancellationToken.None,
                (_, _) => Task.CompletedTask);

        True(
            !outcome.Succeeded,
            "An accepted request must not succeed while the topology remains external-only.");
        Equal(DisplayMode.SecondScreenOnly, outcome.Snapshot.CurrentMode);
        Equal(2, outcome.ApplyCount);
        Equal(2, display.AppliedModes.Count);
        True(
            outcome.Message.Contains("full-screen app", StringComparison.Ordinal),
            "The failure should explain the likely external override.");
    }

    private static async Task DisplayTransitionRetriesExternalOverride()
    {
        FakeDisplayModeService display = new(canRestoreExtendedLayout: true);
        FakeDisplayTopologyService topology = new(
            Topology(DisplayMode.SecondScreenOnly),
            Topology(DisplayMode.Extend));

        DisplayModeTransitionOutcome outcome =
            await DisplayModeTransitionExecutor.ExecuteAsync(
                display,
                topology,
                DisplayMode.SecondScreenOnly,
                DisplayMode.Extend,
                TimeSpan.Zero,
                bridgeMaximumAttempts: 1,
                finalMaximumAttempts: 1,
                requiredStableSnapshots: 1,
                recoveryApplyCount: 1,
                CancellationToken.None,
                (_, _) => Task.CompletedTask);

        True(outcome.Succeeded, "The bounded recovery request should be verified.");
        Equal(DisplayMode.Extend, outcome.Snapshot.CurrentMode);
        Equal(2, outcome.ApplyCount);
        Equal("Extend,Extend", string.Join(',', display.AppliedModes));
    }

    private static async Task FailedDisplayBridgeStopsTransition()
    {
        FakeDisplayModeService display = new(canRestoreExtendedLayout: false);
        FakeDisplayTopologyService topology = new(
            Topology(DisplayMode.SecondScreenOnly));

        DisplayModeTransitionOutcome outcome =
            await DisplayModeTransitionExecutor.ExecuteAsync(
                display,
                topology,
                DisplayMode.SecondScreenOnly,
                DisplayMode.Extend,
                TimeSpan.Zero,
                bridgeMaximumAttempts: 2,
                finalMaximumAttempts: 2,
                requiredStableSnapshots: 1,
                recoveryApplyCount: 1,
                CancellationToken.None,
                (_, _) => Task.CompletedTask);

        True(!outcome.Succeeded, "A bridge that never settles must stop the transition.");
        Equal(1, outcome.ApplyCount);
        Equal("PcScreenOnly", string.Join(',', display.AppliedModes));
    }

    private static Task AudioEndpointLabelsUseFriendlyName()
    {
        AudioEndpointInfo endpoint = new(
            "test-id",
            "USB Headset",
            AudioEndpointKind.Playback,
            IsConsoleDefault: true,
            IsMultimediaDefault: true,
            IsCommunicationsDefault: true);

        Equal("USB Headset", endpoint.DisplayLabel);
        return Task.CompletedTask;
    }

    private static Task AudioSessionVolumeIsFormatted()
    {
        AudioSessionInfo normal = new("1", "Player", "Speakers", 0.426f, false);
        AudioSessionInfo high = new("2", "Player", "Speakers", 1.5f, true);

        Equal("43%", normal.VolumeLabel);
        Equal("100%", high.VolumeLabel);
        Equal("Yes", high.MuteLabel);
        return Task.CompletedTask;
    }

    private static Task AudioEndpointRolesAreClear()
    {
        AudioEndpointInfo available = new(
            "1",
            "Speakers",
            AudioEndpointKind.Playback,
            false,
            false,
            false);
        AudioEndpointInfo normal = available with { IsConsoleDefault = true };
        AudioEndpointInfo calls = available with { IsCommunicationsDefault = true };
        AudioEndpointInfo both = normal with { IsCommunicationsDefault = true };

        Equal("Available audio device", available.ActiveRoleDescription);
        Equal("Default audio device", normal.ActiveRoleDescription);
        Equal("Calls device", calls.ActiveRoleDescription);
        Equal("Default audio and calls device", both.ActiveRoleDescription);
        return Task.CompletedTask;
    }

    private static async Task AudioRefreshNotificationsAreDebounced()
    {
        int callCount = 0;
        TaskCompletionSource actionCompleted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using DebouncedActionScheduler scheduler = new(
            TimeSpan.FromMilliseconds(40),
            cancellationToken =>
            {
                Interlocked.Increment(ref callCount);
                actionCompleted.TrySetResult();
                return Task.CompletedTask;
            });

        scheduler.Schedule();
        scheduler.Schedule();
        scheduler.Schedule();

        await actionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(80);

        Equal(1, callCount);
    }

    private static async Task DisposedAudioDebounceCancelsWork()
    {
        int callCount = 0;
        DebouncedActionScheduler scheduler = new(
            TimeSpan.FromMilliseconds(80),
            cancellationToken =>
            {
                Interlocked.Increment(ref callCount);
                return Task.CompletedTask;
            });

        scheduler.Schedule();
        scheduler.Dispose();
        scheduler.Schedule();

        await Task.Delay(140);
        Equal(0, callCount);
    }

    private static async Task SessionVolumeDelegatesRequestedLevel()
    {
        FakeAudioSessionMutationBackend backend = new();
        WindowsAudioSessionControlService service = new(backend);

        AudioControlResult result = await service.SetVolumeAsync("session-1", 0.42f);

        True(result.Succeeded, result.Message);
        Equal("session-1", backend.SessionId);
        Equal(0.42f, backend.Volume);
        Equal(1, backend.VolumeCallCount);
    }

    private static async Task InvalidSessionVolumeIsRejected()
    {
        FakeAudioSessionMutationBackend backend = new();
        WindowsAudioSessionControlService service = new(backend);

        AudioControlResult result = await service.SetVolumeAsync("session-1", 1.01f);

        True(!result.Succeeded, "Out-of-range volume should fail.");
        Equal(0, backend.VolumeCallCount);
    }

    private static async Task SessionMuteDelegatesRequestedState()
    {
        FakeAudioSessionMutationBackend backend = new();
        WindowsAudioSessionControlService service = new(backend);

        AudioControlResult result = await service.SetMuteAsync("session-2", true);

        True(result.Succeeded, result.Message);
        Equal("session-2", backend.SessionId);
        True(backend.IsMuted, "Mute should be delegated as true.");
        Equal(1, backend.MuteCallCount);
    }

    private static async Task EndpointVolumeControlsValidateAndDelegate()
    {
        FakeAudioEndpointVolumeBackend backend = new()
        {
            Snapshot = new AudioEndpointControlSnapshot(0.6f, false),
        };
        WindowsAudioEndpointControlService service = new(backend);

        AudioEndpointControlSnapshot? state = await service.GetStateAsync(
            "endpoint-1");
        Equal(0.6f, state?.MasterVolume);
        Equal(false, state?.IsMuted);

        AudioControlResult volumeResult =
            await service.SetMasterVolumeAsync(
                "endpoint-1",
                "USB headset",
                0.42f);
        AudioControlResult muteResult = await service.SetMuteAsync(
            "endpoint-1",
            "USB headset",
            true);

        True(volumeResult.Succeeded, volumeResult.Message);
        True(muteResult.Succeeded, muteResult.Message);
        Equal(0.42f, backend.Volume);
        True(backend.IsMuted, "The endpoint mute state should be delegated.");
        Equal(1, backend.VolumeCallCount);
        Equal(1, backend.MuteCallCount);

        AudioControlResult invalidResult =
            await service.SetMasterVolumeAsync(
                "endpoint-1",
                "USB headset",
                1.1f);
        True(!invalidResult.Succeeded, "Out-of-range master volume should fail.");
        Equal(1, backend.VolumeCallCount);
    }

    private static async Task GeneralEndpointSelectionUpdatesBothRoles()
    {
        FakeDefaultAudioEndpointSetter setter = new();
        FakeWindowsSettingsLauncher settings = new();
        WindowsDefaultAudioEndpointService service = new(setter, settings);

        AudioControlResult result = await service.SetDefaultAsync(
            "endpoint-1",
            "USB Headset",
            AudioDefaultRoleSelection.General);

        True(result.Succeeded, result.Message);
        Equal(2, setter.Calls.Count);
        Equal(("endpoint-1", AudioRole.Console), setter.Calls[0]);
        Equal(("endpoint-1", AudioRole.Multimedia), setter.Calls[1]);
        Equal(0, settings.Uris.Count);
    }

    private static async Task CommunicationsEndpointSelectionUpdatesCalls()
    {
        FakeDefaultAudioEndpointSetter setter = new();
        WindowsDefaultAudioEndpointService service = new(
            setter,
            new FakeWindowsSettingsLauncher());

        AudioControlResult result = await service.SetDefaultAsync(
            "endpoint-2",
            "Desk microphone",
            AudioDefaultRoleSelection.Communications);

        True(result.Succeeded, result.Message);
        Equal(1, setter.Calls.Count);
        Equal(("endpoint-2", AudioRole.Communications), setter.Calls[0]);
    }

    private static async Task BothEndpointSelectionUpdatesAllRoles()
    {
        FakeDefaultAudioEndpointSetter setter = new();
        WindowsDefaultAudioEndpointService service = new(
            setter,
            new FakeWindowsSettingsLauncher());

        AudioControlResult result = await service.SetDefaultAsync(
            "endpoint-3",
            "Dock headset",
            AudioDefaultRoleSelection.Both);

        True(result.Succeeded, result.Message);
        Equal(3, setter.Calls.Count);
        Equal(("endpoint-3", AudioRole.Console), setter.Calls[0]);
        Equal(("endpoint-3", AudioRole.Multimedia), setter.Calls[1]);
        Equal(("endpoint-3", AudioRole.Communications), setter.Calls[2]);
    }

    private static async Task UnsupportedEndpointSelectionPreservesFallback()
    {
        FakeDefaultAudioEndpointSetter setter = new()
        {
            Exception = new InvalidOperationException("unsupported"),
        };
        FakeWindowsSettingsLauncher settings = new();
        WindowsDefaultAudioEndpointService service = new(setter, settings);

        AudioControlResult result = await service.SetDefaultAsync(
            "endpoint-3",
            "Speakers",
            AudioDefaultRoleSelection.General);
        AudioControlResult fallback = service.OpenSoundSettings();

        True(!result.Succeeded, "Unsupported policy calls should fail clearly.");
        True(fallback.Succeeded, fallback.Message);
        Equal(1, settings.Uris.Count);
        Equal("ms-settings:sound", settings.Uris[0]);
    }

    private static Task VolumeMixerUsesExactSettingsUri()
    {
        FakeWindowsSettingsLauncher settings = new();
        WindowsDefaultAudioEndpointService service = new(
            new FakeDefaultAudioEndpointSetter(),
            settings);

        AudioControlResult result = service.OpenVolumeMixerSettings();

        True(result.Succeeded, result.Message);
        Equal(1, settings.Uris.Count);
        Equal("ms-settings:apps-volume", settings.Uris[0]);
        return Task.CompletedTask;
    }

    private static Task PhysicalDeviceInterfacesAreGrouped()
    {
        Guid containerId = Guid.NewGuid();

        IReadOnlyList<ConnectedDeviceInfo> devices = ConnectedDeviceClassifier.Classify(
        [
            PnpDevice(
                "USB\\COMPOSITE",
                containerId,
                "USB Composite Device",
                "USB",
                "USB"),
            PnpDevice(
                "USB\\KEYBOARD",
                containerId,
                "Ergo Keyboard",
                "Keyboard",
                "USB"),
        ]);

        Equal(1, devices.Count);
        Equal("Ergo Keyboard", devices[0].Name);
        Equal("Keyboard", devices[0].Category);
        Equal(DeviceTransport.Wired, devices[0].Transport);
        return Task.CompletedTask;
    }

    private static Task BluetoothTakesPrecedence()
    {
        Guid containerId = Guid.NewGuid();

        IReadOnlyList<ConnectedDeviceInfo> devices = ConnectedDeviceClassifier.Classify(
        [
            PnpDevice(
                "USB\\DONGLE",
                containerId,
                "USB Composite Device",
                "USB",
                "USB"),
            PnpDevice(
                "BTHENUM\\HEADSET",
                containerId,
                "Andrew Headphones",
                "AudioEndpoint",
                "BTHENUM"),
        ]);

        Equal(1, devices.Count);
        Equal("Andrew Headphones", devices[0].Name);
        Equal(DeviceTransport.Bluetooth, devices[0].Transport);
        Equal("Audio", devices[0].Category);
        return Task.CompletedTask;
    }

    private static Task GenericDeviceInfrastructureIsHidden()
    {
        IReadOnlyList<ConnectedDeviceInfo> devices = ConnectedDeviceClassifier.Classify(
        [
            PnpDevice(
                "USB\\ROOT_HUB",
                Guid.NewGuid(),
                "USB Root Hub (USB 3.0)",
                "USB",
                "USB"),
            PnpDevice(
                "BTH\\ENUMERATOR",
                Guid.NewGuid(),
                "Microsoft Bluetooth Enumerator",
                "Bluetooth",
                "BTH"),
            PnpDevice(
                "BTHENUM\\SERVICE",
                Guid.NewGuid(),
                "Device Information Service",
                "Bluetooth",
                "BTHENUM"),
            PnpDevice(
                "USB\\KEYBOARD",
                Guid.NewGuid(),
                "HID Keyboard Device",
                "Keyboard",
                "USB"),
        ]);

        Equal(0, devices.Count);
        return Task.CompletedTask;
    }

    private static Task BluetoothProfilesCollapse()
    {
        IReadOnlyList<ConnectedDeviceInfo> devices = ConnectedDeviceClassifier.Classify(
        [
            PnpDevice(
                "BTHENUM\\HEADSET",
                Guid.NewGuid(),
                "LE_WH-1000XM4",
                "Bluetooth",
                "BTHENUM"),
            PnpDevice(
                "BTHENUM\\AUDIO",
                Guid.NewGuid(),
                "WH-1000XM4 Hands-Free AG Audio",
                "AudioEndpoint",
                "BTHENUM"),
        ]);

        Equal(1, devices.Count);
        Equal("WH-1000XM4", devices[0].Name);
        return Task.CompletedTask;
    }

    private static Task UnrelatedUsbDevicesRemainSeparate()
    {
        IReadOnlyList<ConnectedDeviceInfo> devices = ConnectedDeviceClassifier.Classify(
        [
            PnpDevice(
                "USB\\CAMERA",
                Guid.NewGuid(),
                "Conference Camera",
                "Camera",
                "USB"),
            PnpDevice(
                "USB\\DRIVE",
                Guid.NewGuid(),
                "Backup Drive",
                "DiskDrive",
                "USB"),
        ]);

        Equal(2, devices.Count);
        True(
            devices.Select(device => device.Name).Contains("Conference Camera"),
            "The camera should remain visible.");
        True(
            devices.Select(device => device.Name).Contains("Backup Drive"),
            "The drive should remain visible.");
        return Task.CompletedTask;
    }

    private static Task DeviceStatusLabelsAreHumanReadable()
    {
        ConnectedDeviceInfo connected = new(
            "1",
            "Device",
            "USB device",
            DeviceTransport.Wired,
            true,
            0);
        ConnectedDeviceInfo present = connected with { IsStarted = false };
        ConnectedDeviceInfo problem = connected with { ProblemCode = 22 };

        Equal("Connected", connected.StatusLabel);
        Equal("Present", present.StatusLabel);
        Equal("Needs attention", problem.StatusLabel);
        return Task.CompletedTask;
    }

    private static Task DeviceSettingsShortcutsUseExactUris()
    {
        FakeDeviceSettingsLauncher launcher = new();
        WindowsDeviceSettingsService service = new(launcher);

        DeviceActionResult bluetooth = service.OpenBluetoothSettings();
        DeviceActionResult devices = service.OpenConnectedDevicesSettings();
        DeviceActionResult wifi = service.OpenWiFiSettings();
        DeviceActionResult network = service.OpenNetworkSettings();
        DeviceActionResult airplane = service.OpenAirplaneModeSettings();

        True(bluetooth.Succeeded, bluetooth.Message);
        True(devices.Succeeded, devices.Message);
        True(wifi.Succeeded, wifi.Message);
        True(network.Succeeded, network.Message);
        True(airplane.Succeeded, airplane.Message);
        Equal(5, launcher.Uris.Count);
        Equal("ms-settings:bluetooth", launcher.Uris[0]);
        Equal("ms-settings:connecteddevices", launcher.Uris[1]);
        Equal("ms-settings:network-wifi", launcher.Uris[2]);
        Equal("ms-settings:network-status", launcher.Uris[3]);
        Equal("ms-settings:network-airplanemode", launcher.Uris[4]);
        return Task.CompletedTask;
    }

    private static Task TaskbarStateMapsAutoHideFlag()
    {
        FakeTaskbarBackend backend = new()
        {
            State = WindowsTaskbarService.AutoHideState,
        };
        WindowsTaskbarService service = new(
            backend,
            new FakeTaskbarSettingsLauncher());

        Equal(TaskbarState.AutoHidden, service.GetSnapshot().State);

        backend.State = 0;
        Equal(TaskbarState.Visible, service.GetSnapshot().State);
        return Task.CompletedTask;
    }

    private static Task TaskbarControlsDelegateState()
    {
        FakeTaskbarBackend backend = new();
        WindowsTaskbarService service = new(
            backend,
            new FakeTaskbarSettingsLauncher());

        TaskbarActionResult hide = service.SetAutoHide(true);
        TaskbarActionResult show = service.SetAutoHide(false);

        True(hide.Succeeded, hide.Message);
        True(show.Succeeded, show.Message);
        Equal(2, backend.SetCalls.Count);
        Equal(WindowsTaskbarService.AutoHideState, backend.SetCalls[0]);
        Equal(0u, backend.SetCalls[1]);
        return Task.CompletedTask;
    }

    private static Task TaskbarSettingsShortcutsUseExactUris()
    {
        FakeTaskbarSettingsLauncher launcher = new();
        WindowsTaskbarService service = new(
            new FakeTaskbarBackend(),
            launcher);

        TaskbarActionResult taskbar = service.OpenTaskbarSettings();
        TaskbarActionResult display = service.OpenDisplaySettings();
        TaskbarActionResult notifications = service.OpenNotificationSettings();

        True(taskbar.Succeeded, taskbar.Message);
        True(display.Succeeded, display.Message);
        True(notifications.Succeeded, notifications.Message);
        Equal(3, launcher.Uris.Count);
        Equal("ms-settings:taskbar", launcher.Uris[0]);
        Equal("ms-settings:display", launcher.Uris[1]);
        Equal("ms-settings:notifications", launcher.Uris[2]);
        return Task.CompletedTask;
    }

    private static Task TaskbarApiFailuresRemainNonfatal()
    {
        FakeTaskbarBackend backend = new()
        {
            Exception = new InvalidOperationException("shell unavailable"),
        };
        WindowsTaskbarService service = new(
            backend,
            new FakeTaskbarSettingsLauncher());

        TaskbarSnapshot snapshot = service.GetSnapshot();
        TaskbarActionResult result = service.SetAutoHide(true);

        Equal(TaskbarState.Unavailable, snapshot.State);
        True(!result.Succeeded, "Taskbar API failure should become a result.");
        True(
            result.Message.Contains("shell unavailable", StringComparison.Ordinal),
            result.Message);
        return Task.CompletedTask;
    }

    private static async Task WirelessRadioStatesRemainIndependent()
    {
        FakeWirelessRadioBackend backend = new()
        {
            Radios =
            [
                new WirelessRadioDevice(
                    WirelessRadioKind.WiFi,
                    WirelessRadioState.On),
                new WirelessRadioDevice(
                    WirelessRadioKind.Bluetooth,
                    WirelessRadioState.Off),
            ],
        };
        WindowsWirelessRadioService service = new(backend);

        WirelessRadioSnapshot snapshot = await service.GetSnapshotAsync();

        Equal(WirelessRadioState.On, snapshot.WiFi);
        Equal(WirelessRadioState.Off, snapshot.Bluetooth);
    }

    private static async Task WirelessRadioControlDelegatesState()
    {
        FakeWirelessRadioBackend backend = new();
        WindowsWirelessRadioService service = new(backend);

        WirelessRadioResult result = await service.SetStateAsync(
            WirelessRadioKind.Bluetooth,
            true);

        True(result.Succeeded, result.Message);
        Equal(1, backend.SetCalls.Count);
        Equal((WirelessRadioKind.Bluetooth, true), backend.SetCalls[0]);
    }

    private static async Task DeniedWirelessRadioControlFailsClearly()
    {
        FakeWirelessRadioBackend backend = new()
        {
            ControlStatus = WirelessRadioControlStatus.DeniedBySystem,
        };
        WindowsWirelessRadioService service = new(backend);

        WirelessRadioResult result = await service.SetStateAsync(
            WirelessRadioKind.WiFi,
            false);

        True(!result.Succeeded, "Denied radio control should fail.");
        True(
            result.Message.Contains("policy", StringComparison.OrdinalIgnoreCase),
            result.Message);
    }

    private static async Task WirelessRadioApiFailuresRemainNonfatal()
    {
        FakeWirelessRadioBackend backend = new()
        {
            Exception = new UnauthorizedAccessException("radio access"),
        };
        WindowsWirelessRadioService service = new(backend);

        WirelessRadioResult result = await service.SetStateAsync(
            WirelessRadioKind.Bluetooth,
            false);

        True(!result.Succeeded, "A radio API exception should become a failure result.");
        True(
            result.Message.Contains("radio access", StringComparison.Ordinal),
            result.Message);
    }

    private static Task DarkTitleBarUsesAppPalette()
    {
        FakeDwmAttributeSetter setter = new();
        IntPtr windowHandle = new(42);

        WindowsWindowTheme.ApplyDarkTitleBar(windowHandle, setter);

        Equal(4, setter.Calls.Count);
        Equal(
            (windowHandle, WindowsWindowTheme.UseImmersiveDarkMode, 1),
            setter.Calls[0]);
        Equal(
            (
                windowHandle,
                WindowsWindowTheme.BorderColor,
                WindowsWindowTheme.ToColorReference(0x2A, 0x30, 0x3A)),
            setter.Calls[1]);
        Equal(
            (
                windowHandle,
                WindowsWindowTheme.CaptionColor,
                WindowsWindowTheme.ToColorReference(0x10, 0x13, 0x18)),
            setter.Calls[2]);
        Equal(
            (
                windowHandle,
                WindowsWindowTheme.TextColor,
                WindowsWindowTheme.ToColorReference(0xF3, 0xF5, 0xF7)),
            setter.Calls[3]);
        Equal(1, setter.RefreshCount);

        FakeDwmAttributeSetter zeroHandleSetter = new();
        WindowsWindowTheme.ApplyDarkTitleBar(IntPtr.Zero, zeroHandleSetter);
        Equal(0, zeroHandleSetter.Calls.Count);
        return Task.CompletedTask;
    }

    private static Task HighContrastPaletteUsesSystemColors()
    {
        IReadOnlyDictionary<string, Color> palette =
            WidgetTheme.ResolvePalette(useDarkTheme: true, useHighContrast: true);
        IReadOnlyDictionary<string, Color> lightPreferencePalette =
            WidgetTheme.ResolvePalette(useDarkTheme: false, useHighContrast: true);

        Equal(SystemColors.WindowColor, palette["PageBrush"]);
        Equal(SystemColors.WindowTextColor, palette["PrimaryTextBrush"]);
        Equal(SystemColors.HighlightColor, palette["SelectionBrush"]);
        Equal(SystemColors.HighlightTextColor, palette["SelectionTextBrush"]);
        Equal(SystemColors.GrayTextColor, palette["DisabledTextBrush"]);
        Equal(SystemColors.WindowColor, palette["OverlayBackdropBrush"]);
        Equal(palette["PageBrush"], lightPreferencePalette["PageBrush"]);
        Equal(palette["PrimaryTextBrush"], lightPreferencePalette["PrimaryTextBrush"]);
        return Task.CompletedTask;
    }

    private static Task LightTitleBarUsesAppPalette()
    {
        FakeDwmAttributeSetter setter = new();
        IntPtr windowHandle = new(43);

        WindowsWindowTheme.Apply(
            windowHandle,
            false,
            Color.FromRgb(0xF4, 0xF6, 0xF8),
            Color.FromRgb(0xCC, 0xD3, 0xDC),
            Color.FromRgb(0x1B, 0x1F, 0x24),
            setter);

        Equal(4, setter.Calls.Count);
        Equal(
            (windowHandle, WindowsWindowTheme.UseImmersiveDarkMode, 0),
            setter.Calls[0]);
        Equal(
            (
                windowHandle,
                WindowsWindowTheme.BorderColor,
                WindowsWindowTheme.ToColorReference(0xCC, 0xD3, 0xDC)),
            setter.Calls[1]);
        Equal(
            (
                windowHandle,
                WindowsWindowTheme.CaptionColor,
                WindowsWindowTheme.ToColorReference(0xF4, 0xF6, 0xF8)),
            setter.Calls[2]);
        Equal(
            (
                windowHandle,
                WindowsWindowTheme.TextColor,
                WindowsWindowTheme.ToColorReference(0x1B, 0x1F, 0x24)),
            setter.Calls[3]);
        Equal(1, setter.RefreshCount);
        return Task.CompletedTask;
    }

    private static Task WidgetShortcutsValidateAndFormat()
    {
        bool shiftOnly = WidgetShortcut.TryCreate(
            WidgetHotkeyModifiers.Shift,
            0x51,
            out WidgetShortcut? invalid);
        bool valid = WidgetShortcut.TryCreate(
            WidgetHotkeyModifiers.Win | WidgetHotkeyModifiers.Shift,
            0x51,
            out WidgetShortcut? shortcut);
        bool functionKey = WidgetShortcut.TryCreate(
            WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
            0x7B,
            out WidgetShortcut? f12);
        bool reservedNavigation = WidgetShortcut.TryCreate(
            WidgetHotkeyModifiers.Control,
            0x31,
            out WidgetShortcut? ctrlOne);

        True(!shiftOnly, "A Shift-only system shortcut should be rejected.");
        Equal<WidgetShortcut?>(null, invalid);
        True(valid, "Win + Shift + Q should be accepted.");
        Equal("Win + Shift + Q", shortcut!.DisplayText);
        True(functionKey, "Modified function keys should be accepted.");
        Equal("Ctrl + Alt + F12", f12!.DisplayText);
        True(
            !reservedNavigation,
            "Ctrl + 0 through Ctrl + 6 are reserved for overlay navigation.");
        Equal<WidgetShortcut?>(null, ctrlOne);
        return Task.CompletedTask;
    }

    private static Task WidgetSettingsRemoveDuplicates()
    {
        WidgetShortcut duplicate = new(
            WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
            0x41);
        WidgetSettings settings = WidgetSettings.Default with
        {
            Display = duplicate,
            Audio = duplicate,
        };

        WidgetSettings normalized = settings.Normalize();

        Equal(duplicate, normalized.Display);
        Equal<WidgetShortcut?>(null, normalized.Audio);
        True(
            normalized.IsShortcutUsedByAnotherAction(
                WidgetHotkeyAction.Audio,
                duplicate),
            "The surviving Display shortcut should be reported as in use.");
        return Task.CompletedTask;
    }

    private static Task DisplayAndFavoriteShortcutsMapToActions()
    {
        WidgetShortcut displayShortcut = new(
            WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
            0x31);
        WidgetShortcut favoriteShortcut = new(
            WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
            0x32);
        WidgetSettings settings = WidgetSettings.Default with
        {
            Extend = displayShortcut,
            FavoriteOutput2 = new(
                "endpoint-2",
                "Desk speakers",
                favoriteShortcut),
        };

        Equal(
            displayShortcut,
            settings.GetShortcut(WidgetHotkeyAction.Extend));
        Equal(
            favoriteShortcut,
            settings.GetShortcut(WidgetHotkeyAction.FavoriteOutput2));
        Equal(1, settings.FindFavoriteSlot("endpoint-2"));
        True(
            WidgetSettings.TryGetFavoriteSlot(
                WidgetHotkeyAction.FavoriteOutput2,
                out int slot),
            "The favorite action should resolve to a slot.");
        Equal(1, slot);
        return Task.CompletedTask;
    }

    private static Task FavoriteOutputsNormalizeDevices()
    {
        WidgetSettings settings = WidgetSettings.Default with
        {
            FavoriteOutput1 = new("endpoint-1", "Speakers", null),
            FavoriteOutput2 = new("endpoint-1", "Duplicate speakers", null),
            FavoriteOutput3 = new("", "Missing identifier", null),
            FavoriteOutput4 = new("endpoint-4", "", null),
        };

        WidgetSettings normalized = settings.Normalize();

        Equal("Speakers", normalized.FavoriteOutput1?.Name);
        Equal<FavoriteOutputSetting?>(null, normalized.FavoriteOutput2);
        Equal<FavoriteOutputSetting?>(null, normalized.FavoriteOutput3);
        Equal<FavoriteOutputSetting?>(null, normalized.FavoriteOutput4);
        Equal(1, normalized.FindOpenFavoriteSlot());
        return Task.CompletedTask;
    }

    private static Task FavoriteInputsNormalizeAndMapActions()
    {
        WidgetShortcut shortcut = new(
            WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
            0x49);
        WidgetSettings settings = WidgetSettings.Default with
        {
            FavoriteInput1 = new(
                "mic-1",
                "  Desk microphone  ",
                shortcut,
                FavoriteEndpointRole.Communications,
                new string('m', WidgetSettings.MaximumFavoriteAliasLength + 5)),
            FavoriteInput2 = new("mic-1", "Duplicate microphone", null),
            FavoriteInput3 = new("", "Missing identifier", null),
            FavoriteInput4 = new("mic-4", "", null),
        };

        WidgetSettings normalized = settings.Normalize();

        Equal("Desk microphone", normalized.FavoriteInput1?.Name);
        Equal(FavoriteEndpointRole.Communications, normalized.FavoriteInput1?.Role);
        Equal(
            WidgetSettings.MaximumFavoriteAliasLength,
            normalized.FavoriteInput1?.Alias?.Length);
        Equal<FavoriteInputSetting?>(null, normalized.FavoriteInput2);
        Equal<FavoriteInputSetting?>(null, normalized.FavoriteInput3);
        Equal<FavoriteInputSetting?>(null, normalized.FavoriteInput4);
        Equal(1, normalized.FindOpenInputFavoriteSlot());
        Equal(
            shortcut,
            normalized.GetShortcut(WidgetHotkeyAction.FavoriteInput1));
        True(
            WidgetSettings.TryGetInputFavoriteSlot(
                WidgetHotkeyAction.FavoriteInput1,
                out int slot),
            "The favorite-input action should resolve to a slot.");
        Equal(0, slot);
        return Task.CompletedTask;
    }

    private static Task ResetShortcutsPreservesFavorites()
    {
        WidgetShortcut shortcut = new(
            WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
            0x33);
        WidgetSettings settings = WidgetSettings.Default with
        {
            Duplicate = shortcut,
            FavoriteOutput1 = new("endpoint-1", "Headphones", shortcut),
            FavoriteInput1 = new("mic-1", "Desk microphone", shortcut),
            OverlayEnabled = true,
            OverlayDimmingOpacity = 0.4,
            OverlaySidebarWidth = 480,
            ToggleOverlay = shortcut,
        };

        WidgetSettings reset = settings.ResetShortcuts();

        Equal(WidgetSettings.Default.ToggleWidget, reset.ToggleWidget);
        Equal<WidgetShortcut?>(null, reset.Duplicate);
        Equal("endpoint-1", reset.FavoriteOutput1?.EndpointId);
        Equal<WidgetShortcut?>(null, reset.FavoriteOutput1?.Shortcut);
        Equal("mic-1", reset.FavoriteInput1?.EndpointId);
        Equal<WidgetShortcut?>(null, reset.FavoriteInput1?.Shortcut);
        True(reset.OverlayEnabled, "Resetting shortcuts must not disable the overlay.");
        Equal(0.4, reset.OverlayDimmingOpacity);
        Equal(480d, reset.OverlaySidebarWidth);
        Equal<WidgetShortcut?>(null, reset.ToggleOverlay);
        return Task.CompletedTask;
    }

    private static Task WidgetSettingsPersist()
    {
        string settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"WinQuickSwitch.Tests.{Guid.NewGuid():N}.json");

        try
        {
            JsonWidgetSettingsStore store = new(settingsPath);
            WidgetSettings expected = WidgetSettings.Default with
            {
                UseDarkTheme = false,
                Audio = new WidgetShortcut(
                    WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
                    0x41),
                Extend = new WidgetShortcut(
                    WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
                    0x45),
                WindowPinned = true,
                FavoriteOutput1 = new FavoriteOutputSetting(
                    "endpoint-1",
                    "Desk speakers",
                    new WidgetShortcut(
                        WidgetHotkeyModifiers.Control |
                        WidgetHotkeyModifiers.Alt,
                        0x31)),
            };

            True(
                store.TrySave(expected, out string? saveError),
                saveError ?? "Settings save failed.");
            Equal(expected, store.Load());
        }
        finally
        {
            File.Delete(settingsPath);
            File.Delete(settingsPath + ".tmp");
        }

        return Task.CompletedTask;
    }

    private static Task ExpandedFixedShortcutsPersistAndReset()
    {
        string settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"WinQuickSwitch.Tests.{Guid.NewGuid():N}.json");
        WidgetHotkeyAction[] actions =
        [
            WidgetHotkeyAction.Options,
            WidgetHotkeyAction.ToggleWiFi,
            WidgetHotkeyAction.ToggleBluetooth,
            WidgetHotkeyAction.ToggleTaskbarAutoHide,
            WidgetHotkeyAction.RefreshDisplay,
            WidgetHotkeyAction.RefreshAudio,
            WidgetHotkeyAction.RefreshDevices,
            WidgetHotkeyAction.RefreshAll,
        ];

        try
        {
            WidgetSettings expected = WidgetSettings.Default;
            for (int index = 0; index < actions.Length; index++)
            {
                expected = expected.WithShortcut(
                    actions[index],
                    new WidgetShortcut(
                        WidgetHotkeyModifiers.Control |
                        WidgetHotkeyModifiers.Alt,
                        0x41 + index));
            }

            JsonWidgetSettingsStore store = new(settingsPath);
            True(
                store.TrySave(expected, out string? saveError),
                saveError ?? "Expanded shortcut settings save failed.");
            WidgetSettings loaded = store.Load();
            foreach (WidgetHotkeyAction action in actions)
            {
                Equal(expected.GetShortcut(action), loaded.GetShortcut(action));
                Equal<WidgetShortcut?>(
                    null,
                    loaded.ResetShortcuts().GetShortcut(action));
            }
        }
        finally
        {
            File.Delete(settingsPath);
            File.Delete(settingsPath + ".tmp");
        }

        return Task.CompletedTask;
    }

    private static Task OlderWidgetSettingsMigrate()
    {
        string settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"WinQuickSwitch.Tests.{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(
                settingsPath,
                """
                {
                  "UseDarkTheme": false,
                  "ToggleWidget": {
                    "Modifiers": 12,
                    "VirtualKey": 81
                  },
                  "Display": null,
                  "Audio": null,
                  "Devices": null
                }
                """);

            WidgetSettings loaded =
                new JsonWidgetSettingsStore(settingsPath).Load();

            True(!loaded.UseDarkTheme, "The existing theme should be retained.");
            Equal("Win + Shift + Q", loaded.ToggleWidget?.DisplayText);
            Equal<WidgetShortcut?>(null, loaded.Extend);
            Equal<FavoriteOutputSetting?>(null, loaded.FavoriteOutput1);
            True(
                !loaded.OverlayEnabled,
                "Older settings must keep the new overlay disabled by default.");
            True(
                !loaded.WindowPinned,
                "Older settings must keep the normal window unpinned by default.");
            Equal(
                WidgetSettings.DefaultOverlayDimmingOpacity,
                loaded.OverlayDimmingOpacity);
            Equal(
                WidgetSettings.DefaultOverlaySidebarWidth,
                loaded.OverlaySidebarWidth);
            Equal<WidgetShortcut?>(null, loaded.ToggleOverlay);
        }
        finally
        {
            File.Delete(settingsPath);
            File.Delete(settingsPath + ".tmp");
        }

        return Task.CompletedTask;
    }

    private static Task OverlaySettingsDefaultAndNormalize()
    {
        WidgetSettings defaults = WidgetSettings.Default;

        True(
            !defaults.OverlayEnabled,
            "The overlay must be disabled for existing and new installations.");
        Equal(
            WidgetSettings.DefaultOverlayDimmingOpacity,
            defaults.OverlayDimmingOpacity);
        Equal(
            WidgetSettings.DefaultOverlaySidebarWidth,
            defaults.OverlaySidebarWidth);
        Equal<WidgetShortcut?>(null, defaults.ToggleOverlay);

        WidgetShortcut overlayShortcut = new(
            WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
            0x4F);
        WidgetSettings normalized = (defaults with
        {
            OverlayEnabled = true,
            OverlayDimmingOpacity = -1,
            OverlaySidebarWidth = double.PositiveInfinity,
            ToggleOverlay = overlayShortcut,
        }).Normalize();

        True(normalized.OverlayEnabled, "Normalization must preserve the enable switch.");
        Equal(
            WidgetSettings.MinimumOverlayDimmingOpacity,
            normalized.OverlayDimmingOpacity);
        Equal(
            WidgetSettings.DefaultOverlaySidebarWidth,
            normalized.OverlaySidebarWidth);
        Equal(overlayShortcut, normalized.ToggleOverlay);

        WidgetSettings maximum = defaults with
        {
            OverlayDimmingOpacity = 5,
            OverlaySidebarWidth = 5000,
        };
        Equal(
            WidgetSettings.MaximumOverlayDimmingOpacity,
            maximum.Normalize().OverlayDimmingOpacity);
        Equal(
            WidgetSettings.MaximumOverlaySidebarWidth,
            maximum.Normalize().OverlaySidebarWidth);
        return Task.CompletedTask;
    }

    private static Task FloatingWidgetSettingsNormalize()
    {
        FloatingWidgetLayout layout = new(
            IsOpen: true,
            Left: double.PositiveInfinity,
            Top: -240,
            Width: 1200,
            Height: 40,
            IsPinned: true,
            MonitorId: "  ");

        FloatingWidgetLayout normalized = layout.Normalize();

        True(normalized.IsOpen, "Normalization must preserve the open state.");
        Equal<double?>(null, normalized.Left);
        Equal(-240d, normalized.Top);
        Equal(FloatingWidgetLayout.MaximumWidth, normalized.Width);
        Equal(FloatingWidgetLayout.MinimumHeight, normalized.Height);
        True(normalized.IsPinned, "Normalization must preserve pinning.");
        Equal<string?>(null, normalized.MonitorId);
        return Task.CompletedTask;
    }

    private static Task FloatingWidgetSettingsPersist()
    {
        string settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"WinQuickSwitch.Tests.{Guid.NewGuid():N}.json");

        try
        {
            JsonWidgetSettingsStore store = new(settingsPath);
            WidgetSettings expected = WidgetSettings.Default with
            {
                FloatingDisplayWidget = new FloatingWidgetLayout(
                    IsOpen: true,
                    Left: -420,
                    Top: 80,
                    Width: 380,
                    Height: 280,
                    IsPinned: true,
                    MonitorId: "DISPLAY-1"),
                FloatingAudioWidget = new FloatingWidgetLayout(
                    IsOpen: true,
                    Left: 40,
                    Top: 60,
                    Width: 340,
                    Height: 240),
                FloatingDevicesWidget = new FloatingWidgetLayout(
                    IsOpen: true,
                    Left: 80,
                    Top: 100,
                    Width: 360,
                    Height: 260,
                    IsPinned: true),
            };

            True(
                store.TrySave(expected, out string? saveError),
                saveError ?? "Floating widget settings save failed.");
            Equal(expected, store.Load());
        }
        finally
        {
            File.Delete(settingsPath);
            File.Delete(settingsPath + ".tmp");
        }

        return Task.CompletedTask;
    }

    private static Task FreeWidgetSettingsStripProfilesLayouts()
    {
        string settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"WinQuickSwitch.Tests.{Guid.NewGuid():N}.json");

        try
        {
            JsonWidgetSettingsStore store = new(
                settingsPath,
                ProductConfiguration.Free);
            WidgetSettings expected = WidgetSettings.Default with
            {
                OverlayWorkspace = new OverlayWorkspaceSettings(
                    MonitorLayouts: new Dictionary<string, OverlayMonitorLayout>
                    {
                        ["DISPLAY1"] = new(
                            "DISPLAY1",
                            [
                                new OverlayWidgetPlacement(OverlayWidgetType.Display),
                                new OverlayWidgetPlacement(OverlayWidgetType.Profiles),
                            ]),
                    }),
            };

            True(
                store.TrySave(expected, out string? saveError),
                saveError ?? "Free settings save failed.");
            WidgetSettings loaded = store.Load();
            True(
                loaded.OverlayWorkspace?.MonitorLayouts?["DISPLAY1"]
                    .Find(OverlayWidgetType.Profiles) is null,
                "Free settings must not persist or restore Profiles placements.");
        }
        finally
        {
            File.Delete(settingsPath);
            File.Delete(settingsPath + ".tmp");
        }

        return Task.CompletedTask;
    }

    private static Task OverlaySettingsPersist()
    {
        string settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"WinQuickSwitch.Tests.{Guid.NewGuid():N}.json");

        try
        {
            JsonWidgetSettingsStore store = new(settingsPath);
            WidgetShortcut overlayShortcut = new(
                WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Shift,
                0x4F);
            WidgetSettings expected = WidgetSettings.Default with
            {
                OverlayEnabled = true,
                OverlayDimmingOpacity = 0.42,
                OverlaySidebarWidth = 460,
                ToggleOverlay = overlayShortcut,
            };

            True(
                store.TrySave(expected, out string? saveError),
                saveError ?? "Overlay settings save failed.");
            WidgetSettings loaded = store.Load();

            True(loaded.OverlayEnabled, "The overlay enable switch was not persisted.");
            Equal(0.42, loaded.OverlayDimmingOpacity);
            Equal(460d, loaded.OverlaySidebarWidth);
            Equal(overlayShortcut, loaded.ToggleOverlay);
        }
        finally
        {
            File.Delete(settingsPath);
            File.Delete(settingsPath + ".tmp");
        }

        return Task.CompletedTask;
    }

    private static Task OverlayWorkspaceDefaultsAdaptToWorkArea()
    {
        OverlayWorkArea wideArea = new(-1920, -120, 1920, 960);
        OverlayMonitorLayout paid =
            OverlayWorkspaceLayoutCalculator.CreateDefaultLayout(
                " DISPLAY-1 ",
                wideArea,
                profilesEnabled: true);

        OverlayWidgetType[] visiblePaid = (paid.Widgets ?? [])
            .Where(placement => placement.IsVisible)
            .Select(placement => placement.Widget)
            .ToArray();

        True(
            visiblePaid.SequenceEqual(
            [
                OverlayWidgetType.Display,
                OverlayWidgetType.Audio,
                OverlayWidgetType.Devices,
            ]),
            "Paid defaults should expose the essential widgets in order without a duplicate Favorites card.");
        True(
            paid.Find(OverlayWidgetType.Profiles)?.IsVisible == false,
            "Paid defaults should keep Profiles available but hidden.");
        True(
            paid.Find(OverlayWidgetType.About)?.IsVisible == false,
            "Paid defaults should keep About available but hidden.");
        True(
            (paid.Widgets ?? []).All(placement =>
                placement.Left >= wideArea.Left &&
                placement.Top >= wideArea.Top &&
                placement.Left + placement.Width <=
                    wideArea.Right - OverlayWorkspaceLayoutCalculator.LauncherWidth &&
                placement.Top + placement.Height <= wideArea.Bottom),
            "Default cards must fit in the monitor work area.");

        OverlayMonitorLayout free =
            OverlayWorkspaceLayoutCalculator.CreateDefaultLayout(
                "DISPLAY-2",
                new(0, 0, 700, 520),
                profilesEnabled: false);
        True(
            free.Find(OverlayWidgetType.Favorites) is null,
            "Free defaults should not expose a duplicate Favorites card.");
        True(
            free.Find(OverlayWidgetType.Profiles) is { IsVisible: false },
            "Free defaults must not compose a Profiles card.");
        return Task.CompletedTask;
    }

    private static Task OverlayWorkspacePlacementsSnapAndClamp()
    {
        Equal(24d, OverlayWorkspaceLayoutCalculator.SnapToGrid(25));
        Equal(-24d, OverlayWorkspaceLayoutCalculator.SnapToGrid(-25));

        OverlayWidgetPlacement placement =
            new(
                OverlayWidgetType.Audio,
                Left: -1000,
                Top: 700,
                Width: 353,
                Height: 257);
        OverlayWidgetPlacement normalized =
            OverlayWorkspaceLayoutCalculator.SnapAndClamp(
                placement,
                new(-800, -400, 800, 500));

        Equal(-800d, normalized.Left);
        Equal(248d, normalized.Top);
        Equal(348d, normalized.Width);
        Equal(252d, normalized.Height);
        True(
            normalized.Left + normalized.Width <= 800 &&
            normalized.Top + normalized.Height <= 500,
            "Clamped placements must remain inside negative-coordinate work areas.");

        OverlayWidgetPlacement oversized =
            OverlayWorkspaceLayoutCalculator.ClampToWorkArea(
                new(
                    OverlayWidgetType.Display,
                    Left: 100,
                    Top: 100,
                    Width: 1200,
                    Height: 900),
                new(0, 0, 640, 420));
        Equal(640d, oversized.Width);
        Equal(420d, oversized.Height);
        Equal(0d, oversized.Left);
        Equal(0d, oversized.Top);
        return Task.CompletedTask;
    }

    private static Task OverlayWorkspaceContentFitHonorsMonitorCaps()
    {
        (double width, double height) =
            OverlayWorkspaceLayoutCalculator.ClampContentFitSize(
                requestedWidth: 1800,
                requestedHeight: 1400,
                canvasWidth: 1200,
                canvasHeight: 900);
        Equal(960d, width);
        Equal(792d, height);

        (width, height) =
            OverlayWorkspaceLayoutCalculator.ClampContentFitSize(
                requestedWidth: 760,
                requestedHeight: 600,
                canvasWidth: 1920,
                canvasHeight: 1080);
        Equal(760d, width);
        Equal(600d, height);
        return Task.CompletedTask;
    }

    private static Task OverlayResizeStopsAtEdgesWithoutShifting()
    {
        OverlayWidgetPlacement original = new(
            OverlayWidgetType.Devices,
            Left: 500.5,
            Top: 80.25,
            Width: 300,
            Height: 280);
        OverlayWidgetPlacement resized =
            OverlayWorkspaceLayoutCalculator.ResizeFromBottomRight(
                original,
                horizontalChange: 240,
                verticalChange: 500,
                workArea: new OverlayWorkArea(0, 0, 840.5, 620.25),
                minimumWidth: 280,
                minimumHeight: 180);

        Equal(500.5d, resized.Left);
        Equal(80.25d, resized.Top);
        Equal(340d, resized.Width);
        Equal(540d, resized.Height);

        OverlayWidgetPlacement deviceMinimum =
            OverlayWorkspaceLayoutCalculator.ResizeFromBottomRight(
                new OverlayWidgetPlacement(
                    OverlayWidgetType.Devices,
                    Left: 20,
                    Top: 20,
                    Width: 840,
                    Height: 620),
                horizontalChange: -1000,
                verticalChange: -1000,
                workArea: new OverlayWorkArea(0, 0, 1400, 900),
                minimumWidth: 840,
                minimumHeight: 820);
        Equal(20d, deviceMinimum.Left);
        Equal(20d, deviceMinimum.Top);
        Equal(840d, deviceMinimum.Width);
        Equal(820d, deviceMinimum.Height);
        return Task.CompletedTask;
    }

    private static Task OverlayWorkspaceDefaultCardsNeverOverlap()
    {
        OverlayMonitorLayout layout =
            OverlayWorkspaceLayoutCalculator.CreateDefaultLayout(
                "DISPLAY-1",
                new(0, 0, 1920, 1080),
                profilesEnabled: true);
        OverlayWidgetPlacement[] visible = (layout.Widgets ?? [])
            .Where(placement => placement.IsVisible)
            .ToArray();

        for (int first = 0; first < visible.Length; first++)
        {
            for (int second = first + 1; second < visible.Length; second++)
            {
                True(
                    !OverlayWorkspaceLayoutCalculator.Overlaps(
                        visible[first],
                        visible[second]),
                    $"Default {visible[first].Widget} and {visible[second].Widget} cards overlap.");
            }
        }

        return Task.CompletedTask;
    }

    private static Task OverlayFittedDefaultsPackWithoutOverlap()
    {
        OverlayWorkspaceWidgetLayout[] input =
        [
            new("Display", 12, 12, 720, 360, ZIndex: 0),
            new("Audio", 750, 12, 760, 600, ZIndex: 1),
            new("Devices", 12, 520, 840, 820, ZIndex: 2),
        ];
        IReadOnlyList<OverlayWorkspaceWidgetLayout> arranged =
            OverlayInitialLayoutArranger.Arrange(input, 1692, 1022);
        for (int index = 0; index < input.Length; index++)
        {
            OverlayWorkspaceWidgetLayout item = arranged[index];
            Equal(input[index].Width, item.Width);
            Equal(input[index].Height, item.Height);
            Equal(input[index].ZIndex, item.ZIndex);
            True(item.Left >= 0 && item.Top >= 0 &&
                 item.Left + item.Width <= 1692 && item.Top + item.Height <= 1022,
                "Full-sized generated cards must remain inside the canvas.");
            foreach (OverlayWorkspaceWidgetLayout other in arranged.Skip(index + 1))
            {
                True(item.Left + item.Width <= other.Left ||
                     other.Left + other.Width <= item.Left ||
                     item.Top + item.Height <= other.Top ||
                     other.Top + other.Height <= item.Top,
                    "Fitted defaults must not overlap after minimum-size enforcement.");
            }
        }
        True(input[2].Top == 520, "Packing must not mutate its input layouts.");
        return Task.CompletedTask;
    }

    private static Task OverlayWorkspaceFindsFreeSlot()
    {
        OverlayWorkArea area = new(0, 0, 1000, 700);
        OverlayWidgetPlacement requested = new(
            OverlayWidgetType.Devices,
            12,
            12,
            360,
            240);
        OverlayWidgetPlacement[] occupied =
        [
            new(OverlayWidgetType.Display, 12, 12, 360, 240),
            new(OverlayWidgetType.Audio, 384, 12, 360, 240),
        ];

        OverlayWidgetPlacement? available =
            OverlayWorkspaceLayoutCalculator.FindFirstAvailablePlacement(
                requested,
                area,
                occupied);

        True(available is not null, "A free widget slot should be found.");
        OverlayWidgetPlacement free = available!;
        Equal(12d, free.Left);
        Equal(264d, free.Top);
        True(
            occupied.All(blocker =>
                !OverlayWorkspaceLayoutCalculator.Overlaps(
                    free,
                    blocker,
                    OverlayWorkspaceLayoutCalculator.GridIncrement)),
            "The restored widget must not overlap an existing card.");

        OverlayWidgetPlacement custom = new(
            OverlayWidgetType.Options,
            600,
            396,
            360,
            240);
        OverlayWidgetPlacement? preserved =
            OverlayWorkspaceLayoutCalculator.FindFirstAvailablePlacement(
                custom,
                area,
                occupied);
        Equal(600d, preserved!.Left);
        Equal(396d, preserved.Top);
        return Task.CompletedTask;
    }

    private static Task OverlayWorkspaceRestoresSavedDimensionsRegardlessOfCustomSize()
    {
        System.Reflection.MethodInfo? restoreMethod =
            typeof(global::WinQuickSwitch.MainWindow).GetMethod(
                "CreateRestoredWorkspaceLayout",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static);
        True(
            restoreMethod is not null,
            "The workspace restore conversion should remain available for persisted layouts.");

        OverlayWidgetPlacement savedDefaultSize = new(
            OverlayWidgetType.Audio,
            Left: 120,
            Top: 84,
            Width: 516,
            Height: 348,
            IsVisible: true,
            ZIndex: 4,
            HasCustomSize: false);
        OverlayWidgetPlacement savedCustomSize = savedDefaultSize with
        {
            Widget = OverlayWidgetType.Devices,
            Width = 624,
            Height = 516,
            HasCustomSize = true,
        };

        foreach (OverlayWidgetPlacement placement in
                 new[] { savedDefaultSize, savedCustomSize })
        {
            OverlayWorkspaceWidgetLayout restored =
                (OverlayWorkspaceWidgetLayout)restoreMethod!.Invoke(
                    null,
                    new object?[] { placement })!;

            Equal(
                placement.Width,
                restored.Width);
            Equal(
                placement.Height,
                restored.Height);
            Equal(
                placement.Left,
                restored.Left);
            Equal(
                placement.Top,
                restored.Top);
            Equal(
                placement.HasCustomSize,
                restored.HasCustomSize);
        }

        return Task.CompletedTask;
    }

    private static Task OverlayWorkspacePreservesIntentionalOverlap()
    {
        OverlayMonitorLayout saved = new(
            "DISPLAY1",
            [
                new OverlayWidgetPlacement(
                    OverlayWidgetType.Display,
                    Left: 24,
                    Top: 24,
                    Width: 504,
                    Height: 264,
                    IsVisible: true,
                    ZIndex: 0),
                new OverlayWidgetPlacement(
                    OverlayWidgetType.Audio,
                    Left: 240.5,
                    Top: 144.25,
                    Width: 551.75,
                    Height: 335.5,
                    IsVisible: true,
                    ZIndex: 1),
            ]);
        OverlayWorkspaceSettings settings =
            OverlayWorkspaceSettings.Default.WithLayout(saved);

        OverlayMonitorLayout restored = settings.GetOrCreateLayout(
            "DISPLAY1",
            new OverlayWorkArea(0, 0, 1920, 1080));
        OverlayWidgetPlacement display =
            restored.Find(OverlayWidgetType.Display)!;
        OverlayWidgetPlacement audio =
            restored.Find(OverlayWidgetType.Audio)!;

        True(
            OverlayWorkspaceLayoutCalculator.Overlaps(display, audio),
            "Persisted widget layouts should allow cards to overlap intentionally.");
        Equal(504d, display.Width);
        Equal(264d, display.Height);
        Equal(551.75d, audio.Width);
        Equal(335.5d, audio.Height);
        Equal(24d, display.Left);
        Equal(24d, display.Top);
        Equal(240.5d, audio.Left);
        Equal(144.25d, audio.Top);
        return Task.CompletedTask;
    }

    private static Task OverlayWorkspacePreservesCustomBounds()
    {
        True(
            OverlayMonitorLayout.AreMonitorIdsEquivalent(
                "DISPLAY1",
                @"\\.\DISPLAY1"),
            "Legacy and MONITORINFOEX display names should restore the same layout.");
        OverlayWidgetPlacement custom = new(
            OverlayWidgetType.Audio,
            Left: 348,
            Top: 276,
            Width: 516,
            Height: 396,
            IsVisible: false,
            ZIndex: 9,
            HasCustomSize: true);
        OverlayWorkspaceSettings settings =
            OverlayWorkspaceSettings.Default.WithLayout(
                new OverlayMonitorLayout(
                    @"\\.\DISPLAY1",
                    [custom]));
        string json = OverlayWorkspaceJson.Serialize(settings);
        OverlayWorkspaceSettings restored =
            OverlayWorkspaceJson.DeserializeOrDefault(json);
        OverlayWidgetPlacement? placement = restored.MonitorLayouts?
            [@"\\.\DISPLAY1"]
            .Find(OverlayWidgetType.Audio);

        True(placement is not null, "The custom Audio placement was lost.");
        OverlayWidgetPlacement preserved = placement!;
        Equal(custom.Left, preserved.Left);
        Equal(custom.Top, preserved.Top);
        Equal(custom.Width, preserved.Width);
        Equal(custom.Height, preserved.Height);
        Equal(custom.IsVisible, preserved.IsVisible);
        Equal(custom.ZIndex, preserved.ZIndex);
        True(
            preserved.HasCustomSize,
            "The persisted layout must remember that the user chose this size.");
        Equal(
            OverlayWidgetPlacement.CurrentContentLayoutVersion,
            preserved.ContentLayoutVersion);
        return Task.CompletedTask;
    }

    private static Task OverlayWorkspaceDoesNotUseStaleVirtualScreenFallback()
    {
        OverlayWorkspaceSettings settings =
            OverlayWorkspaceSettings.Default.WithLayout(
                new OverlayMonitorLayout(
                    "virtual-screen",
                    [new OverlayWidgetPlacement(
                        OverlayWidgetType.Display,
                        Width: 280,
                        Height: 180)]));

        True(
            settings.FindSavedLayout("DISPLAY2") is null,
            "An unknown monitor must receive a fresh default layout instead of the legacy virtual-screen bucket.");
        True(
            settings.FindSavedLayout("virtual-screen") is not null,
            "The legacy bucket should remain readable for compatibility and migration.");
        return Task.CompletedTask;
    }

    private static Task OverlayWorkspaceMigratesGeneratedV1Layout()
    {
        OverlayWidgetType[] generatedWidgets =
        [
            OverlayWidgetType.Display,
            OverlayWidgetType.Audio,
            OverlayWidgetType.Devices,
            OverlayWidgetType.Profiles,
            OverlayWidgetType.Favorites,
            OverlayWidgetType.About,
        ];
        OverlayWorkspaceSettings legacy = new(
            LayoutVersion: 1,
            MonitorLayouts: new Dictionary<string, OverlayMonitorLayout>
            {
                [@"\\.\DISPLAY1"] = new(
                    @"\\.\DISPLAY1",
                    generatedWidgets.Select((widget, index) =>
                        new OverlayWidgetPlacement(
                            widget,
                            Left: index % 3 * 360,
                            Top: index / 3 * 384,
                            Width: 348,
                            Height: 372,
                            IsVisible: true,
                            ZIndex: index))
                    .ToArray()),
            });

        OverlayWorkspaceSettings migrated = legacy.Normalize(
            profilesEnabled: true);
        OverlayMonitorLayout layout = migrated.MonitorLayouts!
            [@"\\.\DISPLAY1"];
        OverlayWidgetType[] visible = (layout.Widgets ?? [])
            .Where(placement => placement.IsVisible)
            .Select(placement => placement.Widget)
            .ToArray();

        Equal(OverlayWorkspaceSettings.CurrentLayoutVersion, migrated.LayoutVersion);
        True(
            visible.SequenceEqual(
            [
                OverlayWidgetType.Display,
                OverlayWidgetType.Audio,
                OverlayWidgetType.Devices,
            ]),
            "Generated v1 layouts should migrate to the three quick-access defaults.");
        True(
            layout.Find(OverlayWidgetType.Profiles)?.IsVisible == false &&
            layout.Find(OverlayWidgetType.About)?.IsVisible == false,
            "Profiles and About should not remain default quick widgets after migration.");
        True(
            layout.Find(OverlayWidgetType.Favorites) is null,
            "Legacy Favorites placements should be removed because Audio owns those controls.");
        Equal(348d, layout.Find(OverlayWidgetType.Display)!.Width);
        Equal(348d, layout.Find(OverlayWidgetType.Audio)!.Width);
        Equal(
            0,
            layout.Find(OverlayWidgetType.Display)!.ContentLayoutVersion);
        return Task.CompletedTask;
    }

    private static Task OverlayWorkspaceSettingsNormalizeAndRoundTrip()
    {
        OverlayWorkspaceSettings settings = new(
            LayoutVersion: 0,
            InspectorWidth: double.PositiveInfinity,
            MonitorLayouts: new Dictionary<string, OverlayMonitorLayout>(
                StringComparer.OrdinalIgnoreCase)
            {
                [" DISPLAY-1 "] = new(
                    "DISPLAY-1",
                    [
                        new OverlayWidgetPlacement(OverlayWidgetType.Display),
                        new OverlayWidgetPlacement(OverlayWidgetType.Display, ZIndex: 2),
                        new OverlayWidgetPlacement(OverlayWidgetType.Profiles),
                    ]),
                [" "] = new(" ", []),
            });

        OverlayWorkspaceSettings normalized = settings.Normalize(
            profilesEnabled: false);
        Equal(OverlayWorkspaceSettings.CurrentLayoutVersion, normalized.LayoutVersion);
        Equal(
            OverlayWorkspaceSettings.DefaultInspectorWidth,
            normalized.InspectorWidth);
        True(
            normalized.MonitorLayouts?.ContainsKey("DISPLAY-1") == true,
            "Monitor IDs should be trimmed during normalization.");
        Equal(
            1,
            normalized.MonitorLayouts!["DISPLAY-1"].Widgets!.Count);
        True(
            normalized.MonitorLayouts["DISPLAY-1"].Find(OverlayWidgetType.Profiles) is null,
            "Free normalization must remove persisted Profiles placements.");

        string json = OverlayWorkspaceJson.Serialize(normalized, profilesEnabled: false);
        OverlayWorkspaceSettings restored =
            OverlayWorkspaceJson.DeserializeOrDefault(
                json,
                profilesEnabled: false);
        Equal(
            OverlayWorkspaceJson.Serialize(normalized, profilesEnabled: false),
            OverlayWorkspaceJson.Serialize(restored, profilesEnabled: false));
        return Task.CompletedTask;
    }

    private static Task OverlayWorkspaceMigratesLegacyContentFitState()
    {
        OverlayWorkspaceSettings legacy = new(
            LayoutVersion: 4,
            MonitorLayouts: new Dictionary<string, OverlayMonitorLayout>
            {
                ["DISPLAY1"] = new(
                    "DISPLAY1",
                    [
                        new OverlayWidgetPlacement(
                            OverlayWidgetType.Display,
                            Left: 144,
                            Top: 96,
                            Width: 500,
                            Height: 260,
                            IsVisible: true,
                            ZIndex: 7),
                        new OverlayWidgetPlacement(
                            OverlayWidgetType.Audio,
                            Left: 720,
                            Top: 96,
                            Width: 612,
                            Height: 444,
                            IsVisible: true,
                            ZIndex: 3,
                            HasCustomSize: true),
                    ]),
            });

        OverlayWorkspaceSettings migrated = legacy.Normalize();
        OverlayMonitorLayout layout = migrated.MonitorLayouts!["DISPLAY1"];
        OverlayWidgetPlacement display = layout.Find(
            OverlayWidgetType.Display)!;
        OverlayWidgetPlacement audio = layout.Find(
            OverlayWidgetType.Audio)!;

        Equal(OverlayWorkspaceSettings.CurrentLayoutVersion, migrated.LayoutVersion);
        Equal(500d, display.Width);
        Equal(260d, display.Height);
        Equal(144d, display.Left);
        Equal(96d, display.Top);
        Equal(7, display.ZIndex);
        Equal(612d, audio.Width);
        Equal(444d, audio.Height);
        Equal(0, display.ContentLayoutVersion);
        Equal(
            OverlayWidgetPlacement.CurrentContentLayoutVersion,
            audio.ContentLayoutVersion);
        return Task.CompletedTask;
    }

    private static Task LegacySidebarWidthMigratesToInspector()
    {
        OverlayWorkspaceSettings restored =
            OverlayWorkspaceJson.DeserializeOrDefault(
                "{\"OverlaySidebarWidth\":640}");
        Equal(640d, restored.InspectorWidth);

        OverlayWorkspaceSettings minimum =
            OverlayWorkspaceJson.DeserializeOrDefault(
                "{\"OverlaySidebarWidth\":420}");
        Equal(
            OverlayWorkspaceSettings.MinimumInspectorWidth,
            minimum.InspectorWidth);
        return Task.CompletedTask;
    }

    private static Task ProfileCatalogNormalizes()
    {
        WidgetShortcut shortcut = new(
            WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
            0x50);

        ProfileCatalog catalog = new ProfileCatalog(
            0,
            [
                new ProfileDefinition("work", "  Work  ", true, shortcut),
                new ProfileDefinition("work", "Duplicate", true, null),
                new ProfileDefinition("gaming", "Gaming", true, shortcut),
                new ProfileDefinition("meeting", "Meeting", true, null),
                new ProfileDefinition("focus", "Focus", true, null),
                new ProfileDefinition("travel", "Travel", true, null),
                new ProfileDefinition("", "Invalid", true, null),
                new ProfileDefinition("missing-name", "", true, null),
            ]).Normalize();

        Equal(5, catalog.Profiles.Count);
        Equal(1, catalog.SchemaVersion);
        Equal("Work", catalog.Profiles[0].Name);
        True(catalog.Profiles[0].IsPinned, "The first four valid profiles should remain pinned.");
        True(!catalog.Profiles[4].IsPinned, "Additional pinned profiles should be unpinned.");
        Equal("Ctrl + Alt + P", catalog.Profiles[0].Shortcut?.DisplayText);
        True(
            catalog.Profiles[1].Shortcut is null,
            "A duplicate profile shortcut should be cleared from the later profile.");
        return Task.CompletedTask;
    }

    private static Task ProfileCatalogPersists()
    {
        string profilesPath = Path.Combine(
            Path.GetTempPath(),
            $"WinQuickSwitch.Profiles.{Guid.NewGuid():N}.json");

        try
        {
            ProfileDefinition expected = new(
                "work",
                "Work",
                true,
                null,
                DisplayMode.Extend,
                new ProfileEndpointTarget("speakers", "Desk speakers"),
                null,
                new ProfileEndpointTarget("microphone", "USB microphone"),
                null,
                TaskbarState.Visible,
                true,
                0.42f);
            JsonProfileStore store = new(profilesPath);

            True(
                store.TrySave(new ProfileCatalog(0, [expected]), out string? saveError),
                saveError ?? "Profile save failed.");

            ProfileDefinition loaded = store.Load().Profiles.Single();
            Equal(expected.Id, loaded.Id);
            Equal(expected.Name, loaded.Name);
            Equal(expected.DisplayMode, loaded.DisplayMode);
            Equal(expected.PlaybackGeneral, loaded.PlaybackGeneral);
            Equal(expected.RecordingGeneral, loaded.RecordingGeneral);
            Equal(expected.TaskbarState, loaded.TaskbarState);
            Equal(expected.MicrophoneMuted, loaded.MicrophoneMuted);
            Equal(expected.MasterVolume, loaded.MasterVolume);
        }
        finally
        {
            File.Delete(profilesPath);
            File.Delete(profilesPath + ".tmp");
        }

        return Task.CompletedTask;
    }

    private static Task ProfileActionsNormalize()
    {
        ProfileDefinition normalized = new ProfileDefinition(
            "profile",
            "Profile",
            false,
            null,
            null,
            new ProfileEndpointTarget("", "Missing endpoint"),
            new ProfileEndpointTarget("endpoint", "  Valid endpoint  "),
            null,
            null,
            TaskbarState.Unavailable,
            null,
            1.5f).Normalize();

        Equal<ProfileEndpointTarget?>(null, normalized.PlaybackGeneral);
        Equal(
            new ProfileEndpointTarget("endpoint", "Valid endpoint"),
            normalized.PlaybackCommunications);
        Equal<TaskbarState?>(null, normalized.TaskbarState);
        Equal<float?>(null, normalized.MasterVolume);
        True(normalized.HasActions, "A valid endpoint action should remain present.");
        return Task.CompletedTask;
    }

    private static Task StartupRegistrationUsesHiddenCommand()
    {
        FakeStartupRegistry registry = new();
        WindowsStartupRegistrationService service = new(
            registry,
            () => @"C:\Program Files\WinQuickSwitch\WinQuickSwitch.exe");

        StartupRegistrationResult result = service.SetEnabled(true);

        True(result.Succeeded, result.Message);
        Equal(
            "\"C:\\Program Files\\WinQuickSwitch\\WinQuickSwitch.exe\" --startup",
            registry.Value);
        True(service.IsEnabled, "The newly written startup command should be active.");
        return Task.CompletedTask;
    }

    private static Task StartupRegistrationRecognizesCurrentExecutable()
    {
        FakeStartupRegistry registry = new()
        {
            Value = "\"C:\\Old\\WinQuickSwitch.exe\" --startup",
        };
        WindowsStartupRegistrationService service = new(
            registry,
            () => @"C:\New\WinQuickSwitch.exe");

        True(
            !service.IsEnabled,
            "A stale registration for a moved executable should not appear enabled.");

        registry.Value = "\"c:\\new\\winquickswitch.exe\" --startup";
        True(
            service.IsEnabled,
            "Windows paths should be matched without case sensitivity.");
        return Task.CompletedTask;
    }

    private static Task StartupRegistrationCanBeRemoved()
    {
        FakeStartupRegistry registry = new()
        {
            Value = "\"C:\\Apps\\WinQuickSwitch.exe\" --startup",
        };
        WindowsStartupRegistrationService service = new(
            registry,
            () => @"C:\Apps\WinQuickSwitch.exe");

        StartupRegistrationResult result = service.SetEnabled(false);

        True(result.Succeeded, result.Message);
        Equal<string?>(null, registry.Value);
        Equal(1, registry.DeleteCount);
        return Task.CompletedTask;
    }

    private static Task StartupRegistrationDoesNotRemoveAnotherEdition()
    {
        FakeStartupRegistry registry = new()
        {
            Value = "\"C:\\Apps\\WinQuickSwitch Free.exe\" --startup",
        };
        WindowsStartupRegistrationService service = new(
            registry,
            () => @"C:\Apps\WinQuickSwitch.exe");

        StartupRegistrationResult result = service.SetEnabled(false);

        True(result.Succeeded, result.Message);
        Equal(
            "\"C:\\Apps\\WinQuickSwitch Free.exe\" --startup",
            registry.Value);
        Equal(0, registry.DeleteCount);
        return Task.CompletedTask;
    }

    private static Task StartupRegistrationFailuresRemainNonfatal()
    {
        FakeStartupRegistry registry = new()
        {
            Exception = new IOException("blocked"),
        };
        WindowsStartupRegistrationService service = new(
            registry,
            () => @"C:\Apps\WinQuickSwitch.exe");

        True(
            !service.IsEnabled,
            "An unreadable startup entry should be treated as disabled.");

        StartupRegistrationResult result = service.SetEnabled(true);

        True(!result.Succeeded, "A registry failure should be reported.");
        Contains("blocked", result.Message);
        return Task.CompletedTask;
    }

    private static Task GlobalHotkeysRegisterAndResolve()
    {
        FakeGlobalHotkeyNative native = new();
        using WindowsGlobalHotkey hotkeys = new(native);
        WidgetSettings settings = WidgetSettings.Default with
        {
            Audio = new WidgetShortcut(
                WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
                0x41),
            Extend = new WidgetShortcut(
                WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
                0x45),
            FavoriteOutput1 = new FavoriteOutputSetting(
                "endpoint-1",
                "Desk speakers",
                new WidgetShortcut(
                    WidgetHotkeyModifiers.Control |
                    WidgetHotkeyModifiers.Alt,
                    0x31)),
            FavoriteInput1 = new FavoriteInputSetting(
                "mic-1",
                "Desk microphone",
                new WidgetShortcut(
                    WidgetHotkeyModifiers.Control |
                    WidgetHotkeyModifiers.Alt,
                    0x32)),
        };

        HotkeyRegistrationResult result =
            hotkeys.ApplyBindings(new IntPtr(44), settings);

        True(result.Succeeded, result.FirstFailure ?? "Registration failed.");
        Equal(5, native.Registrations.Count);
        True(
            hotkeys.TryResolveAction(
                WindowsGlobalHotkey.GetId(WidgetHotkeyAction.ToggleWidget),
                out WidgetHotkeyAction toggleAction),
            "The toggle shortcut was not registered.");
        Equal(WidgetHotkeyAction.ToggleWidget, toggleAction);
        True(
            hotkeys.TryResolveAction(
                WindowsGlobalHotkey.GetId(WidgetHotkeyAction.Audio),
                out WidgetHotkeyAction audioAction),
            "The Audio shortcut was not registered.");
        Equal(WidgetHotkeyAction.Audio, audioAction);
        True(
            hotkeys.TryResolveAction(
                WindowsGlobalHotkey.GetId(WidgetHotkeyAction.Extend),
                out WidgetHotkeyAction extendAction),
            "The Extend shortcut was not registered.");
        Equal(WidgetHotkeyAction.Extend, extendAction);
        True(
            hotkeys.TryResolveAction(
                WindowsGlobalHotkey.GetId(WidgetHotkeyAction.FavoriteOutput1),
                out WidgetHotkeyAction favoriteAction),
            "The favorite-output shortcut was not registered.");
        Equal(WidgetHotkeyAction.FavoriteOutput1, favoriteAction);
        True(
            hotkeys.TryResolveAction(
                WindowsGlobalHotkey.GetId(WidgetHotkeyAction.FavoriteInput1),
                out WidgetHotkeyAction favoriteInputAction),
            "The favorite-microphone shortcut was not registered.");
        Equal(WidgetHotkeyAction.FavoriteInput1, favoriteInputAction);
        True(
            (native.Registrations[0].Modifiers & 0x4000) != 0,
            "MOD_NOREPEAT was not applied.");
        return Task.CompletedTask;
    }

    private static Task GlobalHotkeysRegisterAndResolveOverlay()
    {
        FakeGlobalHotkeyNative native = new();
        using WindowsGlobalHotkey hotkeys = new(native);
        WidgetShortcut overlayShortcut = new(
            WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Shift,
            0x4F);
        WidgetSettings settings = WidgetSettings.Default with
        {
            ToggleOverlay = overlayShortcut,
        };

        HotkeyRegistrationResult result = hotkeys.ApplyBindings(
            new IntPtr(47),
            settings);

        True(result.Succeeded, result.FirstFailure ?? "Registration failed.");
        Equal(2, native.Registrations.Count);
        True(
            hotkeys.TryResolveAction(
                WindowsGlobalHotkey.GetId(WidgetHotkeyAction.ToggleOverlay),
                out WidgetHotkeyAction overlayAction),
            "The overlay shortcut was not registered.");
        Equal(WidgetHotkeyAction.ToggleOverlay, overlayAction);
        Equal(
            0x5157,
            WindowsGlobalHotkey.GetId(WidgetHotkeyAction.ToggleWidget));
        Equal(
            0x5158,
            WindowsGlobalHotkey.GetId(WidgetHotkeyAction.Display));
        Equal(
            0x5166,
            WindowsGlobalHotkey.GetId(WidgetHotkeyAction.FavoriteInput4));
        Equal(
            0x5167,
            WindowsGlobalHotkey.GetId(WidgetHotkeyAction.ToggleOverlay));
        return Task.CompletedTask;
    }

    private static Task GlobalHotkeyConflictsStayIsolated()
    {
        FakeGlobalHotkeyNative native = new()
        {
            FailingVirtualKey = 0x41,
        };
        using WindowsGlobalHotkey hotkeys = new(native);
        WidgetSettings settings = WidgetSettings.Default with
        {
            Audio = new WidgetShortcut(
                WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
                0x41),
        };

        HotkeyRegistrationResult result =
            hotkeys.ApplyBindings(new IntPtr(45), settings);

        True(!result.Succeeded, "The simulated conflict should be reported.");
        True(
            result.Failures.ContainsKey(WidgetHotkeyAction.Audio),
            "The Audio conflict should remain associated with Audio.");
        True(
            hotkeys.TryResolveAction(
                WindowsGlobalHotkey.GetId(WidgetHotkeyAction.ToggleWidget),
                out WidgetHotkeyAction action),
            "A conflicting Audio binding should not disable the toggle binding.");
        Equal(WidgetHotkeyAction.ToggleWidget, action);
        return Task.CompletedTask;
    }

    private static Task GlobalHotkeysRegisterAndResolveProfiles()
    {
        FakeGlobalHotkeyNative native = new();
        using WindowsGlobalHotkey hotkeys = new(native);
        ProfileHotkeyBinding binding = new(
            "profile-work",
            new WidgetShortcut(
                WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
                0x57));

        HotkeyRegistrationResult result = hotkeys.ApplyBindings(
            new IntPtr(46),
            WidgetSettings.Default,
            [binding]);

        True(result.Succeeded, result.FirstFailure ?? "Registration failed.");
        Equal(2, native.Registrations.Count);
        True(
            hotkeys.TryResolveProfileId(
                native.Registrations[1].Id,
                out string profileId),
            "The profile shortcut was not registered.");
        Equal("profile-work", profileId);
        return Task.CompletedTask;
    }

    private static Task DynamicProfileHotkeysUnregister()
    {
        FakeGlobalHotkeyNative native = new();
        WindowsGlobalHotkey hotkeys = new(native);
        ProfileHotkeyBinding binding = new(
            "profile-work",
            new WidgetShortcut(
                WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
                0x57));

        hotkeys.ApplyBindings(new IntPtr(48), WidgetSettings.Default, [binding]);
        int firstProfileId = native.Registrations
            .Single(registration => registration.VirtualKey == 0x57)
            .Id;

        hotkeys.ApplyBindings(new IntPtr(48), WidgetSettings.Default, []);
        True(
            native.Unregistrations.Any(entry => entry.Id == firstProfileId),
            "Reapplying bindings must unregister the removed profile hotkey.");

        hotkeys.ApplyBindings(new IntPtr(48), WidgetSettings.Default, [binding]);
        int secondProfileId = native.Registrations
            .Last(registration => registration.VirtualKey == 0x57)
            .Id;
        hotkeys.Dispose();

        True(
            native.Unregistrations.Count(entry => entry.Id == secondProfileId) >= 2,
            "Disposal must unregister the active profile hotkey after reapply.");
        return Task.CompletedTask;
    }

    private static Task FailedHotkeyReapplyRetainsPreviousBindings()
    {
        FakeGlobalHotkeyNative native = new();
        using WindowsGlobalHotkey hotkeys = new(native);
        WidgetSettings working = WidgetSettings.Default with
        {
            Audio = new WidgetShortcut(
                WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
                0x41),
        };

        HotkeyRegistrationResult initial =
            hotkeys.ApplyBindings(new IntPtr(49), working);
        True(initial.Succeeded, initial.FirstFailure ?? "Initial registration failed.");

        native.FailingVirtualKey = 0x42;
        WidgetSettings rejected = working with
        {
            Audio = new WidgetShortcut(
                WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
                0x42),
        };
        HotkeyRegistrationResult result =
            hotkeys.ApplyBindings(new IntPtr(49), rejected);

        True(!result.Succeeded, "The simulated replacement conflict should fail.");
        True(
            result.RetainedPreviousBindings,
            "The previous working shortcut set should be restored.");
        Equal(
            0x41u,
            native.ActiveRegistrations[
                WindowsGlobalHotkey.GetId(WidgetHotkeyAction.Audio)].VirtualKey);
        True(
            hotkeys.TryResolveAction(
                WindowsGlobalHotkey.GetId(WidgetHotkeyAction.Audio),
                out WidgetHotkeyAction restoredAction),
            "The restored Audio shortcut should remain resolvable.");
        Equal(WidgetHotkeyAction.Audio, restoredAction);
        return Task.CompletedTask;
    }

    private static Task ProfileHotkeysReportDuplicateOwners()
    {
        FakeGlobalHotkeyNative native = new();
        using WindowsGlobalHotkey hotkeys = new(native);
        WidgetShortcut duplicate = WidgetSettings.Default.ToggleWidget!;
        ProfileHotkeyBinding binding = new(
            "profile-work",
            duplicate,
            "Workday setup");

        HotkeyRegistrationResult result = hotkeys.ApplyBindings(
            new IntPtr(50),
            WidgetSettings.Default,
            [binding]);

        True(!result.Succeeded, "The duplicate profile shortcut should be rejected.");
        string failure = result.ProfileFailures!["profile-work"];
        Contains("Show or hide", failure);
        Contains("Workday setup", failure);
        Equal(1, native.ActiveRegistrations.Count);
        return Task.CompletedTask;
    }

    private static Task ProfileHotkeyIdsRemainStableAcrossReorder()
    {
        FakeGlobalHotkeyNative native = new();
        using WindowsGlobalHotkey hotkeys = new(native);
        ProfileHotkeyBinding work = new(
            "profile-work",
            new WidgetShortcut(
                WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
                0x57),
            "Work");
        ProfileHotkeyBinding gaming = new(
            "profile-gaming",
            new WidgetShortcut(
                WidgetHotkeyModifiers.Control | WidgetHotkeyModifiers.Alt,
                0x47),
            "Gaming");

        hotkeys.ApplyBindings(
            new IntPtr(51),
            WidgetSettings.Default,
            [work, gaming]);
        int workId = native.ActiveRegistrations.Values
            .Single(registration => registration.VirtualKey == 0x57)
            .Id;
        int gamingId = native.ActiveRegistrations.Values
            .Single(registration => registration.VirtualKey == 0x47)
            .Id;

        hotkeys.ApplyBindings(
            new IntPtr(51),
            WidgetSettings.Default,
            [gaming, work]);

        Equal(
            workId,
            native.ActiveRegistrations.Values
                .Single(registration => registration.VirtualKey == 0x57)
                .Id);
        Equal(
            gamingId,
            native.ActiveRegistrations.Values
                .Single(registration => registration.VirtualKey == 0x47)
                .Id);
        return Task.CompletedTask;
    }

    private static Task WidgetOpensBelowPointer()
    {
        ScreenPoint position = WidgetPlacementCalculator.PlaceNearPointer(
            new ScreenPoint(400, 300),
            new ScreenRectangle(0, 0, 1920, 1040),
            widgetWidth: 500,
            widgetHeight: 500);

        Equal(new ScreenPoint(412, 312), position);
        return Task.CompletedTask;
    }

    private static Task WidgetFlipsAtMonitorEdges()
    {
        ScreenPoint position = WidgetPlacementCalculator.PlaceNearPointer(
            new ScreenPoint(1850, 1000),
            new ScreenRectangle(0, 0, 1920, 1040),
            widgetWidth: 500,
            widgetHeight: 500);

        Equal(new ScreenPoint(1338, 488), position);
        return Task.CompletedTask;
    }

    private static Task WidgetStaysInsideWorkArea()
    {
        ScreenPoint position = WidgetPlacementCalculator.PlaceNearPointer(
            new ScreenPoint(-1800, 900),
            new ScreenRectangle(-1920, 0, 0, 1040),
            widgetWidth: 500,
            widgetHeight: 500);

        True(position.X >= -1920, "The widget crossed the work area's left edge.");
        True(position.X + 500 <= 0, "The widget crossed the work area's right edge.");
        True(position.Y >= 0, "The widget crossed the work area's top edge.");
        True(position.Y + 500 <= 1040, "The widget crossed the work area's bottom edge.");
        return Task.CompletedTask;
    }

    private static Task GrowingWidgetClampsAtEdges()
    {
        ScreenRectangle workArea = new(0, 0, 1920, 1040);

        ScreenPoint unchanged = WidgetPlacementCalculator.ClampToWorkArea(
            new ScreenPoint(700, 200),
            workArea,
            widgetWidth: 500,
            widgetHeight: 700);
        ScreenPoint clamped = WidgetPlacementCalculator.ClampToWorkArea(
            new ScreenPoint(1500, 700),
            workArea,
            widgetWidth: 500,
            widgetHeight: 700);

        Equal(new ScreenPoint(700, 200), unchanged);
        Equal(new ScreenPoint(1420, 340), clamped);
        return Task.CompletedTask;
    }

    private static Task LiveDisplayTopologyCanBeRead()
    {
        DisplayTopologySnapshot snapshot = new WindowsDisplayTopologyService().GetSnapshot();

        True(snapshot.IsReliable, snapshot.Status);
        True(snapshot.ActiveDisplayCount > 0, "Windows should report an active display.");
        Console.WriteLine(
            $"     topology={snapshot.CurrentMode}, " +
            $"active={snapshot.ActiveDisplayCount}, " +
            $"available={snapshot.AvailableDisplayCount}");

        return Task.CompletedTask;
    }

    private static async Task LiveAudioInventoryCanBeRead()
    {
        AudioInventory inventory = await new WindowsAudioInventoryService().GetInventoryAsync();

        foreach (AudioSessionInfo session in inventory.Sessions)
        {
            True(
                session.Volume is >= 0 and <= 1,
                $"Session volume for {session.ApplicationName} was outside 0-1.");
        }

        Console.WriteLine(
            $"     playback={inventory.PlaybackEndpoints.Count}, " +
            $"recording={inventory.RecordingEndpoints.Count}, " +
            $"sessions={inventory.Sessions.Count}");
    }

    private static Task LiveAudioWatcherStartsAndStops()
    {
        using WindowsAudioChangeWatcher watcher = new();
        watcher.Start();
        watcher.Stop();
        watcher.Start();
        return Task.CompletedTask;
    }

    private static async Task LiveDeviceInventoryCanBeRead()
    {
        DeviceInventory inventory =
            await new WindowsDeviceInventoryService().GetInventoryAsync();

        foreach (ConnectedDeviceInfo device in inventory.Devices)
        {
            True(!string.IsNullOrWhiteSpace(device.Id), "A device ID was empty.");
            True(!string.IsNullOrWhiteSpace(device.Name), "A device name was empty.");
        }

        Equal(
            inventory.Devices.Count,
            inventory.Devices.Select(device => device.Id).Distinct().Count());

        Console.WriteLine(
            $"     bluetooth={inventory.Devices.Count(device => device.Transport == DeviceTransport.Bluetooth)}, " +
            $"wired={inventory.Devices.Count(device => device.Transport == DeviceTransport.Wired)}");
    }

    private static async Task LiveWirelessRadioStatesCanBeRead()
    {
        WirelessRadioSnapshot snapshot =
            await new WindowsWirelessRadioService().GetSnapshotAsync();

        Console.WriteLine(
            $"     wifi={snapshot.WiFi}, bluetooth={snapshot.Bluetooth}");
    }

    private static Task LiveTaskbarStateCanBeRead()
    {
        TaskbarSnapshot snapshot = new WindowsTaskbarService().GetSnapshot();

        True(
            snapshot.State is TaskbarState.Visible or
                TaskbarState.AutoHidden or
                TaskbarState.Unavailable,
            "Windows returned an unknown taskbar state.");
        Console.WriteLine($"     taskbar={snapshot.State}");
        return Task.CompletedTask;
    }

    private static PnpDeviceDescriptor PnpDevice(
        string instanceId,
        Guid? containerId,
        string name,
        string deviceClass,
        string enumeratorName,
        bool isStarted = true,
        uint problemCode = 0) =>
        new(
            instanceId,
            containerId,
            name,
            deviceClass,
            enumeratorName,
            $"{enumeratorName}\\TEST",
            isStarted,
            problemCode);

    private static DisplayPathDescriptor DisplayPath(
        long adapterId,
        uint sourceId,
        uint targetId,
        DisplayOutputTechnology technology,
        bool active,
        bool available) =>
        new(adapterId, sourceId, targetId, technology, active, available);

    private static DisplayTopologySnapshot Topology(
        DisplayMode? mode,
        int? activeDisplayCount = null,
        int availableDisplayCount = 2)
    {
        int activeCount = activeDisplayCount ??
            (mode is DisplayMode.Duplicate or DisplayMode.Extend ? 2 : 1);

        return new(
            mode,
            ActiveDisplayCount: activeCount,
            AvailableDisplayCount: availableDisplayCount,
            IsReliable: true,
            Status: mode?.GetDisplayName() ?? "Updating");
    }

    private static async Task MapsMode(DisplayMode mode, uint topologyFlag)
    {
        FakeDisplayConfigNative native = new();
        WindowsDisplayModeService service = CreateDisplayModeService(native);

        await service.ApplyAsync(mode);

        Equal(WindowsDisplayModeService.SdcApply | topologyFlag, native.LastFlags);
        Equal(1, native.CallCount);
    }

    private static async Task ExtendUsesWindowsProjectionCommand()
    {
        FakeDisplayConfigNative native = new();
        FakeDisplaySwitchProcess displaySwitch = new();
        WindowsDisplayModeService service =
            CreateDisplayModeService(native, displaySwitch);

        DisplayModeResult result = await service.ApplyAsync(DisplayMode.Extend);

        True(result.Succeeded, "The Windows projection command should succeed.");
        Equal(0, native.CallCount);
        Equal(1, displaySwitch.CallCount);
        Equal("/extend", displaySwitch.LastArguments);
        Contains("DisplaySwitch.exe", displaySwitch.LastExecutablePath);
    }

    private static async Task LeavingExtendCapturesLayout()
    {
        FakeDisplayConfigNative native = new();
        FakeExtendedDisplayLayoutRestorer layoutRestorer = new()
        {
            CaptureResult = true,
        };
        WindowsDisplayModeService service =
            CreateDisplayModeService(native, layoutRestorer: layoutRestorer);

        await service.ApplyAsync(DisplayMode.SecondScreenOnly);

        Equal(1, layoutRestorer.CaptureCallCount);
        True(
            service.CanRestoreExtendedLayout,
            "A successfully captured layout should be available for restore.");
    }

    private static async Task CapturedExtendLayoutBypassesProjectionCommand()
    {
        FakeDisplayConfigNative native = new();
        FakeDisplaySwitchProcess displaySwitch = new();
        FakeExtendedDisplayLayoutRestorer layoutRestorer = new()
        {
            HasCapturedLayout = true,
            ApplyResult = 0,
        };
        WindowsDisplayModeService service = CreateDisplayModeService(
            native,
            displaySwitch,
            layoutRestorer);

        DisplayModeResult result = await service.ApplyAsync(DisplayMode.Extend);

        True(result.Succeeded, "The captured layout should restore successfully.");
        Equal(1, layoutRestorer.ApplyCallCount);
        Equal(0, displaySwitch.CallCount);
    }

    private static async Task FailedCapturedLayoutFallsBackToProjection()
    {
        FakeDisplayConfigNative native = new();
        FakeDisplaySwitchProcess displaySwitch = new();
        FakeExtendedDisplayLayoutRestorer layoutRestorer = new()
        {
            HasCapturedLayout = true,
            ApplyResult = 87,
        };
        WindowsDisplayModeService service = CreateDisplayModeService(
            native,
            displaySwitch,
            layoutRestorer);

        DisplayModeResult result = await service.ApplyAsync(DisplayMode.Extend);

        True(result.Succeeded, "Projection should remain a safe fallback.");
        Equal(1, layoutRestorer.ApplyCallCount);
        Equal(1, displaySwitch.CallCount);
    }

    private static async Task ZeroDisplayConfigResultSucceeds()
    {
        FakeDisplayConfigNative native = new() { ResultCode = 0 };
        DisplayModeResult result =
            await CreateDisplayModeService(native).ApplyAsync(
                DisplayMode.Duplicate);

        True(result.Succeeded, "Result code zero should be successful.");
        Equal(0, result.ErrorCode);
        Contains("Duplicate", result.Message);
    }

    private static async Task NonZeroDisplayConfigResultFails()
    {
        FakeDisplayConfigNative native = new() { ResultCode = 5 };
        DisplayModeResult result =
            await CreateDisplayModeService(native).ApplyAsync(
                DisplayMode.Duplicate);

        True(!result.Succeeded, "A non-zero result code should fail.");
        Equal(5, result.ErrorCode);
        Contains("error code 5", result.Message);
    }

    private static async Task NativeDisplayFailureBecomesResult()
    {
        FakeDisplayConfigNative native = new()
        {
            Exception = new InvalidOperationException("test failure"),
        };

        DisplayModeResult result =
            await CreateDisplayModeService(native).ApplyAsync(
                DisplayMode.Duplicate);

        True(!result.Succeeded, "A native exception should return a failure result.");
        Contains("test failure", result.Message);
    }

    private static async Task CancellationIsPreserved()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        FakeDisplayConfigNative native = new();

        await ThrowsAsync<OperationCanceledException>(
            () => CreateDisplayModeService(native).ApplyAsync(
                DisplayMode.Extend,
                cancellation.Token));
        Equal(0, native.CallCount);
    }

    private static async Task UnknownModeIsRejected()
    {
        FakeDisplayConfigNative native = new();

        await ThrowsAsync<ArgumentOutOfRangeException>(
            () => CreateDisplayModeService(native).ApplyAsync(
                (DisplayMode)999));

        Equal(0, native.CallCount);
    }

    private static async Task DisplayCallRunsOffCallingThread()
    {
        int callingThreadId = Environment.CurrentManagedThreadId;
        FakeDisplayConfigNative native = new();

        await CreateDisplayModeService(native).ApplyAsync(
            DisplayMode.Duplicate);

        True(
            native.ApplyThreadId != callingThreadId,
            "SetDisplayConfig should not block the calling UI thread.");
    }

    private static WindowsDisplayModeService CreateDisplayModeService(
        FakeDisplayConfigNative native,
        FakeDisplaySwitchProcess? displaySwitch = null,
        FakeExtendedDisplayLayoutRestorer? layoutRestorer = null) =>
        new(
            native,
            layoutRestorer ?? new FakeExtendedDisplayLayoutRestorer(),
            displaySwitch ?? new FakeDisplaySwitchProcess(),
            @"C:\Windows\System32\DisplaySwitch.exe");

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new TestFailureException(message);
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new TestFailureException($"Expected '{expected}', received '{actual}'.");
        }
    }

    private static void Contains(string expectedSubstring, string actual)
    {
        if (!actual.Contains(expectedSubstring, StringComparison.Ordinal))
        {
            throw new TestFailureException(
                $"Expected '{actual}' to contain '{expectedSubstring}'.");
        }
    }

    private static async Task ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new TestFailureException($"Expected {typeof(TException).Name}.");
    }

    private sealed class TrackingProfileStore(ProfileCatalog catalog) : IProfileStore
    {
        private readonly ProfileCatalog _catalog = catalog;

        public int LoadCallCount { get; private set; }

        public int SaveCallCount { get; private set; }

        public ProfileCatalog Load()
        {
            LoadCallCount++;
            return _catalog;
        }

        public bool TrySave(ProfileCatalog candidate, out string? errorMessage)
        {
            SaveCallCount++;
            errorMessage = null;
            return true;
        }
    }

    private sealed class TestFailureException(string message) : Exception(message);

    private sealed class FakeDisplayConfigNative : IDisplayConfigNative
    {
        public int ResultCode { get; init; }

        public Exception? Exception { get; init; }

        public int CallCount { get; private set; }

        public uint LastFlags { get; private set; }

        public int ApplyThreadId { get; private set; }

        public int Apply(uint flags)
        {
            CallCount++;
            LastFlags = flags;
            ApplyThreadId = Environment.CurrentManagedThreadId;

            if (Exception is not null)
            {
                throw Exception;
            }

            return ResultCode;
        }
    }

    private sealed class FakeDisplaySwitchProcess : IDisplaySwitchProcess
    {
        public int ResultCode { get; init; }

        public int CallCount { get; private set; }

        public string LastExecutablePath { get; private set; } = string.Empty;

        public string LastArguments { get; private set; } = string.Empty;

        public Task<int> RunAsync(
            string executablePath,
            string arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastExecutablePath = executablePath;
            LastArguments = arguments;
            return Task.FromResult(ResultCode);
        }
    }

    private sealed class FakeExtendedDisplayLayoutRestorer :
        IExtendedDisplayLayoutRestorer
    {
        public bool HasCapturedLayout { get; set; }

        public bool CaptureResult { get; init; }

        public int? ApplyResult { get; init; }

        public int CaptureCallCount { get; private set; }

        public int ApplyCallCount { get; private set; }

        public bool TryCaptureCurrent()
        {
            CaptureCallCount++;

            if (CaptureResult)
            {
                HasCapturedLayout = true;
            }

            return CaptureResult;
        }

        public int? ApplyCaptured()
        {
            ApplyCallCount++;
            return HasCapturedLayout ? ApplyResult : null;
        }
    }

    private sealed class FakeDisplayTopologyService(
        params DisplayTopologySnapshot[] snapshots) : IDisplayTopologyService
    {
        private readonly Queue<DisplayTopologySnapshot> _snapshots = new(snapshots);
        private DisplayTopologySnapshot _last = snapshots.First();

        public int CallCount { get; private set; }

        public DisplayTopologySnapshot GetSnapshot()
        {
            CallCount++;

            if (_snapshots.TryDequeue(out DisplayTopologySnapshot? snapshot))
            {
                _last = snapshot;
            }

            return _last;
        }
    }

    private sealed class FakeDisplayModeService(
        bool canRestoreExtendedLayout,
        params DisplayModeResult[] results) : IDisplayModeService
    {
        private readonly Queue<DisplayModeResult> _results = new(results);

        public bool CanRestoreExtendedLayout { get; } = canRestoreExtendedLayout;

        public List<DisplayMode> AppliedModes { get; } = [];

        public Task<DisplayModeResult> ApplyAsync(
            DisplayMode mode,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppliedModes.Add(mode);

            return Task.FromResult(
                _results.TryDequeue(out DisplayModeResult? result)
                    ? result
                    : new DisplayModeResult(true, $"{mode.GetDisplayName()} accepted."));
        }
    }

    private sealed class FakeAudioSessionMutationBackend : IAudioSessionMutationBackend
    {
        public int VolumeCallCount { get; private set; }

        public int MuteCallCount { get; private set; }

        public string SessionId { get; private set; } = string.Empty;

        public float Volume { get; private set; }

        public bool IsMuted { get; private set; }

        public AudioControlResult SetVolume(
            string sessionId,
            float volume,
            CancellationToken cancellationToken)
        {
            VolumeCallCount++;
            SessionId = sessionId;
            Volume = volume;
            return AudioControlResult.Success("updated");
        }

        public AudioControlResult SetMute(
            string sessionId,
            bool isMuted,
            CancellationToken cancellationToken)
        {
            MuteCallCount++;
            SessionId = sessionId;
            IsMuted = isMuted;
            return AudioControlResult.Success("updated");
        }
    }

    private sealed class FakeAudioEndpointVolumeBackend : IAudioEndpointVolumeBackend
    {
        public int VolumeCallCount { get; private set; }

        public int MuteCallCount { get; private set; }

        public float Volume { get; private set; }

        public bool IsMuted { get; private set; }

        public AudioEndpointControlSnapshot? Snapshot { get; init; }

        public AudioEndpointControlSnapshot? GetState(
            string endpointId,
            CancellationToken cancellationToken) => Snapshot;

        public AudioControlResult SetMasterVolume(
            string endpointId,
            string endpointName,
            float volume,
            CancellationToken cancellationToken)
        {
            VolumeCallCount++;
            Volume = volume;
            return AudioControlResult.Success("updated");
        }

        public AudioControlResult SetMute(
            string endpointId,
            string endpointName,
            bool isMuted,
            CancellationToken cancellationToken)
        {
            MuteCallCount++;
            IsMuted = isMuted;
            return AudioControlResult.Success("updated");
        }
    }

    private sealed class FakeDefaultAudioEndpointSetter : IDefaultAudioEndpointSetter
    {
        public List<(string EndpointId, AudioRole Role)> Calls { get; } = [];

        public Exception? Exception { get; init; }

        public void SetDefaultEndpoint(string endpointId, AudioRole role)
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            Calls.Add((endpointId, role));
        }
    }

    private sealed class FakeQuickSwitchCoordinator : IQuickSwitchCoordinator
    {
        private QuickSwitchState _state;

        public FakeQuickSwitchCoordinator(QuickSwitchState? state = null)
        {
            _state = state ?? QuickSwitchState.Empty;
        }

        public QuickSwitchState State => _state;

        public List<QuickSwitchCommand> Commands { get; } = [];

        public Exception? RefreshException { get; set; }

        public int RefreshCount { get; private set; }

        public event EventHandler<QuickSwitchStateChangedEventArgs>? StateChanged;

        public Task<QuickSwitchResult> ExecuteAsync(
            QuickSwitchCommand command,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.FromResult(QuickSwitchResult.Info("Test command."));
        }

        public void PublishState(
            QuickSwitchState state,
            QuickSwitchRefreshScope scope = QuickSwitchRefreshScope.All,
            long revision = 0)
        {
            _state = state;
            StateChanged?.Invoke(
                this,
                new QuickSwitchStateChangedEventArgs(_state, scope, revision));
        }

        public Task RefreshAsync(
            QuickSwitchRefreshScope scope,
            CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            if (RefreshException is not null)
            {
                throw RefreshException;
            }

            PublishState(State, scope);
            return Task.CompletedTask;
        }

        public IDisposable AcquireSurfaceDemand(QuickSwitchSurfaceDemand demand) =>
            NoopDisposable.Instance;
    }

    private sealed class RevisionTrackingFeatureViewModel(
        IQuickSwitchCoordinator coordinator)
        : SharedFeatureViewModel(coordinator, QuickSwitchSurfaceDemand.None)
    {
        public string StatusMessage { get; private set; } = string.Empty;

        public int ApplyCount { get; private set; }

        protected override QuickSwitchRefreshScope RefreshScope =>
            QuickSwitchRefreshScope.Options;

        protected override void ApplyState(QuickSwitchState state)
        {
            StatusMessage = state.Options.StatusMessage;
            ApplyCount++;
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed class FakeWindowsSettingsLauncher : IWindowsSettingsLauncher
    {
        public List<string> Uris { get; } = [];

        public void Open(string settingsUri) => Uris.Add(settingsUri);
    }

    private sealed class FakeDeviceSettingsLauncher : IDeviceSettingsLauncher
    {
        public List<string> Uris { get; } = [];

        public void Open(string settingsUri) => Uris.Add(settingsUri);
    }

    private sealed class FakeTaskbarBackend : ITaskbarBackend
    {
        public uint State { get; set; }

        public Exception? Exception { get; init; }

        public bool SetResult { get; init; } = true;

        public List<uint> SetCalls { get; } = [];

        public uint GetState()
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            return State;
        }

        public bool SetState(uint state)
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            SetCalls.Add(state);
            State = state;
            return SetResult;
        }
    }

    private sealed class FakeTaskbarSettingsLauncher : ITaskbarSettingsLauncher
    {
        public List<string> Uris { get; } = [];

        public void Open(string uri) => Uris.Add(uri);
    }

    private sealed class FakeWirelessRadioBackend : IWirelessRadioBackend
    {
        public IReadOnlyList<WirelessRadioDevice> Radios { get; init; } = [];

        public WirelessRadioControlStatus ControlStatus { get; init; } =
            WirelessRadioControlStatus.Allowed;

        public Exception? Exception { get; init; }

        public List<(WirelessRadioKind Kind, bool Enabled)> SetCalls { get; } = [];

        public Task<IReadOnlyList<WirelessRadioDevice>> GetRadiosAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Radios);
        }

        public Task<WirelessRadioControlStatus> SetStateAsync(
            WirelessRadioKind kind,
            bool enabled,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Exception is not null)
            {
                throw Exception;
            }

            SetCalls.Add((kind, enabled));
            return Task.FromResult(ControlStatus);
        }
    }

    private sealed class FakeDwmAttributeSetter : IDwmAttributeSetter
    {
        public List<(IntPtr Handle, int Attribute, int Value)> Calls { get; } = [];

        public int RefreshCount { get; private set; }

        public void Set(IntPtr windowHandle, int attribute, int value) =>
            Calls.Add((windowHandle, attribute, value));

        public void RefreshFrame(IntPtr windowHandle) => RefreshCount++;
    }

    private sealed class FakeGlobalHotkeyNative : IGlobalHotkeyNative
    {
        public int? FailingVirtualKey { get; set; }

        public List<(IntPtr Handle, int Id, uint Modifiers, uint VirtualKey)> Registrations
        {
            get;
        } = [];

        public List<(IntPtr Handle, int Id)> Unregistrations { get; } = [];

        public Dictionary<int, (IntPtr Handle, int Id, uint Modifiers, uint VirtualKey)>
            ActiveRegistrations { get; } = [];

        public bool Register(
            IntPtr windowHandle,
            int id,
            uint modifiers,
            uint virtualKey,
            out int errorCode)
        {
            if (virtualKey == FailingVirtualKey)
            {
                errorCode = 1409;
                return false;
            }

            (IntPtr Handle, int Id, uint Modifiers, uint VirtualKey) registration =
                (windowHandle, id, modifiers, virtualKey);
            Registrations.Add(registration);
            ActiveRegistrations[id] = registration;
            errorCode = 0;
            return true;
        }

        public void Unregister(IntPtr windowHandle, int id)
        {
            Unregistrations.Add((windowHandle, id));
            ActiveRegistrations.Remove(id);
        }
    }

    private sealed class FakeStartupRegistry : IStartupRegistry
    {
        public string? Value { get; set; }

        public Exception? Exception { get; init; }

        public int DeleteCount { get; private set; }

        public string? ReadValue()
        {
            ThrowIfConfigured();
            return Value;
        }

        public void WriteValue(string command)
        {
            ThrowIfConfigured();
            Value = command;
        }

        public void DeleteValue()
        {
            ThrowIfConfigured();
            DeleteCount++;
            Value = null;
        }

        private void ThrowIfConfigured()
        {
            if (Exception is not null)
            {
                throw Exception;
            }
        }
    }
}

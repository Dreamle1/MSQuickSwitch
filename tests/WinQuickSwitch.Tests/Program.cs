using WinQuickSwitch.Features.Audio;
using WinQuickSwitch.Features.Display;
using WinQuickSwitch.Platform.Windows.Audio;
using WinQuickSwitch.Platform.Windows.Display;

namespace WinQuickSwitch.Tests;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        List<(string Name, Func<Task> Run)> tests =
        [
            ("PC screen only uses /internal", () => MapsMode(DisplayMode.PcScreenOnly, "/internal")),
            ("Duplicate uses /clone", () => MapsMode(DisplayMode.Duplicate, "/clone")),
            ("Extend uses /extend", () => MapsMode(DisplayMode.Extend, "/extend")),
            ("Second screen only uses /external", () => MapsMode(DisplayMode.SecondScreenOnly, "/external")),
            ("Zero exit code succeeds", ZeroExitCodeSucceeds),
            ("Non-zero exit code fails", NonZeroExitCodeFails),
            ("Process startup failure becomes a result", ProcessFailureBecomesResult),
            ("Cancellation is preserved", CancellationIsPreserved),
            ("Unknown modes are rejected before process execution", UnknownModeIsRejected),
            ("Hidden process runner returns the process exit code", HiddenProcessRunnerReturnsExitCode),
            ("Single internal display is classified as PC screen only", SingleInternalDisplayIsClassified),
            ("Single external display is classified as second screen only", SingleExternalDisplayIsClassified),
            ("Shared display source is classified as duplicate", SharedSourceIsClassifiedAsDuplicate),
            ("Distinct display sources are classified as extend", DistinctSourcesAreClassifiedAsExtend),
            ("Mixed display sources are not mislabeled", MixedSourcesAreNotMislabeled),
            ("Inactive available display enables multi-display choices", AvailableInactiveDisplayEnablesChoices),
            ("No active display produces an unreliable snapshot", NoActiveDisplayIsUnreliable),
            ("Audio endpoint labels use the friendly name", AudioEndpointLabelsUseFriendlyName),
            ("Audio session volume is formatted and clamped", AudioSessionVolumeIsFormatted),
            ("Audio refresh notifications are debounced", AudioRefreshNotificationsAreDebounced),
            ("Disposed audio debounce cancels pending work", DisposedAudioDebounceCancelsWork),
        ];

        if (args.Contains("--integration", StringComparer.OrdinalIgnoreCase))
        {
            tests.Add(("Live display topology can be read", LiveDisplayTopologyCanBeRead));
            tests.Add(("Live Core Audio inventory can be read", LiveAudioInventoryCanBeRead));
            tests.Add(("Live audio notification watcher starts and stops", LiveAudioWatcherStartsAndStops));
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
        return Task.CompletedTask;
    }

    private static DisplayPathDescriptor DisplayPath(
        long adapterId,
        uint sourceId,
        uint targetId,
        DisplayOutputTechnology technology,
        bool active,
        bool available) =>
        new(adapterId, sourceId, targetId, technology, active, available);

    private static async Task MapsMode(DisplayMode mode, string expectedArgument)
    {
        FakeDisplaySwitchProcess process = new();
        WindowsDisplayModeService service = CreateService(process);

        await service.ApplyAsync(mode);

        Equal(expectedArgument, process.LastArguments);
        True(process.CallCount == 1, "The process should run exactly once.");
        True(
            process.LastExecutablePath.EndsWith("DisplaySwitch.exe", StringComparison.OrdinalIgnoreCase),
            "The service should invoke DisplaySwitch.exe.");
    }

    private static async Task ZeroExitCodeSucceeds()
    {
        FakeDisplaySwitchProcess process = new() { ExitCode = 0 };
        DisplayModeResult result = await CreateService(process).ApplyAsync(DisplayMode.Extend);

        True(result.Succeeded, "Exit code zero should be successful.");
        Equal(0, result.ExitCode);
        Contains("Extend", result.Message);
    }

    private static async Task NonZeroExitCodeFails()
    {
        FakeDisplaySwitchProcess process = new() { ExitCode = 5 };
        DisplayModeResult result = await CreateService(process).ApplyAsync(DisplayMode.Duplicate);

        True(!result.Succeeded, "A non-zero exit code should fail.");
        Equal(5, result.ExitCode);
        Contains("exit code 5", result.Message);
    }

    private static async Task ProcessFailureBecomesResult()
    {
        FakeDisplaySwitchProcess process = new()
        {
            Exception = new InvalidOperationException("test failure"),
        };

        DisplayModeResult result = await CreateService(process).ApplyAsync(DisplayMode.Extend);

        True(!result.Succeeded, "A process exception should return a failure result.");
        Contains("test failure", result.Message);
    }

    private static async Task CancellationIsPreserved()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        FakeDisplaySwitchProcess process = new()
        {
            Exception = new OperationCanceledException(cancellation.Token),
        };

        await ThrowsAsync<OperationCanceledException>(
            () => CreateService(process).ApplyAsync(DisplayMode.Extend, cancellation.Token));
    }

    private static async Task UnknownModeIsRejected()
    {
        FakeDisplaySwitchProcess process = new();

        await ThrowsAsync<ArgumentOutOfRangeException>(
            () => CreateService(process).ApplyAsync((DisplayMode)999));

        Equal(0, process.CallCount);
    }

    private static async Task HiddenProcessRunnerReturnsExitCode()
    {
        string commandProcessor = Environment.GetEnvironmentVariable("ComSpec")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "cmd.exe");

        HiddenProcessRunner process = new();
        int exitCode = await process.RunAsync(
            commandProcessor,
            "/d /c exit 7",
            CancellationToken.None);

        Equal(7, exitCode);
    }

    private static WindowsDisplayModeService CreateService(FakeDisplaySwitchProcess process) =>
        new(process, @"C:\Windows\System32\DisplaySwitch.exe");

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

    private sealed class TestFailureException(string message) : Exception(message);

    private sealed class FakeDisplaySwitchProcess : IDisplaySwitchProcess
    {
        public int ExitCode { get; init; }

        public Exception? Exception { get; init; }

        public int CallCount { get; private set; }

        public string LastExecutablePath { get; private set; } = string.Empty;

        public string LastArguments { get; private set; } = string.Empty;

        public Task<int> RunAsync(
            string executablePath,
            string arguments,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastExecutablePath = executablePath;
            LastArguments = arguments;

            return Exception is null
                ? Task.FromResult(ExitCode)
                : Task.FromException<int>(Exception);
        }
    }
}

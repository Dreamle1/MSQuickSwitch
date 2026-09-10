using WinQuickSwitch.Features.Audio;

namespace WinQuickSwitch.Tests;

internal static class AudioQuickActionTests
{
    internal static IReadOnlyList<(string Name, Func<Task> Run)> GetTests() =>
    [
        ("Audio quick actions clamp master volume", VolumeChangesClampAtBothBounds),
        ("Audio quick actions target output and microphone mute", MuteActionsTargetTheCorrectEndpointKind),
        ("Audio quick actions prefer multimedia defaults", MultimediaDefaultsWinAndConsoleDefaultsFallback),
        ("Audio quick actions reject missing or ambiguous defaults", MissingAndAmbiguousDefaultsAreRejected),
        ("Audio quick actions honor cancellation before mutation", CancellationDoesNotReachMutation),
        ("Audio quick actions reject overlapping calls", OverlappingCallsReturnBusyWithoutQueueing),
        ("Audio quick actions enforce fake-clock rate limits", AcceptedCallsAreRateLimitedByTheFakeClock),
        ("Audio quick actions reject invalid input and state", InvalidInputsAndEndpointStateAreRejected),
        ("Audio quick actions sanitize endpoint exceptions", ExceptionsAreSanitized),
    ];

    internal static async Task Run()
    {
        foreach ((_, Func<Task> run) in GetTests())
        {
            await run();
        }
    }

    private static async Task VolumeChangesClampAtBothBounds()
    {
        ManualTimeProvider clock = new();
        FakeAudioInventoryService inventory = new(CreateInventory(
            playback:
            [
                Endpoint(
                    "speakers",
                    "Desk speakers",
                    AudioEndpointKind.Playback,
                    multimediaDefault: true),
            ]));
        FakeAudioEndpointControlService endpoint = new();
        endpoint.States["speakers"] = new(0.95f, false);
        AudioQuickActionExecutor executor = new(inventory, endpoint, clock);

        AudioControlResult up = await executor.ExecuteAsync(
            AudioQuickAction.VolumeUp,
            10);

        True(up.Succeeded, up.Message);
        Equal(1f, endpoint.VolumeCalls.Single().Volume);

        clock.Advance(TimeSpan.FromMilliseconds(150));
        endpoint.States["speakers"] = new(0.05f, false);

        AudioControlResult down = await executor.ExecuteAsync(
            AudioQuickAction.VolumeDown,
            10);

        True(down.Succeeded, down.Message);
        Equal(0f, endpoint.VolumeCalls.Last().Volume);
    }

    private static async Task MuteActionsTargetTheCorrectEndpointKind()
    {
        ManualTimeProvider clock = new();
        FakeAudioInventoryService inventory = new(CreateInventory(
            playback:
            [
                Endpoint(
                    "output",
                    "Desk speakers",
                    AudioEndpointKind.Playback,
                    multimediaDefault: true),
            ],
            recording:
            [
                Endpoint(
                    "microphone",
                    "Desk microphone",
                    AudioEndpointKind.Recording,
                    multimediaDefault: true),
            ]));
        FakeAudioEndpointControlService endpoint = new();
        endpoint.States["output"] = new(0.5f, false);
        endpoint.States["microphone"] = new(0.5f, false);
        AudioQuickActionExecutor executor = new(inventory, endpoint, clock);

        AudioControlResult output = await executor.ExecuteAsync(
            AudioQuickAction.ToggleOutputMute,
            5);

        True(output.Succeeded, output.Message);
        Equal("output", endpoint.MuteCalls.Single().EndpointId);
        True(endpoint.MuteCalls.Single().IsMuted, "Output should be muted.");

        clock.Advance(TimeSpan.FromMilliseconds(150));

        AudioControlResult microphone = await executor.ExecuteAsync(
            AudioQuickAction.ToggleMicrophoneMute,
            5);

        True(microphone.Succeeded, microphone.Message);
        Equal("microphone", endpoint.MuteCalls.Last().EndpointId);
        True(
            endpoint.MuteCalls.Last().IsMuted,
            "The microphone should be muted.");
    }

    private static async Task MultimediaDefaultsWinAndConsoleDefaultsFallback()
    {
        ManualTimeProvider clock = new();
        AudioEndpointInfo console = Endpoint(
            "console",
            "Console device",
            AudioEndpointKind.Playback,
            consoleDefault: true);
        AudioEndpointInfo multimedia = Endpoint(
            "multimedia",
            "Multimedia device",
            AudioEndpointKind.Playback,
            multimediaDefault: true);
        FakeAudioInventoryService inventory = new(CreateInventory(
            playback: [console, multimedia]));
        FakeAudioEndpointControlService endpoint = new();
        endpoint.States[console.Id] = new(0.5f, false);
        endpoint.States[multimedia.Id] = new(0.5f, false);
        AudioQuickActionExecutor executor = new(inventory, endpoint, clock);

        AudioControlResult multimediaResult = await executor.ExecuteAsync(
            AudioQuickAction.VolumeUp,
            5);

        True(multimediaResult.Succeeded, multimediaResult.Message);
        Equal("multimedia", endpoint.VolumeCalls.Single().EndpointId);

        clock.Advance(TimeSpan.FromMilliseconds(150));
        inventory.Inventory = CreateInventory(playback: [console]);

        AudioControlResult consoleResult = await executor.ExecuteAsync(
            AudioQuickAction.VolumeUp,
            5);

        True(consoleResult.Succeeded, consoleResult.Message);
        Equal("console", endpoint.VolumeCalls.Last().EndpointId);

        clock.Advance(TimeSpan.FromMilliseconds(150));
        inventory.Inventory = CreateInventory(
            playback:
            [
                Endpoint(
                    "calls-only",
                    "Calls-only device",
                    AudioEndpointKind.Playback,
                    communicationsDefault: true),
            ]);

        AudioControlResult callsOnlyResult = await executor.ExecuteAsync(
            AudioQuickAction.VolumeUp,
            5);

        True(!callsOnlyResult.Succeeded, "Calls-only endpoints are not eligible.");
        Equal(2, endpoint.VolumeCalls.Count);
    }

    private static async Task MissingAndAmbiguousDefaultsAreRejected()
    {
        ManualTimeProvider clock = new();
        FakeAudioInventoryService inventory = new(CreateInventory());
        FakeAudioEndpointControlService endpoint = new();
        AudioQuickActionExecutor executor = new(inventory, endpoint, clock);

        AudioControlResult absent = await executor.ExecuteAsync(
            AudioQuickAction.VolumeUp,
            5);

        True(!absent.Succeeded, "A missing default should fail.");
        Equal(0, endpoint.GetStateCalls.Count);

        clock.Advance(TimeSpan.FromMilliseconds(150));
        inventory.Inventory = CreateInventory(
            playback:
            [
                Endpoint(
                    "multimedia-1",
                    "First device",
                    AudioEndpointKind.Playback,
                    multimediaDefault: true),
                Endpoint(
                    "multimedia-2",
                    "Second device",
                    AudioEndpointKind.Playback,
                    multimediaDefault: true),
            ]);

        AudioControlResult ambiguousMultimedia = await executor.ExecuteAsync(
            AudioQuickAction.VolumeUp,
            5);

        True(
            !ambiguousMultimedia.Succeeded,
            "Ambiguous multimedia defaults should fail.");
        Equal(0, endpoint.GetStateCalls.Count);

        clock.Advance(TimeSpan.FromMilliseconds(150));
        inventory.Inventory = CreateInventory(
            playback:
            [
                Endpoint(
                    "console-1",
                    "First console device",
                    AudioEndpointKind.Playback,
                    consoleDefault: true),
                Endpoint(
                    "console-2",
                    "Second console device",
                    AudioEndpointKind.Playback,
                    consoleDefault: true),
            ]);

        AudioControlResult ambiguousConsole = await executor.ExecuteAsync(
            AudioQuickAction.VolumeUp,
            5);

        True(
            !ambiguousConsole.Succeeded,
            "Ambiguous console fallbacks should fail.");
        Equal(0, endpoint.GetStateCalls.Count);
    }

    private static async Task CancellationDoesNotReachMutation()
    {
        FakeAudioInventoryService inventory = new(CreateInventory(
            playback:
            [
                Endpoint(
                    "speakers",
                    "Desk speakers",
                    AudioEndpointKind.Playback,
                    multimediaDefault: true),
            ]));
        FakeAudioEndpointControlService endpoint = new();
        endpoint.States["speakers"] = new(0.5f, false);
        using CancellationTokenSource cancellation = new();
        endpoint.BeforeGetStateAsync = _ =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };
        AudioQuickActionExecutor executor = new(inventory, endpoint);

        await AssertCanceled(
            () => executor.ExecuteAsync(
                AudioQuickAction.VolumeUp,
                5,
                cancellation.Token),
            "Cancellation should propagate from the state read.");

        Equal(0, endpoint.VolumeCalls.Count);
        Equal(0, endpoint.MuteCalls.Count);
    }

    private static async Task OverlappingCallsReturnBusyWithoutQueueing()
    {
        FakeAudioInventoryService inventory = new(CreateInventory(
            playback:
            [
                Endpoint(
                    "speakers",
                    "Desk speakers",
                    AudioEndpointKind.Playback,
                    multimediaDefault: true),
            ]));
        FakeAudioEndpointControlService endpoint = new();
        endpoint.States["speakers"] = new(0.5f, false);
        TaskCompletionSource stateRead = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseStateRead = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        endpoint.BeforeGetStateAsync = async cancellationToken =>
        {
            stateRead.TrySetResult();
            await releaseStateRead.Task.WaitAsync(cancellationToken);
        };
        AudioQuickActionExecutor executor = new(inventory, endpoint);

        Task<AudioControlResult> firstTask = executor.ExecuteAsync(
            AudioQuickAction.VolumeUp,
            5);
        await stateRead.Task.WaitAsync(TimeSpan.FromSeconds(1));

        AudioControlResult busy = await executor.ExecuteAsync(
            AudioQuickAction.VolumeUp,
            5);

        True(!busy.Succeeded, "The overlapping call should be rejected.");
        Contains("already in progress", busy.Message);
        Equal(0, endpoint.VolumeCalls.Count);

        releaseStateRead.TrySetResult();
        AudioControlResult first = await firstTask.WaitAsync(
            TimeSpan.FromSeconds(1));
        True(first.Succeeded, first.Message);
        Equal(1, endpoint.VolumeCalls.Count);
    }

    private static async Task AcceptedCallsAreRateLimitedByTheFakeClock()
    {
        ManualTimeProvider clock = new();
        FakeAudioInventoryService inventory = new(CreateInventory(
            playback:
            [
                Endpoint(
                    "speakers",
                    "Desk speakers",
                    AudioEndpointKind.Playback,
                    multimediaDefault: true),
            ]));
        FakeAudioEndpointControlService endpoint = new();
        endpoint.States["speakers"] = new(0.5f, false);
        AudioQuickActionExecutor executor = new(inventory, endpoint, clock);

        AudioControlResult first = await executor.ExecuteAsync(
            AudioQuickAction.VolumeUp,
            5);
        AudioControlResult immediate = await executor.ExecuteAsync(
            AudioQuickAction.VolumeUp,
            5);

        True(first.Succeeded, first.Message);
        True(!immediate.Succeeded, "A repeated call should be rate-limited.");
        Contains("rate-limited", immediate.Message);
        Equal(1, inventory.CallCount);
        Equal(1, endpoint.VolumeCalls.Count);

        clock.Advance(TimeSpan.FromMilliseconds(149));
        AudioControlResult beforeWindow = await executor.ExecuteAsync(
            AudioQuickAction.VolumeUp,
            5);
        True(!beforeWindow.Succeeded, "The rate window should still be active.");

        clock.Advance(TimeSpan.FromMilliseconds(1));
        AudioControlResult afterWindow = await executor.ExecuteAsync(
            AudioQuickAction.VolumeUp,
            5);
        True(afterWindow.Succeeded, afterWindow.Message);
        Equal(2, endpoint.VolumeCalls.Count);
    }

    private static async Task InvalidInputsAndEndpointStateAreRejected()
    {
        FakeAudioInventoryService inventory = new(CreateInventory(
            playback:
            [
                Endpoint(
                    "speakers",
                    "Desk speakers",
                    AudioEndpointKind.Playback,
                    multimediaDefault: true),
            ]));
        FakeAudioEndpointControlService endpoint = new();
        endpoint.States["speakers"] = new(0.5f, false);
        AudioQuickActionExecutor executor = new(inventory, endpoint);

        AudioControlResult unknownAction = await executor.ExecuteAsync(
            (AudioQuickAction)999,
            5);
        AudioControlResult zeroStep = await executor.ExecuteAsync(
            AudioQuickAction.VolumeUp,
            0);
        AudioControlResult largeStep = await executor.ExecuteAsync(
            AudioQuickAction.VolumeUp,
            21);

        True(!unknownAction.Succeeded, "Unknown actions should fail.");
        True(!zeroStep.Succeeded, "A zero volume step should fail.");
        True(!largeStep.Succeeded, "An oversized volume step should fail.");
        Equal(0, inventory.CallCount);

        endpoint.States.Clear();
        AudioControlResult missingState = await executor.ExecuteAsync(
            AudioQuickAction.VolumeUp,
            5);
        True(!missingState.Succeeded, "A missing endpoint state should fail.");
        Equal(0, endpoint.VolumeCalls.Count);

        ManualTimeProvider invalidStateClock = new();
        FakeAudioEndpointControlService invalidStateEndpoint = new();
        invalidStateEndpoint.States["speakers"] =
            new(float.NaN, false);
        AudioQuickActionExecutor invalidStateExecutor = new(
            inventory,
            invalidStateEndpoint,
            invalidStateClock);
        AudioControlResult invalidState = await invalidStateExecutor.ExecuteAsync(
            AudioQuickAction.VolumeDown,
            5);

        True(!invalidState.Succeeded, "A NaN master volume should fail.");
        Equal(0, invalidStateEndpoint.VolumeCalls.Count);
    }

    private static async Task ExceptionsAreSanitized()
    {
        FakeAudioInventoryService inventory = new(CreateInventory(
            playback:
            [
                Endpoint(
                    "endpoint-id-secret",
                    "Private endpoint",
                    AudioEndpointKind.Playback,
                    multimediaDefault: true),
            ]));
        FakeAudioEndpointControlService endpoint = new();
        endpoint.States["endpoint-id-secret"] = new(0.5f, false);
        endpoint.SetVolumeException = new InvalidOperationException(
            "endpoint-id-secret COM detail");
        AudioQuickActionExecutor executor = new(inventory, endpoint);

        AudioControlResult result = await executor.ExecuteAsync(
            AudioQuickAction.VolumeUp,
            5);

        True(!result.Succeeded, "The endpoint exception should fail the action.");
        Contains("could not be completed", result.Message);
        True(
            !result.Message.Contains(
                "endpoint-id-secret",
                StringComparison.OrdinalIgnoreCase),
            "The raw endpoint ID must not escape through the result.");
    }

    private static AudioInventory CreateInventory(
        IReadOnlyList<AudioEndpointInfo>? playback = null,
        IReadOnlyList<AudioEndpointInfo>? recording = null) =>
        new(
            playback ?? Array.Empty<AudioEndpointInfo>(),
            recording ?? Array.Empty<AudioEndpointInfo>(),
            Array.Empty<AudioSessionInfo>(),
            DateTimeOffset.UnixEpoch);

    private static AudioEndpointInfo Endpoint(
        string id,
        string name,
        AudioEndpointKind kind,
        bool consoleDefault = false,
        bool multimediaDefault = false,
        bool communicationsDefault = false) =>
        new(
            id,
            name,
            kind,
            consoleDefault,
            multimediaDefault,
            communicationsDefault);

    private static async Task AssertCanceled(
        Func<Task> action,
        string message)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Equal<T>(T expected, T actual, string message = "")
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(message)
                    ? $"Expected {expected}, received {actual}."
                    : message);
        }
    }

    private static void Contains(string expected, string actual)
    {
        True(
            actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            $"Expected '{actual}' to contain '{expected}'.");
    }

    private sealed class FakeAudioInventoryService(AudioInventory inventory) :
        IAudioInventoryService
    {
        public AudioInventory Inventory { get; set; } = inventory;

        public int CallCount { get; private set; }

        public Exception? Exception { get; set; }

        public Task<AudioInventory> GetInventoryAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            if (Exception is Exception exception)
            {
                throw exception;
            }

            return Task.FromResult(Inventory);
        }
    }

    private sealed class FakeAudioEndpointControlService :
        IAudioEndpointControlService
    {
        public Dictionary<string, AudioEndpointControlSnapshot> States { get; } =
            new(StringComparer.Ordinal);

        public List<string> GetStateCalls { get; } = [];

        public List<(string EndpointId, string EndpointName, float Volume)> VolumeCalls { get; } = [];

        public List<(string EndpointId, string EndpointName, bool IsMuted)> MuteCalls { get; } = [];

        public Func<CancellationToken, Task>? BeforeGetStateAsync { get; set; }

        public Exception? GetStateException { get; set; }

        public Exception? SetVolumeException { get; set; }

        public Exception? SetMuteException { get; set; }

        public async Task<AudioEndpointControlSnapshot?> GetStateAsync(
            string endpointId,
            CancellationToken cancellationToken = default)
        {
            GetStateCalls.Add(endpointId);
            if (BeforeGetStateAsync is not null)
            {
                await BeforeGetStateAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (GetStateException is Exception exception)
            {
                throw exception;
            }

            return States.TryGetValue(endpointId, out AudioEndpointControlSnapshot? state)
                ? state
                : null;
        }

        public Task<AudioControlResult> SetMasterVolumeAsync(
            string endpointId,
            string endpointName,
            float volume,
            CancellationToken cancellationToken = default)
        {
            VolumeCalls.Add((endpointId, endpointName, volume));
            cancellationToken.ThrowIfCancellationRequested();
            if (SetVolumeException is Exception exception)
            {
                throw exception;
            }

            AudioEndpointControlSnapshot current = States.TryGetValue(
                endpointId,
                out AudioEndpointControlSnapshot? state)
                ? state
                : new(0, false);
            States[endpointId] = current with { MasterVolume = volume };
            return Task.FromResult(AudioControlResult.Success("updated"));
        }

        public Task<AudioControlResult> SetMuteAsync(
            string endpointId,
            string endpointName,
            bool isMuted,
            CancellationToken cancellationToken = default)
        {
            MuteCalls.Add((endpointId, endpointName, isMuted));
            cancellationToken.ThrowIfCancellationRequested();
            if (SetMuteException is Exception exception)
            {
                throw exception;
            }

            AudioEndpointControlSnapshot current = States.TryGetValue(
                endpointId,
                out AudioEndpointControlSnapshot? state)
                ? state
                : new(0, false);
            States[endpointId] = current with { IsMuted = isMuted };
            return Task.FromResult(AudioControlResult.Success("updated"));
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration)
        {
            _timestamp += checked((long)(duration.TotalSeconds * TimestampFrequency));
        }
    }
}

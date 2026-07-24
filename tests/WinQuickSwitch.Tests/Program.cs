using WinQuickSwitch.Features.Display;
using WinQuickSwitch.Platform.Windows.Display;

namespace WinQuickSwitch.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
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
        };

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
        Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
        return failed == 0 ? 0 : 1;
    }

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

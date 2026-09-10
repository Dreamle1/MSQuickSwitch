using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WinQuickSwitch.Features.Product;
using WinQuickSwitch.Features.Widget;
using WinQuickSwitch.Platform.Windows;

namespace WinQuickSwitch;

public partial class App : Application
{
    private const string InstanceIdEnvironmentVariable =
        "WINQUICKSWITCH_INSTANCE_ID";

    private readonly CancellationTokenSource _activationCancellation = new();
    private readonly string _mutexName = GetScopedName(
        @"Local\WinQuickSwitch.Resident.Singleton.v2");
    private readonly string _activationEventName = GetScopedName(
        $@"Local\WinQuickSwitch.Resident.Activate.{ProductConfiguration.Default.Edition}.v2");
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private Task? _activationListener;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool isStartupLaunch = e.Args.Contains(
            WindowsStartupRegistrationService.StartupArgument,
            StringComparer.OrdinalIgnoreCase);
        bool isAttendedValidation = e.Args.Contains(
            "--attended-validation",
            StringComparer.OrdinalIgnoreCase);
        WidgetTheme.StartSystemThemeTracking();

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            _mutexName,
            out _ownsMutex);

        if (!_ownsMutex)
        {
            if (!isStartupLaunch && !SignalExistingInstance())
            {
                MessageBox.Show(
                    $"{ProductConfiguration.Default.DisplayName} cannot start " +
                    "because another WinQuickSwitch edition is already running.",
                    "WinQuickSwitch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        _activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            _activationEventName);
        MainWindow window = CreateMainWindow(e.Args);
        MainWindow = window;

        if (isAttendedValidation)
        {
            window.ShowForAttendedValidation();
        }
        else if (isStartupLaunch)
        {
            new WindowInteropHelper(window).EnsureHandle();
        }
        else
        {
            window.ShowConfiguredSurfaceFromStartup();
        }

        _activationListener = Task.Run(ListenForActivationRequests);
    }

    private static MainWindow CreateMainWindow(IReadOnlyList<string> arguments)
    {
#if WINQUICKSWITCH_E2E
        if (arguments.Contains(
                "--sandbox-e2e",
                StringComparer.OrdinalIgnoreCase))
        {
            return E2E.SandboxE2eRuntime.CreateMainWindow();
        }
#endif

        return new MainWindow();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        WidgetTheme.StopSystemThemeTracking();
        _activationCancellation.Cancel();
        _activationEvent?.Set();

        try
        {
            _activationListener?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // The application is already exiting.
        }

        _activationEvent?.Dispose();
        _activationCancellation.Dispose();

        if (_ownsMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void ListenForActivationRequests()
    {
        if (_activationEvent is null)
        {
            return;
        }

        WaitHandle[] handles =
        [
            _activationEvent,
            _activationCancellation.Token.WaitHandle,
        ];

        while (WaitHandle.WaitAny(handles) == 0)
        {
            Dispatcher.BeginInvoke(
                () => (MainWindow as WinQuickSwitch.MainWindow)?
                    .ShowFromExternalRequest(),
                DispatcherPriority.Normal);
        }
    }

    private bool SignalExistingInstance()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using EventWaitHandle existing =
                    EventWaitHandle.OpenExisting(_activationEventName);
                existing.Set();
                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
        }

        return false;
    }

    private static string GetScopedName(string baseName)
    {
        string? instanceId =
            Environment.GetEnvironmentVariable(InstanceIdEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return baseName;
        }

        string safeInstanceId = new(
            instanceId
                .Where(char.IsLetterOrDigit)
                .Take(32)
                .ToArray());

        return string.IsNullOrEmpty(safeInstanceId)
            ? baseName
            : $"{baseName}.{safeInstanceId}";
    }
}

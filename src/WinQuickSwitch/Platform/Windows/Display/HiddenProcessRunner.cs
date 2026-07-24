using System.Diagnostics;

namespace WinQuickSwitch.Platform.Windows.Display;

internal sealed class HiddenProcessRunner : IDisplaySwitchProcess
{
    public async Task<int> RunAsync(
        string executablePath,
        string arguments,
        CancellationToken cancellationToken)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            },
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Windows did not start the display switch process.");
        }

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}

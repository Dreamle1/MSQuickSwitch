using System.Windows;
using System.Windows.Controls;
using WinQuickSwitch.Features.Display;
using WinQuickSwitch.Platform.Windows.Display;

namespace WinQuickSwitch;

public partial class MainWindow : Window
{
    private readonly IDisplayModeService _displayModeService;
    private readonly CancellationTokenSource _lifetimeCancellation = new();

    public MainWindow() : this(new WindowsDisplayModeService())
    {
    }

    internal MainWindow(IDisplayModeService displayModeService)
    {
        _displayModeService = displayModeService;
        InitializeComponent();
    }

    protected override void OnClosed(EventArgs e)
    {
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        base.OnClosed(e);
    }

    private async void ApplyDisplayMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string modeName } ||
            !Enum.TryParse(modeName, out DisplayMode mode))
        {
            DisplayStatusText.Text = "That display mode is not recognized.";
            return;
        }

        if (RequiresConfirmation(mode) && !ConfirmDisplayChange(mode))
        {
            DisplayStatusText.Text = "Display change cancelled.";
            return;
        }

        DisplayModeButtons.IsEnabled = false;
        DisplayStatusText.Text = $"Switching to {mode.GetDisplayName()}...";

        try
        {
            DisplayModeResult result = await _displayModeService.ApplyAsync(
                mode,
                _lifetimeCancellation.Token);

            DisplayStatusText.Text = result.Message;
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // The window is closing; there is no status left to update.
        }
        finally
        {
            if (!_lifetimeCancellation.IsCancellationRequested)
            {
                DisplayModeButtons.IsEnabled = true;
            }
        }
    }

    private static bool RequiresConfirmation(DisplayMode mode) =>
        mode is DisplayMode.PcScreenOnly or DisplayMode.SecondScreenOnly;

    private bool ConfirmDisplayChange(DisplayMode mode)
    {
        MessageBoxResult answer = MessageBox.Show(
            this,
            $"{mode.GetDisplayName()} can turn off the display you are currently using. Continue?",
            "Confirm display change",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return answer == MessageBoxResult.Yes;
    }
}

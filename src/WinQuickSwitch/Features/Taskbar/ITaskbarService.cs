namespace WinQuickSwitch.Features.Taskbar;

public interface ITaskbarService
{
    TaskbarSnapshot GetSnapshot();

    TaskbarActionResult SetAutoHide(bool enabled);

    TaskbarActionResult OpenTaskbarSettings();

    TaskbarActionResult OpenDisplaySettings();

    TaskbarActionResult OpenNotificationSettings();
}

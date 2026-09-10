using WinQuickSwitch.Features.Product;

namespace WinQuickSwitch.Features.Widget;

internal static class WidgetPanelNavigation
{
    public static IReadOnlyList<WidgetPanel> GetAvailablePanels(
        ProductConfiguration configuration)
    {
        List<WidgetPanel> panels =
        [
            WidgetPanel.Display,
            WidgetPanel.Audio,
            WidgetPanel.Devices,
            WidgetPanel.Options,
        ];

        if (configuration.HasFeature(ProductFeature.Profiles))
        {
            panels.Add(WidgetPanel.Profiles);
        }

        panels.Add(WidgetPanel.About);
        return panels;
    }

    public static WidgetPanel? FromControlNumber(
        ProductConfiguration configuration,
        int number)
    {
        IReadOnlyList<WidgetPanel> panels = GetAvailablePanels(configuration);
        return number is >= 1 and <= 9 && number <= panels.Count
            ? panels[number - 1]
            : null;
    }
}
